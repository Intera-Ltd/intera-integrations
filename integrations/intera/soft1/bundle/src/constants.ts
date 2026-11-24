export const INTEGRATION_ID = 'intera-soft1-integration';
export const INTEGRATION_NAME = 'EntersoftOne Soft1';
export const INTEGRATION_VERSION = '3.2.0';

export const DIRECT_API_KIND = 'direct_api';
export const DATA_SOURCE_CAPABILITY = 'datasource';
export const DEFAULT_CACHE_TTL_SEC = 300;

export const INTERA_SERVICE_URL_CONFIG_FIELD = 'intera_service_url';
export const INTERA_SERVICE_TOKEN_CONFIG_FIELD = 'intera_service_token';
export const DEFAULT_REQUEST_TIMEOUT_MS = 300_000;
export const MAX_RESPONSE_BYTES = 64 * 1024 * 1024;

export const TOTAL_COUNT_HEADER = 'X-Total-Count';

/** Prefix of every data source id; the rest is the API's source id (`soft1_customers` -> `customers`). */
export const SOURCE_ID_PREFIX = 'soft1_';
