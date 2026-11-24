export const INTEGRATION_ID = 'intera-gsheets-integration';
export const INTEGRATION_NAME = 'Google Sheets';
export const INTEGRATION_VERSION = '1.3.1';

export const DIRECT_API_KIND = 'readonly_api' as const;
export const DATA_SOURCE_CAPABILITY = 'datasource';
export const DEFAULT_CACHE_TTL_SEC = 300;

/** Config fields. Names are also the keys the datasource reads at query time. */
export const SPREADSHEET_CONFIG_FIELD = 'spreadsheet';
export const SHEET_NAME_CONFIG_FIELD = 'sheet_name';
export const CREDENTIALS_CONFIG_FIELD = 'service_account_json';
export const FIRST_ROW_HEADER_CONFIG_FIELD = 'is_first_row_header';
export const SKIP_ROWS_CONFIG_FIELD = 'skip_rows';
export const IDENTITY_COLUMN_CONFIG_FIELD = 'identity_column';
export const WEEK_START_CONFIG_FIELD = 'week_start';

export const SHEET_ROWS_SOURCE_ID = 'gsheet-rows';
export const SHEET_ROWS_SOURCE_NAME = 'Google Sheets rows';
export const SHEET_ROWS_SOURCE_DESCRIPTION =
  'Rows of the configured sheet tab. Columns are read from the tab when the ' +
  'instance is saved, so this list reflects what that tab contains — ' +
  're-save the instance after the header row changes.';

/**
 * Every other tab of the book is a datasource of its own that provisioning
 * adds on the `gsheet-rows` template: `gsheet-tab-<gid>`.
 */
export const TAB_SOURCE_ID_PREFIX = 'gsheet-tab-';

/**
 * Read-only scope for Google Sheets API v4. Nothing in this integration writes,
 * so tokens are strictly scoped to read-only spreadsheet access.
 */
export const SHEETS_READONLY_SCOPE =
  'https://www.googleapis.com/auth/spreadsheets.readonly';

export const GOOGLE_TOKEN_URL = 'https://oauth2.googleapis.com/token';
export const SHEETS_API_BASE = 'https://sheets.googleapis.com/v4/spreadsheets';

/** An access token is valid for an hour and renewed this early before expiry. */
export const TOKEN_REFRESH_SKEW_SEC = 120;

/** Lifetime of the signed JWT client assertion. Google refuses more than an hour. */
export const ASSERTION_LIFETIME_SEC = 3600;

export const REQUEST_TIMEOUT_MS = 30_000;

/** Maximum size of a JSON response read from Google Sheets API (64 MiB). */
export const MAX_RESPONSE_BYTES = 64 * 1024 * 1024;

/** Maximum size of an OAuth token response read from the token endpoint (2 MiB). */
export const MAX_TOKEN_RESPONSE_BYTES = 2 * 1024 * 1024;

/** Maximum number of service-account access tokens cached in memory (LRU). */
export const MAX_TOKEN_CACHE_ENTRIES = 64;

/**
 * Number of data rows sampled when inferring column types. A column is typed as
 * `number`, `date`, or `boolean` only when every non-blank sampled value matches
 * that type; mixed columns fall back to `string`.
 */
export const SCHEMA_SAMPLE_ROWS = 500;

/**
 * Hard cap on the data rows returned by one tab read, enforced directly in the
 * requested A1 range so oversized tabs do not materialize unbounded responses.
 */
export const MAX_DATA_ROWS = 100_000;
