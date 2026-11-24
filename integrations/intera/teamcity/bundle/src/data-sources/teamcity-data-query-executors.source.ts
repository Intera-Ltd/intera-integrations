import { createTeamCityClient, type TeamCityClient } from '../api/teamcity-client';
import { readPluginConfig } from '../config';
import {
  BUILD_TYPES_SOURCE_ID,
  BUILD_TYPES_SOURCE_NAME,
  TEST_RESULTS_SOURCE_ID,
  TEST_RESULTS_SOURCE_NAME,
} from '../constants';
import type { IDataQuery, IDataQueryContext, IDataQueryExecutor } from '../types';
import { normalizeDataQuery } from '@intera-integrations/shared';
import { BUILD_TYPE_FIELDS, TEST_RESULT_FIELDS } from './teamcity-data-sources.source';

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

  throw new Error(`TeamCity ${name} response must contain an object in data`);
}

function createExecutor(
  key: string,
  label: string,
  fields: IDataQueryExecutor['fields'],
  execute: (client: TeamCityClient, query: IDataQuery, signal?: AbortSignal) => Promise<unknown>,
): IDataQueryExecutor {
  return {
    key,
    label,
    fields,
    async execute(context: IDataQueryContext, query: IDataQuery): Promise<Record<string, unknown>> {
      const client = createTeamCityClient(readPluginConfig(context));
      return firstRow(await execute(client, normalizeDataQuery(query), context?.signal), key);
    },
  };
}

export const teamCityDataQueryExecutors: IDataQueryExecutor[] = [
  createExecutor(BUILD_TYPES_SOURCE_ID, BUILD_TYPES_SOURCE_NAME, BUILD_TYPE_FIELDS, (client, query, signal) =>
    client.queryBuildTypes(query, signal),
  ),
  createExecutor(TEST_RESULTS_SOURCE_ID, TEST_RESULTS_SOURCE_NAME, TEST_RESULT_FIELDS, (client, query, signal) =>
    client.queryTestResults(query, signal),
  ),
];
