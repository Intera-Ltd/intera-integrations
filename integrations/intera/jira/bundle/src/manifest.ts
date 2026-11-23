import {
  ACCESS_TOKEN_CONFIG_FIELD,
  API_BASE_URL_CONFIG_FIELD,
  DATA_SOURCE_CAPABILITY,
  DEFAULT_CACHE_TTL_SEC,
  DIRECT_API_KIND,
  INTEGRATION_ID,
  INTEGRATION_NAME,
  INTEGRATION_VERSION,
  JQL_SCOPE_CONFIG_FIELD,
  OAUTH_CALLBACK_HANDLER_KEY,
  SITE_URL_CONFIG_FIELD,
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
  types: ['project_management'],
  iconFile: 'assets/icon.svg',
  description:
    'Connect Jira to monitor projects, issues, sprints, workloads and delivery progress. INTERA turns Jira activity ' +
    'into project health metrics, trends and management-level visibility.',
  configSchema: [
    {
      name: API_BASE_URL_CONFIG_FIELD,
      label: 'Service URL override (leave empty when deployed from Marketplace)',
      kind: 'string',
      required: false,
    },
    {
      name: ACCESS_TOKEN_CONFIG_FIELD,
      label: 'Service access token (required only when Jira:ApiAccessToken is set on the service)',
      kind: 'secret',
      required: false,
    },
    {
      name: SITE_URL_CONFIG_FIELD,
      label: 'Jira site URL (only if the grant covers several sites)',
      kind: 'string',
      required: false,
    },
    {
      name: JQL_SCOPE_CONFIG_FIELD,
      label: 'JQL scope (optional, e.g. project in (DEV, OPS))',
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
