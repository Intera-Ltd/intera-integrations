import {
  DEFAULT_CACHE_TTL_SEC,
  INTEGRATION_CONTRACT_VERSION,
  INTEGRATION_ID,
  INTEGRATION_NAME,
  INTEGRATION_VERSION,
} from './constants';
import type { IIntegrationManifest } from './types';

/**
 * Runtime manifest that Core reads from `dist/index.js`. It needs no config
 * fields: the only setting, the service address, is provided by Core.
 */
export const manifest: IIntegrationManifest = {
  id: INTEGRATION_ID,
  name: INTEGRATION_NAME,
  version: INTEGRATION_VERSION,
  kind: 'readonly_api',
  capabilities: ['datasource'],
  defaultCacheTtlSec: DEFAULT_CACHE_TTL_SEC,
  configSchema: [],
  compatibility: { integration_contract: INTEGRATION_CONTRACT_VERSION },
};
