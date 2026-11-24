export const INTEGRATION_ID = 'intera-revolut-integration';
export const INTEGRATION_NAME = 'Revolut Business';
export const INTEGRATION_VERSION = '3.5.0';

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

export const REVOLUT_ACCOUNTS_SOURCE_ID = 'revolut-accounts';
export const REVOLUT_ACCOUNTS_SOURCE_NAME = 'Revolut Accounts';

export const REVOLUT_BALANCES_SOURCE_ID = 'revolut-balances';
export const REVOLUT_BALANCES_SOURCE_NAME = 'Revolut Balances';

export const REVOLUT_EXCHANGE_RATE_SOURCE_ID = 'revolut-exchange-rate';
export const REVOLUT_EXCHANGE_RATE_SOURCE_NAME = 'Revolut Exchange Rate';

export const REVOLUT_TRANSACTIONS_SOURCE_ID = 'revolut-transactions';
export const REVOLUT_TRANSACTIONS_SOURCE_NAME = 'Revolut Transactions';

export const DATA_SOURCE_IDS = [
  REVOLUT_ACCOUNTS_SOURCE_ID,
  REVOLUT_BALANCES_SOURCE_ID,
  REVOLUT_EXCHANGE_RATE_SOURCE_ID,
  REVOLUT_TRANSACTIONS_SOURCE_ID,
] as const;

export const DATA_SOURCE_NAMES = {
  [REVOLUT_ACCOUNTS_SOURCE_ID]: REVOLUT_ACCOUNTS_SOURCE_NAME,
  [REVOLUT_BALANCES_SOURCE_ID]: REVOLUT_BALANCES_SOURCE_NAME,
  [REVOLUT_EXCHANGE_RATE_SOURCE_ID]: REVOLUT_EXCHANGE_RATE_SOURCE_NAME,
  [REVOLUT_TRANSACTIONS_SOURCE_ID]: REVOLUT_TRANSACTIONS_SOURCE_NAME,
} as const;
