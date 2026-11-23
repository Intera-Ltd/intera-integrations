import {
  DATA_SOURCE_CAPABILITY,
  DEFAULT_CACHE_TTL_SEC,
  DIRECT_API_KIND,
  EU_LIST_URL_CONFIG_FIELD,
  INTEGRATION_ID,
  INTEGRATION_NAME,
  INTEGRATION_VERSION,
} from './constants';
import type { IIntegrationManifest } from './types';

export const manifest: IIntegrationManifest = {
  id: INTEGRATION_ID,
  name: INTEGRATION_NAME,
  version: INTEGRATION_VERSION,
  requires: { core: '>=0.5.0' },
  compatibility: { integration_contract: 2 },
  kind: DIRECT_API_KIND,
  capabilities: [DATA_SOURCE_CAPABILITY],
  defaultCacheTtlSec: DEFAULT_CACHE_TTL_SEC,
  author: 'Intera',
  icon: '🛡️',
  description:
    'Official sanctions lists as datasources, downloaded straight from the ' +
    'publisher once a day and searched locally — no per-name request leaves ' +
    'the host. Currently the EU Consolidated List of Financial Sanctions.',
  types: ['core'],
  configSchema: [
    {
      name: EU_LIST_URL_CONFIG_FIELD,
      label:
        'EU list download address (leave empty for the official FSF XML export)',
      kind: 'string',
    },
  ],
};
