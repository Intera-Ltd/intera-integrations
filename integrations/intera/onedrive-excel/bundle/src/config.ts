import {
  CLIENT_ID_CONFIG_FIELD,
  CLIENT_SECRET_CONFIG_FIELD,
  DRIVE_ID_CONFIG_FIELD,
  FIRST_ROW_HEADER_CONFIG_FIELD,
  IDENTITY_COLUMN_CONFIG_FIELD,
  ITEM_ID_CONFIG_FIELD,
  SHEET_NAME_CONFIG_FIELD,
  SKIP_ROWS_CONFIG_FIELD,
  TENANT_ID_CONFIG_FIELD,
  WEEK_START_CONFIG_FIELD,
} from './constants';
import type { IDataSourceContext, IExcelConfig } from './types';

/** Maximum rows that may be skipped above the table. */
const MAX_SKIP_ROWS = 10_000;

const WEEK_DAYS = [
  'sunday',
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
];

const GUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Verified tenant domain (`contoso.onmicrosoft.com`, `contoso.com`). */
const TENANT_DOMAIN_RE = /^(?=.{4,253}$)[a-z0-9-]+(?:\.[a-z0-9-]+)+$/i;

/** Multi-tenant authorities rejected for single-directory client-credentials sign-in. */
const MULTI_TENANT_AUTHORITIES = new Set([
  'common',
  'organizations',
  'consumers',
]);

/** Microsoft Graph bare drive/item identifier character set. */
const GRAPH_ID_RE = /^[A-Za-z0-9!_\-]{8,}$/;

export function readExcelConfig(context: IDataSourceContext): IExcelConfig {
  if (context.config === undefined) {
    throw new Error(
      'Integration instance config is required. Pass ?instanceId=<uuid> when calling this DataSource.',
    );
  }
  return parseExcelConfig(context.config);
}

/** Reads and validates an instance config. Throws with an admin-readable reason. */
export function parseExcelConfig(
  configJson: Record<string, unknown>,
): IExcelConfig {
  return {
    credentials: {
      tenantId: readTenantId(configJson),
      clientId: readGuid(configJson, CLIENT_ID_CONFIG_FIELD),
      clientSecret: requireString(configJson, CLIENT_SECRET_CONFIG_FIELD),
    },
    driveId: readGraphId(configJson, DRIVE_ID_CONFIG_FIELD),
    itemId: readGraphId(configJson, ITEM_ID_CONFIG_FIELD),
    sheetName: readString(configJson, SHEET_NAME_CONFIG_FIELD),
    isFirstRowHeader: readBoolean(
      configJson,
      FIRST_ROW_HEADER_CONFIG_FIELD,
      true,
    ),
    skipRows: readSkipRows(configJson),
    identityColumn: readString(configJson, IDENTITY_COLUMN_CONFIG_FIELD),
    weekStart: readWeekStart(configJson),
  };
}

function readTenantId(configJson: Record<string, unknown>): string {
  const tenantId = requireString(configJson, TENANT_ID_CONFIG_FIELD);
  if (MULTI_TENANT_AUTHORITIES.has(tenantId.toLowerCase())) {
    throw new Error(
      `Config field "${TENANT_ID_CONFIG_FIELD}" must name one tenant — its ` +
        `Directory (tenant) ID or a verified domain. "${tenantId}" is a ` +
        'multi-tenant sign-in address, and an app signing in as itself has ' +
        'to be told which directory it belongs to.',
    );
  }
  if (!GUID_RE.test(tenantId) && !TENANT_DOMAIN_RE.test(tenantId)) {
    throw new Error(
      `Config field "${TENANT_ID_CONFIG_FIELD}" must be a Directory (tenant) ID ` +
        '(a GUID) or a domain such as contoso.onmicrosoft.com',
    );
  }
  return tenantId;
}

function readGuid(
  configJson: Record<string, unknown>,
  fieldName: string,
): string {
  const value = requireString(configJson, fieldName);
  if (!GUID_RE.test(value)) {
    throw new Error(`Config field "${fieldName}" must be a GUID`);
  }
  return value;
}

function readGraphId(
  configJson: Record<string, unknown>,
  fieldName: string,
): string {
  const value = requireString(configJson, fieldName);
  if (!GRAPH_ID_RE.test(value)) {
    throw new Error(
      `Config field "${fieldName}" must be the bare id Microsoft Graph reports ` +
        '(for example from GET /drives/{drive-id}/items/{item-id}), not a URL',
    );
  }
  return value;
}

function readBoolean(
  configJson: Record<string, unknown>,
  fieldName: string,
  fallback: boolean,
): boolean {
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

function readSkipRows(configJson: Record<string, unknown>): number {
  const value = configJson[SKIP_ROWS_CONFIG_FIELD];
  if (value === undefined || value === null || value === '') {
    return 0;
  }
  const parsed =
    typeof value === 'number' ? value : Number(String(value).trim());
  if (!Number.isInteger(parsed) || parsed < 0 || parsed > MAX_SKIP_ROWS) {
    throw new Error(
      `Config field "${SKIP_ROWS_CONFIG_FIELD}" must be a whole number between 0 and ${MAX_SKIP_ROWS}`,
    );
  }
  return parsed;
}

function readWeekStart(configJson: Record<string, unknown>): number {
  const raw = readString(configJson, WEEK_START_CONFIG_FIELD);
  if (raw === null) {
    return 1;
  }
  const index = WEEK_DAYS.indexOf(raw.toLowerCase());
  if (index < 0) {
    throw new Error(
      `Config field "${WEEK_START_CONFIG_FIELD}" must be one of: ${WEEK_DAYS.join(', ')}`,
    );
  }
  return index;
}

function requireString(
  configJson: Record<string, unknown>,
  fieldName: string,
): string {
  const value = readString(configJson, fieldName);
  if (value === null) {
    throw new Error(`Config field "${fieldName}" is required`);
  }
  return value;
}

function readString(
  configJson: Record<string, unknown>,
  fieldName: string,
): string | null {
  const value = configJson[fieldName];
  if (typeof value !== 'string') {
    return null;
  }
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}
