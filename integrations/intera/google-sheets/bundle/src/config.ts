import {
  CREDENTIALS_CONFIG_FIELD,
  FIRST_ROW_HEADER_CONFIG_FIELD,
  IDENTITY_COLUMN_CONFIG_FIELD,
  SHEET_NAME_CONFIG_FIELD,
  SKIP_ROWS_CONFIG_FIELD,
  SPREADSHEET_CONFIG_FIELD,
  WEEK_START_CONFIG_FIELD,
} from './constants';
import { parseServiceAccount } from './google/service-account';
import type { IDataSourceContext, IGsheetConfig } from './types';

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

const SPREADSHEET_URL_ID_RE = /\/spreadsheets\/d\/([a-zA-Z0-9-_]+)/;
const SPREADSHEET_ID_RE = /^[a-zA-Z0-9-_]{20,}$/;
const GID_RE = /[#?&]gid=(\d+)/;

export function readGsheetConfig(context: IDataSourceContext): IGsheetConfig {
  if (context.config === undefined) {
    throw new Error(
      'Integration instance config is required. Pass ?instanceId=<uuid> when calling this DataSource.',
    );
  }
  return parseGsheetConfig(context.config);
}

/** Reads and validates an instance config. Throws with an admin-readable reason. */
export function parseGsheetConfig(
  configJson: Record<string, unknown>,
): IGsheetConfig {
  const spreadsheet = readString(configJson, SPREADSHEET_CONFIG_FIELD);
  if (spreadsheet === null) {
    throw new Error(`Config field "${SPREADSHEET_CONFIG_FIELD}" is required`);
  }
  const credentialsJson = readString(configJson, CREDENTIALS_CONFIG_FIELD);
  if (credentialsJson === null) {
    throw new Error(`Config field "${CREDENTIALS_CONFIG_FIELD}" is required`);
  }

  const { spreadsheetId, sheetGid } = parseSpreadsheetReference(spreadsheet);

  return {
    spreadsheetId,
    sheetName: readString(configJson, SHEET_NAME_CONFIG_FIELD),
    sheetGid,
    credentials: parseServiceAccount(credentialsJson),
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

/**
 * Extracts the spreadsheet id and optional `#gid=...` tab hint from either a
 * full Google Sheets browser URL or a bare spreadsheet id.
 */
export function parseSpreadsheetReference(raw: string): {
  spreadsheetId: string;
  sheetGid: number | null;
} {
  const fromUrl = SPREADSHEET_URL_ID_RE.exec(raw);
  if (fromUrl !== null) {
    const gid = GID_RE.exec(raw);
    return {
      spreadsheetId: fromUrl[1],
      sheetGid: gid === null ? null : Number(gid[1]),
    };
  }

  if (!SPREADSHEET_ID_RE.test(raw)) {
    throw new Error(
      `Config field "${SPREADSHEET_CONFIG_FIELD}" must be a spreadsheet id or ` +
        'the URL of the spreadsheet (https://docs.google.com/spreadsheets/d/…)',
    );
  }
  return { spreadsheetId: raw, sheetGid: null };
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
  const parsed = typeof value === 'number' ? value : Number(String(value).trim());
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
