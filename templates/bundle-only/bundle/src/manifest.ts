import {
  API_KEY_CONFIG_KEY,
  BASE_URL_CONFIG_KEY,
  DEFAULT_CACHE_TTL_SEC,
  INTEGRATION_CONTRACT_VERSION,
  INTEGRATION_ID,
  INTEGRATION_NAME,
  INTEGRATION_VERSION,
} from './constants';
import type { IIntegrationManifest } from './types';

/** Runtime manifest that Core reads from `dist/index.js`. */
export const manifest: IIntegrationManifest = {
  id: INTEGRATION_ID,
  name: INTEGRATION_NAME,
  version: INTEGRATION_VERSION,
  kind: 'readonly_api',
  capabilities: ['datasource'],
  defaultCacheTtlSec: DEFAULT_CACHE_TTL_SEC,
  configSchema: [
    { name: BASE_URL_CONFIG_KEY, label: 'API base URL', kind: 'string', required: true },
    // Secrets are entered by the administrator and stored encrypted by Core.
    { name: API_KEY_CONFIG_KEY, label: 'API key', kind: 'secret', required: true },
  ],
  compatibility: { integration_contract: INTEGRATION_CONTRACT_VERSION },
};
