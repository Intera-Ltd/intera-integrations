import {
  ACCESS_TOKEN_CONFIG_FIELD,
  API_BASE_URL_CONFIG_FIELD,
  DATA_SOURCE_CAPABILITY,
  DEFAULT_CACHE_TTL_SEC,
  DIRECT_API_KIND,
  INTEGRATION_ID,
  INTEGRATION_NAME,
  INTEGRATION_VERSION,
  OAUTH_CALLBACK_HANDLER_KEY,
  TENANT_ID_CONFIG_FIELD,
  WEBHOOK_CAPABILITY,
} from './constants';
import type { IIntegrationManifest } from './types';

export const manifest: IIntegrationManifest = {
  id: INTEGRATION_ID,
  name: INTEGRATION_NAME,
  version: INTEGRATION_VERSION,
  requires: { core: '>=0.5.25' },
  compatibility: { integration_contract: 2 },
  kind: DIRECT_API_KIND,
  capabilities: [DATA_SOURCE_CAPABILITY, WEBHOOK_CAPABILITY],
  defaultCacheTtlSec: DEFAULT_CACHE_TTL_SEC,
  types: ['accounting'],
  iconFile: 'assets/icon.svg',
  description:
    'Connect Xero Accounting to monitor invoices, bills, payments, bank transactions, manual journals, budgets, and financial reports.',
  configSchema: [
    {
      name: API_BASE_URL_CONFIG_FIELD,
      label: 'Service URL override (leave empty when deployed from Marketplace)',
      kind: 'string',
      required: false,
    },
    {
      name: ACCESS_TOKEN_CONFIG_FIELD,
      label: 'Service access token (required only when Xero:ApiAccessToken is set on the service)',
      kind: 'secret',
      required: false,
    },
    {
      name: TENANT_ID_CONFIG_FIELD,
      label: 'Xero tenant ID (only if the grant covers several organisations)',
      kind: 'string',
      required: false,
    },
  ],
  webhooks: [
    {
      key: OAUTH_CALLBACK_HANDLER_KEY,
      methods: ['GET'],
      registration: 'auto',
      invalidatesRegistrationOn: [API_BASE_URL_CONFIG_FIELD, ACCESS_TOKEN_CONFIG_FIELD],
    },
  ],
};
