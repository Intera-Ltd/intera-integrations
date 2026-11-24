import * as crypto from 'node:crypto';
import {
  ASSERTION_LIFETIME_SEC,
  GOOGLE_TOKEN_URL,
  MAX_TOKEN_RESPONSE_BYTES,
  REQUEST_TIMEOUT_MS,
  SHEETS_READONLY_SCOPE,
} from '../constants';
import type { IServiceAccount } from '../types';
import {
  extractPemSecrets,
  fetchBoundedText,
  redactSecrets,
  validateHttpsUrl,
} from './http';

/**
 * Service-account JWT-bearer authentication (RFC 7523) using Node's built-in
 * `node:crypto` RSA-SHA256 signer.
 *
 * Domain-wide delegation (`sub`) is intentionally omitted: sharing individual
 * spreadsheets with the service account's `client_email` keeps permissions
 * scoped strictly to those files.
 */

export interface IAccessToken {
  token: string;
  /** Epoch milliseconds. */
  expiresAt: number;
}

/** Parses and validates a Google Cloud service-account JSON key file. */
export function parseServiceAccount(rawJson: string): IServiceAccount {
  let parsed: unknown;
  try {
    parsed = JSON.parse(rawJson);
  } catch {
    throw new Error(
      'service_account_json is not valid JSON. Paste the key file Google ' +
        'Cloud downloaded, in full, including the outer braces.',
    );
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
    throw new Error('service_account_json must be a JSON object');
  }

  const record = parsed as Record<string, unknown>;
  const type = record['type'];
  if (type !== undefined && type !== 'service_account') {
    throw new Error(
      `service_account_json is a "${String(type)}" key file. This integration ` +
        'needs a service-account key: Google Cloud → IAM & Admin → Service ' +
        'Accounts → Keys → Add key → JSON.',
    );
  }

  const clientEmail = readNonEmptyString(record, 'client_email');
  const privateKey = normalizePem(readNonEmptyString(record, 'private_key'));
  const rawTokenUri = record['token_uri'];
  const tokenUri =
    typeof rawTokenUri === 'string' && rawTokenUri.trim().length > 0
      ? validateHttpsUrl(rawTokenUri, 'service_account_json "token_uri"')
      : GOOGLE_TOKEN_URL;

  return { clientEmail, privateKey, tokenUri };
}

/** SHA-256 fingerprint of the credential, used to scope in-memory caches. */
export function credentialFingerprint(account: IServiceAccount): string {
  return crypto
    .createHash('sha256')
    .update(`${account.clientEmail}\n${account.privateKey}\n${account.tokenUri}`)
    .digest('hex');
}

/** Signs an RS256 JWT assertion and exchanges it for a Google OAuth access token. */
export async function requestAccessToken(
  account: IServiceAccount,
  signal?: AbortSignal,
): Promise<IAccessToken> {
  const secrets = extractPemSecrets(account.privateKey);
  const issuedAt = Math.floor(Date.now() / 1000);
  const assertion = signAssertion(account, issuedAt, secrets);

  const response = await fetchBoundedText(
    account.tokenUri,
    {
      method: 'POST',
      headers: { 'content-type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'urn:ietf:params:oauth:grant-type:jwt-bearer',
        assertion,
      }).toString(),
    },
    {
      timeoutMs: REQUEST_TIMEOUT_MS,
      maxBytes: MAX_TOKEN_RESPONSE_BYTES,
      timeoutMessage: `Google token endpoint did not answer within ${REQUEST_TIMEOUT_MS} ms`,
      unreachablePrefix:
        `Cannot reach the Google token endpoint (${account.tokenUri}): `,
      sizeLabel: 'Google token endpoint',
      signal,
      secrets: [...secrets, assertion],
    },
  );

  if (!response.ok) {
    throw new Error(
      describeTokenFailure(response.status, response.body, [
        ...secrets,
        assertion,
      ]),
    );
  }

  let payload: { access_token?: unknown; expires_in?: unknown };
  try {
    payload = JSON.parse(response.body) as typeof payload;
  } catch {
    throw new Error('Google token endpoint answered with a non-JSON body');
  }
  if (typeof payload.access_token !== 'string' || !payload.access_token) {
    throw new Error('Google token endpoint answered without an access_token');
  }

  const lifetime =
    typeof payload.expires_in === 'number' && payload.expires_in > 60
      ? payload.expires_in
      : 3600;

  return {
    token: payload.access_token,
    expiresAt: Date.now() + lifetime * 1000,
  };
}

function readNonEmptyString(
  record: Record<string, unknown>,
  key: string,
): string {
  const value = record[key];
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new Error(`service_account_json has no "${key}"`);
  }
  return value;
}

function normalizePem(privateKey: string): string {
  const normalized = privateKey.includes('\\n')
    ? privateKey.replace(/\\n/g, '\n')
    : privateKey;
  if (!normalized.includes('-----BEGIN')) {
    throw new Error(
      'service_account_json: "private_key" is not a PEM block. It must start ' +
        'with -----BEGIN PRIVATE KEY-----.',
    );
  }
  return normalized;
}

function signAssertion(
  account: IServiceAccount,
  issuedAt: number,
  secrets: readonly string[],
): string {
  const header = base64Url(JSON.stringify({ alg: 'RS256', typ: 'JWT' }));
  const claims = base64Url(
    JSON.stringify({
      iss: account.clientEmail,
      scope: SHEETS_READONLY_SCOPE,
      aud: account.tokenUri,
      iat: issuedAt,
      exp: issuedAt + ASSERTION_LIFETIME_SEC,
    }),
  );

  let signature: string;
  try {
    signature = crypto
      .createSign('RSA-SHA256')
      .update(`${header}.${claims}`)
      .sign(account.privateKey, 'base64');
  } catch (err) {
    const detail = redactSecrets(
      err instanceof Error ? err.message : String(err),
      secrets,
    );
    throw new Error(
      'service_account_json: "private_key" could not be used to sign — the ' +
        `key is malformed or truncated (${detail})`,
    );
  }

  return `${header}.${claims}.${toBase64Url(signature)}`;
}

function base64Url(text: string): string {
  return toBase64Url(Buffer.from(text, 'utf8').toString('base64'));
}

function toBase64Url(base64: string): string {
  return base64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function describeTokenFailure(
  status: number,
  body: string,
  secrets: readonly string[],
): string {
  let code = '';
  let description = '';
  try {
    const parsed = JSON.parse(body) as {
      error?: unknown;
      error_description?: unknown;
    };
    if (typeof parsed.error === 'string') {
      code = redactSecrets(parsed.error, secrets);
    }
    if (typeof parsed.error_description === 'string') {
      description = redactSecrets(parsed.error_description, secrets);
    }
  } catch {
    // Never include raw non-JSON upstream response bodies in thrown errors.
    description = '';
  }

  if (code === 'invalid_grant') {
    return (
      'Google refused the service-account key (invalid_grant). Either the key ' +
      'was deleted or disabled in Google Cloud, or the clock of the host ' +
      `running Core is off by more than a few minutes. ${description}`.trimEnd()
    );
  }
  if (code === 'unauthorized_client') {
    return (
      'Google refused the service account (unauthorized_client): the Google ' +
      'Sheets API is not enabled for its project. Enable it in Google Cloud → ' +
      `APIs & Services → Library. ${description}`.trimEnd()
    );
  }
  if (code === 'invalid_client') {
    return (
      'Google does not know this service account (invalid_client) — it was ' +
      `deleted, or client_email does not match the key. ${description}`.trimEnd()
    );
  }
  const parts = [`Google token endpoint answered ${status}`, code, description]
    .filter((part) => part.length > 0)
    .join(' ');
  return parts;
}
