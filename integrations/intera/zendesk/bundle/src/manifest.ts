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
  WEBHOOK_CAPABILITY,
} from './constants';
import type { IIntegrationManifest } from './types';

export const manifest: IIntegrationManifest = {
  id: INTEGRATION_ID,
  name: INTEGRATION_NAME,
  version: INTEGRATION_VERSION,
  requires: { core: '>=0.5.0' },
  compatibility: { integration_contract: 2 },
  kind: DIRECT_API_KIND,
  capabilities: [DATA_SOURCE_CAPABILITY, WEBHOOK_CAPABILITY],
  defaultCacheTtlSec: DEFAULT_CACHE_TTL_SEC,
  types: ['ticketing'],
  description:
    'Connect Zendesk Support to monitor tickets, backlog, first-response and resolution times, SLA breaches, ' +
    'reopens, workload by agent, group and customer, and CSAT. INTERA reads the metrics Zendesk already calculates ' +
    'and turns them into support health trends and management-level visibility.',
  configSchema: [
    {
      name: API_BASE_URL_CONFIG_FIELD,
      label: 'Service URL override (leave empty when deployed from Marketplace)',
      kind: 'string',
      required: false,
    },
    {
      name: ACCESS_TOKEN_CONFIG_FIELD,
      label: 'Service access token (required only when Zendesk:ApiAccessToken is set on the service)',
      kind: 'secret',
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
