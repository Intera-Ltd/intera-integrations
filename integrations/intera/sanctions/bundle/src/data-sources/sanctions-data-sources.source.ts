import {
  awaitWithSignal,
  buildEvaluationSchema,
  executeInMemoryQuery,
} from '@intera-integrations/shared';
import { resolveConfigUrl } from '../config';
import {
  EU_FSF_XML_URL,
  EU_LIST_URL_CONFIG_FIELD,
  EU_SANCTIONS_SOURCE_DESCRIPTION,
  EU_SANCTIONS_SOURCE_ID,
  EU_SANCTIONS_SOURCE_NAME,
} from '../constants';
import { EU_FIELDS, loadEuList } from '../lists/eu/eu-fsf';
import { getList, type ISanctionsList } from '../lists/list-cache';
import type {
  IDataSource,
  IDataSourceContext,
  IDataSourceFieldDescriptor,
  IDataSourcePage,
  IDataSourceParams,
} from '../types';

/**
 * What distinguishes one list from another. Everything after the download —
 * caching, filtering, grouping, paging — is the same for every list, so the
 * next list (OFAC, UK, UN, …) is one more definition in {@link SANCTIONS_LISTS}
 * plus its reader, and nothing here.
 */
export interface ISanctionsListDefinition {
  id: string;
  name: string;
  description: string;
  fields: IDataSourceFieldDescriptor[];
  /** The download address for an instance: its override, else the default. */
  resolveUrl(config: Record<string, unknown> | undefined): string;
  load(url: string): Promise<ISanctionsList>;
}

export const EU_SANCTIONS_LIST: ISanctionsListDefinition = {
  id: EU_SANCTIONS_SOURCE_ID,
  name: EU_SANCTIONS_SOURCE_NAME,
  description: EU_SANCTIONS_SOURCE_DESCRIPTION,
  fields: EU_FIELDS,
  resolveUrl: (config) => resolveConfigUrl(config, EU_LIST_URL_CONFIG_FIELD, EU_FSF_XML_URL),
  load: loadEuList,
};

export const SANCTIONS_LISTS: ISanctionsListDefinition[] = [EU_SANCTIONS_LIST];

export function loadList(
  list: ISanctionsListDefinition,
  config: Record<string, unknown> | undefined,
): Promise<ISanctionsList> {
  const url = list.resolveUrl(config);
  return getList(url, list.load);
}

function createListDataSource(list: ISanctionsListDefinition): IDataSource {
  const evaluationSchema = buildEvaluationSchema(list.fields);

  return {
    id: list.id,
    name: list.name,
    description: list.description,
    fields: list.fields,

    async getData(
      params: IDataSourceParams,
      context: IDataSourceContext,
    ): Promise<unknown[] | IDataSourcePage> {
      const { rows } = await awaitWithSignal(
        loadList(list, context?.config),
        context?.signal,
      );
      return executeInMemoryQuery(rows, params, evaluationSchema);
    },
  };
}

export const sanctionsDataSources: IDataSource[] =
  SANCTIONS_LISTS.map(createListDataSource);
