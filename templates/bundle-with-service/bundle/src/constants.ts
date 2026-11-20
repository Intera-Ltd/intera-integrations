/** Id of the integration inside the bundle; equals `runtime_id` in manifest.yaml. */
export const INTEGRATION_ID = 'your_integration';
export const INTEGRATION_NAME = 'Your Integration';
/** Must equal `version` in manifest.yaml: Core checks it after install. */
export const INTEGRATION_VERSION = '0.1.0';
/** Integration Contract this bundle is written against. */
export const INTEGRATION_CONTRACT_VERSION = 2;
export const DEFAULT_CACHE_TTL_SEC = 60;

/**
 * Config key under which Core passes the address of the integration's
 * container (for example `http://<container>:8080`). Core sets it itself for
 * integrations installed with `deployment.type: docker`.
 */
export const SERVICE_URL_CONFIG_KEY = 'intera_service_url';

export const RECORDS_PATH = '/api/records';
export const HEALTH_PATH = '/health';

/** Upper bound for one HTTP call to the service, in milliseconds. */
export const REQUEST_TIMEOUT_MS = 10_000;

export const RECORDS_DATA_SOURCE_ID = 'records';
export const RECORDS_DATA_SOURCE_NAME = 'Your records';
