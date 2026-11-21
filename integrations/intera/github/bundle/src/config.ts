import {
  ACCESS_TOKEN_CONFIG_FIELD,
  API_URL_CONFIG_FIELD,
  APP_ID_CONFIG_FIELD,
  APP_PRIVATE_KEY_CONFIG_FIELD,
  DEFAULT_HISTORY_DAYS,
  GITHUB_API_BASE_URL,
  HISTORY_DAYS_CONFIG_FIELD,
  INCLUDE_ARCHIVED_CONFIG_FIELD,
  INSTALLATION_ID_CONFIG_FIELD,
  OWNER_CONFIG_FIELD,
  REPOSITORIES_CONFIG_FIELD,
} from './constants';
import type { IDataSourceContext } from './types';

/** A personal access token (fine-grained or classic): one long-lived bearer secret acting as its user. */
export interface ITokenCredential {
  kind: 'token';
  token: string;
}

/**
 * A GitHub App: its id and private key sign a JWT, which is traded for an hour-long installation token. The
 * installation is the app's on the instance's owner unless one is named.
 */
export interface IAppCredential {
  kind: 'app';
  appId: string;
  privateKeyPem: string;
  installationId?: string;
}

export interface IGitHubConfig {
  credential: ITokenCredential | IAppCredential;
  /** The organization or user whose repositories the instance reads. */
  owner: string;
  /** Repository names the instance reads (bare, or `owner/name` with the instance's owner); empty means all. */
  repositories: string[];
  includeArchivedRepositories: boolean;
  /** Days the event-like sources read when the filter gives no lower bound. */
  historyDays: number;
  /** REST API root, without a trailing slash: `https://api.github.com`, or a GitHub Enterprise Server's `/api/v3`. */
  apiUrl: string;
}

function readString(configJson: Record<string, unknown>, fieldName: string): string | undefined {
  const value = configJson[fieldName];
  if (typeof value === 'number' && Number.isFinite(value)) {
    return String(value);
  }
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : undefined;
}

/**
 * Booleans arrive as real booleans from a `kind: 'boolean'` config field, but a YAML- or API-authored instance can
 * just as easily send the string. Both are accepted; anything else falls back to the default rather than silently
 * reading as `false`.
 */
function readBoolean(configJson: Record<string, unknown>, fieldName: string, fallback: boolean): boolean {
  const value = configJson[fieldName];
  if (typeof value === 'boolean') {
    return value;
  }
  if (typeof value === 'string') {
    const token = value.trim().toLowerCase();
    if (token === 'true' || token === 'yes' || token === '1') return true;
    if (token === 'false' || token === 'no' || token === '0') return false;
  }
  return fallback;
}

/** A list typed into one text field: comma-, space- or newline-separated (repository names contain none of them). */
function readList(configJson: Record<string, unknown>, fieldName: string): string[] {
  const value = readString(configJson, fieldName);
  if (value === undefined) {
    return [];
  }
  return [...new Set(value.split(/[,\s]+/).map((entry) => entry.trim()).filter((entry) => entry.length > 0))];
}

/**
 * The private key as a PEM the crypto module accepts, whatever a secret field did to it.
 *
 * A one-line input collapses the file's newlines into spaces, or a YAML-authored instance carries them as `\n`
 * escapes, or the admin pasted the whole file base64-encoded. The body is rebuilt from its base64 between the two
 * armour lines, which reads all of them the same way.
 */
export function normalizePrivateKey(raw: string): string {
  let text = raw.trim().replace(/\\n/g, '\n');
  if (!text.includes('-----BEGIN')) {
    const decoded = Buffer.from(text.replace(/\s+/g, ''), 'base64').toString('utf8');
    if (!decoded.includes('-----BEGIN')) {
      throw new Error(
        `Config field "${APP_PRIVATE_KEY_CONFIG_FIELD}" is not a PEM private key — paste the whole .pem file GitHub ` +
          'downloaded for the app, including its BEGIN and END lines',
      );
    }
    text = decoded.trim();
  }
  const match = /-----BEGIN ([A-Z ]+)-----([\s\S]*?)-----END \1-----/.exec(text);
  if (match === null) {
    throw new Error(`Config field "${APP_PRIVATE_KEY_CONFIG_FIELD}" has a BEGIN line without its matching END line`);
  }
  const label = match[1];
  const body = match[2].replace(/\s+/g, '');
  const lines = body.match(/.{1,64}/g) ?? [];
  return `-----BEGIN ${label}-----\n${lines.join('\n')}\n-----END ${label}-----\n`;
}

function readApiUrl(configJson: Record<string, unknown>): string {
  const value = readString(configJson, API_URL_CONFIG_FIELD);
  if (value === undefined) {
    return GITHUB_API_BASE_URL;
  }
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new Error(`Config field "${API_URL_CONFIG_FIELD}" is not a URL: ${value}`);
  }
  if (url.protocol !== 'https:' && url.protocol !== 'http:') {
    throw new Error(`Config field "${API_URL_CONFIG_FIELD}" must be an http(s) URL: ${value}`);
  }
  return url.toString().replace(/\/+$/, '');
}

function readHistoryDays(configJson: Record<string, unknown>): number {
  const raw = configJson[HISTORY_DAYS_CONFIG_FIELD];
  if (raw === undefined || raw === null || raw === '') {
    return DEFAULT_HISTORY_DAYS;
  }
  const value = typeof raw === 'number' ? raw : Number(String(raw).trim());
  if (!Number.isInteger(value) || value < 1 || value > 3_650) {
    throw new Error(`Config field "${HISTORY_DAYS_CONFIG_FIELD}" must be a whole number of days from 1 to 3650`);
  }
  return value;
}

/** `owner/name` or `name`, as the bare name; another owner is refused rather than quietly read as this one. */
function repositoryName(entry: string, owner: string): string {
  const slash = entry.indexOf('/');
  if (slash < 0) {
    return entry;
  }
  const entryOwner = entry.slice(0, slash);
  if (entryOwner.toLowerCase() !== owner.toLowerCase()) {
    throw new Error(
      `Config field "${REPOSITORIES_CONFIG_FIELD}": "${entry}" belongs to ${entryOwner}, not to the instance's ` +
        `owner ${owner} — one instance reads one owner`,
    );
  }
  return entry.slice(slash + 1);
}

/** Reads and validates an instance config. Throws with an admin-readable reason. */
export function parseGitHubConfig(configJson: Record<string, unknown>): IGitHubConfig {
  const token = readString(configJson, ACCESS_TOKEN_CONFIG_FIELD);
  const appId = readString(configJson, APP_ID_CONFIG_FIELD);
  const privateKey = readString(configJson, APP_PRIVATE_KEY_CONFIG_FIELD);

  let credential: ITokenCredential | IAppCredential;
  if (token !== undefined && (appId !== undefined || privateKey !== undefined)) {
    throw new Error(
      `Fill either "${ACCESS_TOKEN_CONFIG_FIELD}" (a personal access token) or "${APP_ID_CONFIG_FIELD}" with ` +
        `"${APP_PRIVATE_KEY_CONFIG_FIELD}" (a GitHub App), not both`,
    );
  }
  if (token !== undefined) {
    credential = { kind: 'token', token };
  } else if (appId !== undefined && privateKey !== undefined) {
    credential = {
      kind: 'app',
      appId,
      privateKeyPem: normalizePrivateKey(privateKey),
      installationId: readString(configJson, INSTALLATION_ID_CONFIG_FIELD),
    };
  } else if (appId !== undefined || privateKey !== undefined) {
    throw new Error(`A GitHub App needs both "${APP_ID_CONFIG_FIELD}" and "${APP_PRIVATE_KEY_CONFIG_FIELD}"`);
  } else {
    throw new Error(
      `Config field "${ACCESS_TOKEN_CONFIG_FIELD}" is required (or "${APP_ID_CONFIG_FIELD}" with ` +
        `"${APP_PRIVATE_KEY_CONFIG_FIELD}" for a GitHub App)`,
    );
  }

  const owner = readString(configJson, OWNER_CONFIG_FIELD);
  if (owner === undefined) {
    throw new Error(`Config field "${OWNER_CONFIG_FIELD}" is required — the organization or user to read`);
  }

  return {
    credential,
    owner,
    repositories: [...new Set(readList(configJson, REPOSITORIES_CONFIG_FIELD).map((entry) => repositoryName(entry, owner)))],
    includeArchivedRepositories: readBoolean(configJson, INCLUDE_ARCHIVED_CONFIG_FIELD, false),
    historyDays: readHistoryDays(configJson),
    apiUrl: readApiUrl(configJson),
  };
}

export function readGitHubConfig(context: IDataSourceContext): IGitHubConfig {
  if (context.config === undefined) {
    throw new Error('Integration instance config is required. Pass ?instanceId=<uuid> when calling this DataSource.');
  }
  return parseGitHubConfig(context.config);
}
