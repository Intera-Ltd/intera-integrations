export const INTEGRATION_ID = 'intera-eurobank-integration';
export const INTEGRATION_NAME = 'Eurobank';
export const INTEGRATION_VERSION = '3.2.0';

export const DIRECT_API_KIND = 'direct_api';
export const DATA_SOURCE_CAPABILITY = 'datasource';
export const WEBHOOK_CAPABILITY = 'webhook';
export const DEFAULT_CACHE_TTL_SEC = 30;

export const INTERA_SERVICE_URL_CONFIG_FIELD = 'intera_service_url';
export const INTERA_SERVICE_TOKEN_CONFIG_FIELD = 'intera_service_token';
export const OAUTH_CALLBACK_HANDLER_KEY = 'oauth-callback';

export const TOTAL_COUNT_HEADER = 'X-Total-Count';

export const DEFAULT_REQUEST_TIMEOUT_MS = 200_000;
export const STATUS_TIMEOUT_MS = 10_000;
export const REGISTRATION_TIMEOUT_MS = 10_000;
export const CALLBACK_RELAY_TIMEOUT_MS = 30_000;

export const EUROBANK_ACCOUNTS_SOURCE_ID = 'eurobank-accounts';
export const EUROBANK_ACCOUNTS_SOURCE_NAME = 'Eurobank Accounts';

export const DATA_SOURCE_IDS = [EUROBANK_ACCOUNTS_SOURCE_ID] as const;

export const DATA_SOURCE_NAMES = {
  [EUROBANK_ACCOUNTS_SOURCE_ID]: EUROBANK_ACCOUNTS_SOURCE_NAME,
} as const;
