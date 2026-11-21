import {
  ACCESS_TOKEN_CONFIG_FIELD,
  API_URL_CONFIG_FIELD,
  APP_ID_CONFIG_FIELD,
  APP_PRIVATE_KEY_CONFIG_FIELD,
  DATA_SOURCE_CAPABILITY,
  DEFAULT_CACHE_TTL_SEC,
  DEFAULT_HISTORY_DAYS,
  DIRECT_API_KIND,
  HISTORY_DAYS_CONFIG_FIELD,
  INCLUDE_ARCHIVED_CONFIG_FIELD,
  INSTALLATION_ID_CONFIG_FIELD,
  INTEGRATION_ID,
  INTEGRATION_NAME,
  INTEGRATION_VERSION,
  OWNER_CONFIG_FIELD,
  REPOSITORIES_CONFIG_FIELD,
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
  types: ['project_management'],
  // Simple Icons' GitHub mark, in its brand colour #181717; the build copies assets/ into dist/.
  iconFile: 'assets/icon.svg',
  description:
    'Connect GitHub to monitor repositories, CI/CD runs, deployments, pull requests, issues and security alerts. ' +
    'INTERA turns repository activity into delivery, operations and security metrics.',
  configSchema: [
    {
      name: OWNER_CONFIG_FIELD,
      label: 'Owner — the organization (or user) whose repositories to read',
      kind: 'string',
      required: true,
    },
    {
      name: ACCESS_TOKEN_CONFIG_FIELD,
      label:
        'Personal access token — fine-grained, with the owner as resource owner and read-only access to Metadata, ' +
        'Contents, Actions, Deployments, Pull requests, Issues and the three alert kinds. Leave empty for a ' +
        'GitHub App.',
      kind: 'secret',
    },
    {
      name: APP_ID_CONFIG_FIELD,
      label: 'GitHub App — its App ID (or Client ID), instead of a token',
      kind: 'string',
    },
    {
      name: APP_PRIVATE_KEY_CONFIG_FIELD,
      label: "GitHub App — its private key, the whole .pem file's text",
      kind: 'secret',
    },
    {
      name: INSTALLATION_ID_CONFIG_FIELD,
      label: "GitHub App — installation ID (empty: the app's installation on the owner)",
      kind: 'string',
    },
    {
      name: REPOSITORIES_CONFIG_FIELD,
      label: 'Repositories to read — names, comma-separated (empty: every repository the credential can see)',
      kind: 'string',
    },
    {
      name: INCLUDE_ARCHIVED_CONFIG_FIELD,
      label: 'Read the contents of archived repositories too (the repositories themselves are always listed)',
      kind: 'boolean',
      defaultValue: false,
    },
    {
      name: HISTORY_DAYS_CONFIG_FIELD,
      label:
        'Days of history for runs, commits, deployments and closed issues and pull requests, when a filter sets ' +
        'no start date (open ones are always read)',
      kind: 'number',
      defaultValue: DEFAULT_HISTORY_DAYS,
    },
    {
      name: API_URL_CONFIG_FIELD,
      label: 'API URL — only for GitHub Enterprise Server: https://<host>/api/v3',
      kind: 'string',
    },
  ],
};
