import { createJiraClient } from './api/jira-client';
import { parsePluginConfig } from './config';
import type { ITestConnectionContext, ITestConnectionResult } from './types';

export async function testConnection(context: ITestConnectionContext): Promise<ITestConnectionResult> {
  const config = parsePluginConfig(context?.config ?? {});

  const data = await createJiraClient(config).query('/api/Source/projects/query', {
    result: {
      expressions: [
        { alias: 'jira_site_url', expression: { type: 'field', field: 'jira_site_url' } },
        { alias: 'projects', expression: { type: 'aggregate', action: 'count' } },
      ],
      groupBy: ['jira_site_url'],
    },
  });
  const rows = Array.isArray(data) ? (data as Array<Record<string, unknown>>) : [];
  const site = typeof rows[0]?.jira_site_url === 'string' ? rows[0].jira_site_url : undefined;
  const projects = typeof rows[0]?.projects === 'number' ? ` (${rows[0].projects} projects)` : '';

  return { detail: site ? `Jira: ${site}${projects}` : 'Jira: connected, no projects visible' };
}
