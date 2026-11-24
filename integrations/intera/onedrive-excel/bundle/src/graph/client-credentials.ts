import * as crypto from 'node:crypto';
import {
  GRAPH_DEFAULT_SCOPE,
  LOGIN_BASE,
  MAX_JSON_RESPONSE_BYTES,
  REQUEST_TIMEOUT_MS,
} from '../constants';
import type { IAppCredentials } from '../types';
import { fetchBoundedText, redactSecrets } from './http';

/**
 * Microsoft Entra ID OAuth 2.0 v2.0 `client_credentials` token exchange.
 */

export interface IAccessToken {
  token: string;
  /** Epoch milliseconds. */
  expiresAt: number;
  /**
   * Unverified `roles` claim from the JWT payload, used strictly to format
   * actionable 403/404 diagnostic messages (distinguishing missing admin
   * consent from missing per-file grant).
   */
  roles: string[];
}

/** SHA-256 fingerprint of the app credential, used to scope in-memory caches. */
export function credentialFingerprint(credentials: IAppCredentials): string {
  return crypto
    .createHash('sha256')
    .update(
      `${credentials.tenantId.toLowerCase()}\n${credentials.clientId.toLowerCase()}\n${credentials.clientSecret}`,
    )
    .digest('hex');
}

export async function requestAccessToken(
  credentials: IAppCredentials,
  signal?: AbortSignal,
): Promise<IAccessToken> {
  const tokenUrl = `${LOGIN_BASE}/${encodeURIComponent(credentials.tenantId)}/oauth2/v2.0/token`;
  const secrets = [credentials.clientSecret];

  const response = await fetchBoundedText(
    tokenUrl,
    {
      method: 'POST',
      headers: { 'content-type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'client_credentials',
        client_id: credentials.clientId,
        client_secret: credentials.clientSecret,
        scope: GRAPH_DEFAULT_SCOPE,
      }).toString(),
    },
    {
      timeoutMs: REQUEST_TIMEOUT_MS,
      maxBytes: MAX_JSON_RESPONSE_BYTES,
      timeoutMessage: `Microsoft sign-in did not answer within ${REQUEST_TIMEOUT_MS} ms`,
      unreachablePrefix: `Cannot reach Microsoft sign-in (${LOGIN_BASE}): `,
      sizeLabel: 'Microsoft sign-in',
      signal,
      secrets,
    },
  );

  if (!response.ok) {
    throw new Error(
      describeTokenFailure(response.status, response.body, credentials, secrets),
    );
  }

  let payload: { access_token?: unknown; expires_in?: unknown };
  try {
    payload = JSON.parse(response.body) as typeof payload;
  } catch {
    throw new Error('Microsoft sign-in answered with a non-JSON body');
  }
  if (typeof payload.access_token !== 'string' || !payload.access_token) {
    throw new Error('Microsoft sign-in answered without an access_token');
  }

  const expiresIn = Number(payload.expires_in);
  const lifetime =
    Number.isFinite(expiresIn) && expiresIn > 60 ? expiresIn : 3600;

  return {
    token: payload.access_token,
    expiresAt: Date.now() + lifetime * 1000,
    roles: readRolesClaim(payload.access_token),
  };
}

function readRolesClaim(token: string): string[] {
  const payload = token.split('.')[1];
  if (payload === undefined) {
    return [];
  }
  try {
    const claims = JSON.parse(
      Buffer.from(payload, 'base64url').toString('utf8'),
    ) as { roles?: unknown };
    return Array.isArray(claims.roles)
      ? claims.roles.filter((role): role is string => typeof role === 'string')
      : [];
  } catch {
    return [];
  }
}

function describeTokenFailure(
  status: number,
  body: string,
  credentials: IAppCredentials,
  secrets: readonly string[],
): string {
  let codes: number[] = [];
  let description = '';
  try {
    const parsed = JSON.parse(body) as {
      error?: unknown;
      error_description?: unknown;
      error_codes?: unknown;
    };
    if (Array.isArray(parsed.error_codes)) {
      codes = parsed.error_codes.filter(
        (code): code is number => typeof code === 'number',
      );
    }
    if (typeof parsed.error_description === 'string') {
      const firstLine = parsed.error_description.split(/\r?\n/)[0] ?? '';
      description = redactSecrets(firstLine, secrets);
    } else if (typeof parsed.error === 'string') {
      description = redactSecrets(parsed.error, secrets);
    }
  } catch {
    // Never include raw non-JSON upstream response bodies in thrown errors.
    description = '';
  }

  const has = (code: number): boolean => codes.includes(code);

  if (has(7000215)) {
    return (
      'Microsoft rejected the client secret (AADSTS7000215). Paste the ' +
      "secret's Value, not its Secret ID — the portal shows the Value only " +
      'once, right after the secret is created.'
    );
  }
  if (has(7000222)) {
    return (
      'The client secret has expired (AADSTS7000222). Create a new one under ' +
      'Certificates & secrets of the app registration and update the instance.'
    );
  }
  if (has(700016)) {
    return (
      `No application ${credentials.clientId} in tenant ${credentials.tenantId} ` +
      '(AADSTS700016). Check the Application (client) ID, and that the tenant ' +
      'is the one the app is registered in.'
    );
  }
  if (has(90002) || has(900023)) {
    return (
      `Tenant "${credentials.tenantId}" does not exist (${codes.map((code) => `AADSTS${code}`).join(', ')}). ` +
      'Use the Directory (tenant) ID from the app registration overview.'
    );
  }
  return `Microsoft sign-in answered ${status}. ${description}`.trimEnd();
}
