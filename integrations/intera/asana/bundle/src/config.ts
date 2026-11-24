import {
  ACCESS_TOKEN_CONFIG_FIELD,
  API_URL_CONFIG_FIELD,
  ASANA_API_BASE_URL,
  INCLUDE_ARCHIVED_CONFIG_FIELD,
  INCLUDE_SUBTASKS_CONFIG_FIELD,
  PROJECTS_CONFIG_FIELD,
  WORKSPACE_CONFIG_FIELD,
} from './constants';
import type { IDataSourceContext } from './types';

export interface IAsanaConfig {
  /** A personal access token or a service account token; both are long-lived bearer secrets. */
  accessToken: string;
  /** Workspace gid or name; absent means the token's only workspace. */
  workspace?: string;
  /** Project gids or names the instance reads; empty means every project the token can see. */
  projects: string[];
  includeArchivedProjects: boolean;
  includeSubtasks: boolean;
  /** Asana REST API root without a trailing slash; defaults to {@link ASANA_API_BASE_URL}. */
  apiUrl: string;
}

const LOOPBACK_HOSTNAMES = new Set(['localhost', '127.0.0.1', '[::1]']);

export function readAsanaConfig(context: IDataSourceContext): IAsanaConfig {
  if (context.config === undefined) {
    throw new Error('Integration instance config is required. Pass ?instanceId=<uuid> when calling this DataSource.');
  }
  return parseAsanaConfig(context.config);
}

/** Reads and validates an instance config. Throws with an admin-readable reason. */
export function parseAsanaConfig(configJson: Record<string, unknown>): IAsanaConfig {
  const accessToken = readString(configJson, ACCESS_TOKEN_CONFIG_FIELD);
  if (accessToken === undefined) {
    throw new Error(`Config field "${ACCESS_TOKEN_CONFIG_FIELD}" is required`);
  }
  return {
    accessToken,
    workspace: readString(configJson, WORKSPACE_CONFIG_FIELD),
    projects: readList(configJson, PROJECTS_CONFIG_FIELD),
    includeArchivedProjects: readBoolean(configJson, INCLUDE_ARCHIVED_CONFIG_FIELD, false),
    includeSubtasks: readBoolean(configJson, INCLUDE_SUBTASKS_CONFIG_FIELD, true),
    apiUrl: readApiUrl(configJson),
  };
}

/**
 * Validates that an Asana API base URL uses `https:` (or `http:` exclusively
 * on loopback `localhost`, `127.0.0.1`, `[::1]`) and carries neither userinfo
 * credentials nor a URL fragment.
 */
export function validateApiUrl(rawUrl: string, label = `Config field "${API_URL_CONFIG_FIELD}"`): string {
  const value = rawUrl.trim();
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new Error(`${label} is not a valid URL`);
  }
  if (url.protocol !== 'https:' && url.protocol !== 'http:') {
    throw new Error(`${label} must be an https URL (http(s) URL is only allowed on loopback)`);
  }
  if (url.protocol === 'http:' && !LOOPBACK_HOSTNAMES.has(url.hostname.toLowerCase())) {
    throw new Error(
      `${label} must use https (plain http is only allowed for loopback hosts localhost, 127.0.0.1, [::1])`,
    );
  }
  if (url.username !== '' || url.password !== '' || hasAuthorityUserinfo(value, url.protocol)) {
    throw new Error(`${label} must not contain userinfo credentials`);
  }
  if (url.hash !== '' || value.includes('#')) {
    throw new Error(`${label} must not contain a URL fragment`);
  }
  return url.toString().replace(/\/+$/, '');
}

function readApiUrl(configJson: Record<string, unknown>): string {
  const value = readString(configJson, API_URL_CONFIG_FIELD);
  if (value === undefined) {
    return ASANA_API_BASE_URL;
  }
  return validateApiUrl(value, `Config field "${API_URL_CONFIG_FIELD}"`);
}

function hasAuthorityUserinfo(rawUrl: string, protocol: string): boolean {
  const rest = rawUrl.slice(protocol.length + 2);
  const boundary = rest.search(/[/?#]/);
  const authority = boundary < 0 ? rest : rest.slice(0, boundary);
  return authority.includes('@');
}

function readString(configJson: Record<string, unknown>, fieldName: string): string | undefined {
  const value = configJson[fieldName];
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

/**
 * A list typed into one text field: comma- or newline-separated. A project name may itself contain a comma, which is
 * why a gid is the reliable spelling and the label says so.
 */
function readList(configJson: Record<string, unknown>, fieldName: string): string[] {
  const value = readString(configJson, fieldName);
  if (value === undefined) {
    return [];
  }
  return [...new Set(value.split(/[,\n]/).map((entry) => entry.trim()).filter((entry) => entry.length > 0))];
}
