import {
  ACCESS_TOKEN_CONFIG_FIELD,
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
  };
}

export function readAsanaConfig(context: IDataSourceContext): IAsanaConfig {
  if (context.config === undefined) {
    throw new Error('Integration instance config is required. Pass ?instanceId=<uuid> when calling this DataSource.');
  }
  return parseAsanaConfig(context.config);
}
