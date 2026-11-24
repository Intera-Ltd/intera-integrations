import { createJiraClient } from '../api/jira-client';
import { readPluginConfig } from '../config';
import { SOURCE_ID_PREFIX } from '../constants';
import type { IDataQuery, IDataQueryContext, IDataQueryExecutor } from '../types';
import { normalizeDataQuery } from '@intera-integrations/shared';
import { apiPath } from './jira-data-sources.source';
import { JIRA_SOURCES } from './jira-sources.generated';

function firstRow(data: unknown, name: string): Record<string, unknown> {
  const rows = Array.isArray(data)
    ? data
    : typeof data === 'object' && data !== null && Array.isArray((data as { rows?: unknown }).rows)
      ? (data as { rows: unknown[] }).rows
      : [data];
  const row = rows[0];

  if (typeof row === 'object' && row !== null && !Array.isArray(row)) {
    return row as Record<string, unknown>;
  }

  throw new Error(`Jira ${name} response must contain an object in data`);
}

// Compatibility export for the current Core KPI runner, as in the other API-backed plugins.
export const jiraDataQueryExecutors: IDataQueryExecutor[] = JIRA_SOURCES.map((definition) => {
  const key = `${SOURCE_ID_PREFIX}${definition.apiSource}`;

  return {
    key,
    label: definition.name,
    fields: definition.fields,
    async execute(context: IDataQueryContext, query: IDataQuery): Promise<Record<string, unknown>> {
      const client = createJiraClient(readPluginConfig(context));
      return firstRow(await client.query(apiPath(definition), normalizeDataQuery(query)), key);
    },
  };
});
