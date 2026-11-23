import {
  ACCESS_TOKEN_CONFIG_FIELD,
  DATA_SOURCE_CAPABILITY,
  DEFAULT_CACHE_TTL_SEC,
  DIRECT_API_KIND,
  INCLUDE_ARCHIVED_CONFIG_FIELD,
  INCLUDE_SUBTASKS_CONFIG_FIELD,
  INTEGRATION_ID,
  INTEGRATION_NAME,
  INTEGRATION_VERSION,
  PROJECTS_CONFIG_FIELD,
  WORKSPACE_CONFIG_FIELD,
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
  // Simple Icons' Asana mark (CC0), in its brand colour #F06A6A; the build copies assets/ into dist/.
  iconFile: 'assets/icon.svg',
  description:
    'Connect Asana to monitor projects, tasks, workloads, deadlines and status updates. INTERA turns Asana work ' +
    'into project health metrics, trends and management-level visibility.',
  configSchema: [
    {
      name: ACCESS_TOKEN_CONFIG_FIELD,
      label:
        'Access token — a personal access token (Asana: My settings → Apps → Developer console), or a service ' +
        'account token on Enterprise. INTERA sees what that user sees.',
      kind: 'secret',
      required: true,
    },
    {
      name: WORKSPACE_CONFIG_FIELD,
      label: 'Workspace — its name or gid (only needed when the token belongs to several)',
      kind: 'string',
    },
    {
      name: PROJECTS_CONFIG_FIELD,
      label: 'Projects to read — gids or exact names, comma-separated (empty: every project the token can see)',
      kind: 'string',
    },
    {
      name: INCLUDE_ARCHIVED_CONFIG_FIELD,
      label: 'Read the tasks of archived projects too (projects themselves are always listed)',
      kind: 'boolean',
      defaultValue: false,
    },
    {
      name: INCLUDE_SUBTASKS_CONFIG_FIELD,
      label: 'Include subtasks (one extra call per task that has subtasks)',
      kind: 'boolean',
      defaultValue: true,
    },
  ],
};
