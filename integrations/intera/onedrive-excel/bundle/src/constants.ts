export const INTEGRATION_ID = 'intera-onedrive-excel-integration';
export const INTEGRATION_NAME = 'Excel (OneDrive / SharePoint)';
export const INTEGRATION_VERSION = '3.3.0';

export const DIRECT_API_KIND = 'readonly_api' as const;
export const DATA_SOURCE_CAPABILITY = 'datasource';
export const DEFAULT_CACHE_TTL_SEC = 300;

/** Config fields. Names are also the keys the datasource reads at query time. */
export const TENANT_ID_CONFIG_FIELD = 'tenant_id';
export const CLIENT_ID_CONFIG_FIELD = 'client_id';
export const CLIENT_SECRET_CONFIG_FIELD = 'client_secret';
export const DRIVE_ID_CONFIG_FIELD = 'drive_id';
export const ITEM_ID_CONFIG_FIELD = 'item_id';
export const SHEET_NAME_CONFIG_FIELD = 'sheet_name';
export const FIRST_ROW_HEADER_CONFIG_FIELD = 'is_first_row_header';
export const SKIP_ROWS_CONFIG_FIELD = 'skip_rows';
export const IDENTITY_COLUMN_CONFIG_FIELD = 'identity_column';
export const WEEK_START_CONFIG_FIELD = 'week_start';

export const SHEET_ROWS_SOURCE_ID = 'excel-rows';
export const SHEET_ROWS_SOURCE_NAME = 'Excel worksheet rows';
export const SHEET_ROWS_SOURCE_DESCRIPTION =
  'Rows of the configured worksheet. Columns are read from the workbook when ' +
  'the instance is saved, so this list reflects what that sheet contains — ' +
  're-save the instance after the header row changes.';

export const LOGIN_BASE = 'https://login.microsoftonline.com';
export const GRAPH_API_BASE = 'https://graph.microsoft.com/v1.0';

/**
 * The client-credentials grant uses `.default`, which requests the application
 * permissions consented for the Entra ID app registration (such as
 * `Files.SelectedOperations.Selected` or `Sites.Selected`).
 */
export const GRAPH_DEFAULT_SCOPE = 'https://graph.microsoft.com/.default';

/** Access tokens live 60–90 minutes and are renewed this early before expiry. */
export const TOKEN_REFRESH_SKEW_SEC = 120;

export const REQUEST_TIMEOUT_MS = 30_000;

/** Workbook binary downloads allow a longer timeout than JSON metadata calls. */
export const DOWNLOAD_TIMEOUT_MS = 120_000;

/** Maximum compressed `.xlsx` workbook size downloaded from Graph/SharePoint (64 MiB). */
export const MAX_FILE_BYTES = 64 * 1024 * 1024;

/** Maximum JSON response size read from Entra ID or Microsoft Graph (32 MiB). */
export const MAX_JSON_RESPONSE_BYTES = 32 * 1024 * 1024;

/** Maximum uncompressed size of any single XML part inflated from the `.xlsx` archive (256 MiB). */
export const MAX_PART_BYTES = 256 * 1024 * 1024;

/** Maximum downloaded workbooks cached in memory (LRU, scoped by credential hash + item + contentTag). */
export const FILE_CACHE_ENTRIES = 4;

/** Maximum Entra ID access tokens cached in memory (LRU, scoped by credential hash). */
export const MAX_TOKEN_CACHE_ENTRIES = 64;

/** Number of data rows sampled when inferring worksheet column types. */
export const SCHEMA_SAMPLE_ROWS = 500;

/** Hard cap on the data rows returned by one worksheet read. */
export const MAX_DATA_ROWS = 100_000;
