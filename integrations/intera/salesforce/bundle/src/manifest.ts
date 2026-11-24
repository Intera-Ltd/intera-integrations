import {
  DATA_SOURCE_CAPABILITY,
  DEFAULT_CACHE_TTL_SEC,
  DIRECT_API_KIND,
  INTEGRATION_ID,
  INTEGRATION_NAME,
  INTEGRATION_VERSION,
} from './constants';
import type { IIntegrationManifest } from './types';

export const manifest: IIntegrationManifest = {
  id: INTEGRATION_ID,
  name: INTEGRATION_NAME,
  version: INTEGRATION_VERSION,
  requires: { core: '>=0.5.94' },
  compatibility: { integration_contract: 2 },
  kind: DIRECT_API_KIND,
  capabilities: [DATA_SOURCE_CAPABILITY],
  defaultCacheTtlSec: DEFAULT_CACHE_TTL_SEC,
  types: ['sales_crm'],
  description:
    'Connect Salesforce CRM to monitor accounts, pipeline opportunities, cases, tasks, events, and customer orders.',
  configSchema: [],
};
