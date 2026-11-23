import {
  ACCESS_TOKEN_CONFIG_FIELD,
  API_BASE_URL_CONFIG_FIELD,
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
  requires: { core: '>=0.5.0' },
  compatibility: { integration_contract: 2 },
  kind: DIRECT_API_KIND,
  capabilities: [DATA_SOURCE_CAPABILITY],
  defaultCacheTtlSec: DEFAULT_CACHE_TTL_SEC,
  description:
    'Customers, orders, purchasing, receipts, packing, shipments, invoices, bills and stock from a ' +
    'Zoho Inventory organization — the commitment-to-fulfilment chain at line level, custom fields included.',
  types: ['sales_crm'],
  iconFile: 'assets/icon.svg',
  configSchema: [
    {
      name: API_BASE_URL_CONFIG_FIELD,
      label: 'Service URL override (leave empty when deployed from Marketplace)',
      kind: 'string',
      required: false,
    },
    {
      name: ACCESS_TOKEN_CONFIG_FIELD,
      label: 'Service access token (required only when ZohoInventory:ApiAccessToken is set on the service)',
      kind: 'secret',
      required: false,
    },
  ],
};
