import { createJiraClient } from '../api/jira-client';
import { readPluginConfig } from '../config';
import { SOURCE_ID_PREFIX } from '../constants';
import type { IDataSource, IDataSourceContext, IDataSourceParams } from '../types';
import { normalizeDataQuery } from './query-normalization';
import { JIRA_SOURCES, type IJiraSourceDefinition } from './jira-sources.generated';

/**
 * One data source per entry of the generated list. Every source is served by the same API route,
 * `POST /api/Source/{apiSource}/query`, so a source here is its id, its fields and that path.
 */
export function apiPath(definition: IJiraSourceDefinition): string {
  return `/api/Source/${encodeURIComponent(definition.apiSource)}/query`;
}

function createDataSource(definition: IJiraSourceDefinition): IDataSource {
  return {
    id: `${SOURCE_ID_PREFIX}${definition.apiSource}`,
    name: definition.name,
    description: definition.description,
    fields: definition.fields,
    async getData(params: IDataSourceParams, context: IDataSourceContext): Promise<unknown> {
      const query = normalizeDataQuery(params);
      const client = createJiraClient(readPluginConfig(context));
      return client.query(apiPath(definition), query, context?.signal);
    },
  };
}

export const jiraDataSources: IDataSource[] = JIRA_SOURCES.map(createDataSource);
