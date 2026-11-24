export const INTEGRATION_ID = 'intera-teamcity-integration';
export const INTEGRATION_NAME = 'JetBrains TeamCity';
export const INTEGRATION_VERSION = '3.2.0';

export const DIRECT_API_KIND = 'direct_api';
export const DATA_SOURCE_CAPABILITY = 'datasource';
export const DEFAULT_CACHE_TTL_SEC = 60;

export const INTERA_SERVICE_URL_CONFIG_FIELD = 'intera_service_url';
export const INTERA_SERVICE_TOKEN_CONFIG_FIELD = 'intera_service_token';
export const DEFAULT_REQUEST_TIMEOUT_MS = 200_000;
export const MAX_RESPONSE_BYTES = 64 * 1024 * 1024;

export const TOTAL_COUNT_HEADER = 'X-Total-Count';

export const BUILD_TYPES_SOURCE_ID = 'teamcity_build_types';
export const BUILD_TYPES_SOURCE_NAME = 'TeamCity build configurations';

export const TEST_RESULTS_SOURCE_ID = 'teamcity_test_results';
export const TEST_RESULTS_SOURCE_NAME = 'TeamCity test results (latest build)';

export const DATA_SOURCE_IDS = [BUILD_TYPES_SOURCE_ID, TEST_RESULTS_SOURCE_ID] as const;
