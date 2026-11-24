import {
  DATA_SOURCE_CAPABILITY,
  DEFAULT_CACHE_TTL_SEC,
  DIRECT_API_KIND,
  INTEGRATION_ID,
  INTEGRATION_NAME,
  INTEGRATION_VERSION,
  OAUTH_CALLBACK_HANDLER_KEY,
  WEBHOOK_CAPABILITY,
} from './constants';
import type { IIntegrationManifest } from './types';

export const manifest: IIntegrationManifest = {
  id: INTEGRATION_ID,
  name: INTEGRATION_NAME,
  version: INTEGRATION_VERSION,
  requires: { core: '>=0.5.94' },
  compatibility: { integration_contract: 2 },
  kind: DIRECT_API_KIND,
  capabilities: [DATA_SOURCE_CAPABILITY, WEBHOOK_CAPABILITY],
  defaultCacheTtlSec: DEFAULT_CACHE_TTL_SEC,
  types: ['banking'],
  // Simple Icons "revolut" (CC0), filled with its brand colour #191C1F.
  iconFile: 'assets/icon.svg',
  description:
    'Connect Revolut Business to monitor accounts, balances, exchange rates, transactions and payment activity ' +
    'through INTERA dashboards, metrics and financial controls.',
  configSchema: [],
  webhooks: [
    {
      key: OAUTH_CALLBACK_HANDLER_KEY,
      methods: ['GET'],
      registration: 'auto',
    },
  ],
};
