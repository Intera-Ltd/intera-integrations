export const INTEGRATION_ID = 'intera-salesforce-integration';
export const INTEGRATION_NAME = 'Salesforce';
export const INTEGRATION_VERSION = '3.3.0';

export const DIRECT_API_KIND = 'direct_api';
export const DATA_SOURCE_CAPABILITY = 'datasource';

export const DEFAULT_CACHE_TTL_SEC = 300;

export const INTERA_SERVICE_URL_CONFIG_KEY = 'intera_service_url';
export const INTERA_SERVICE_TOKEN_CONFIG_KEY = 'intera_service_token';
export const DEFAULT_REQUEST_TIMEOUT_MS = 200_000;

export const TOTAL_COUNT_HEADER = 'X-Total-Count';

export const ACCOUNTS_SOURCE_ID = 'salesforce_accounts';
export const ACCOUNTS_SOURCE_NAME = 'Accounts';

export const OPPORTUNITIES_SOURCE_ID = 'salesforce_opportunities';
export const OPPORTUNITIES_SOURCE_NAME = 'Opportunities';

export const CASES_SOURCE_ID = 'salesforce_cases';
export const CASES_SOURCE_NAME = 'Cases';

export const TASKS_SOURCE_ID = 'salesforce_tasks';
export const TASKS_SOURCE_NAME = 'Tasks';

export const EVENTS_SOURCE_ID = 'salesforce_events';
export const EVENTS_SOURCE_NAME = 'Events';

export const ORDERS_SOURCE_ID = 'salesforce_orders';
export const ORDERS_SOURCE_NAME = 'Orders';

export const DATA_SOURCE_IDS = [
  ACCOUNTS_SOURCE_ID,
  OPPORTUNITIES_SOURCE_ID,
  CASES_SOURCE_ID,
  TASKS_SOURCE_ID,
  EVENTS_SOURCE_ID,
  ORDERS_SOURCE_ID,
] as const;

export const DATA_SOURCE_NAMES = {
  [ACCOUNTS_SOURCE_ID]: ACCOUNTS_SOURCE_NAME,
  [OPPORTUNITIES_SOURCE_ID]: OPPORTUNITIES_SOURCE_NAME,
  [CASES_SOURCE_ID]: CASES_SOURCE_NAME,
  [TASKS_SOURCE_ID]: TASKS_SOURCE_NAME,
  [EVENTS_SOURCE_ID]: EVENTS_SOURCE_NAME,
  [ORDERS_SOURCE_ID]: ORDERS_SOURCE_NAME,
} as const;
