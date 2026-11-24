import { createSoft1Client } from '../api/soft1-client';
import { readPluginConfig } from '../config';
import { SOURCE_ID_PREFIX } from '../constants';
import type { IDataSource, IDataSourceContext, IDataSourceParams } from '../types';
import { normalizeDataQuery } from './query-normalization';
import { SOFT1_SOURCES, type ISoft1SourceDefinition } from './soft1-sources.generated';

export function apiPath(definition: ISoft1SourceDefinition): string {
  return `/api/Source/${encodeURIComponent(definition.apiSource)}/query`;
}

function createDataSource(definition: ISoft1SourceDefinition): IDataSource {
  return {
    id: `${SOURCE_ID_PREFIX}${definition.apiSource}`,
    name: definition.name,
    description: definition.description,
    fields: definition.fields,
    async getData(params: IDataSourceParams, context: IDataSourceContext): Promise<unknown> {
      const config = readPluginConfig(context);
      const query = normalizeDataQuery(params);
      const client = createSoft1Client(config);
      return client.query(apiPath(definition), query, context?.signal);
    },
  };
}

export const soft1DataSources: IDataSource[] = SOFT1_SOURCES.map(createDataSource);
