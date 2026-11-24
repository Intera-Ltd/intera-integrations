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
  IDataSourceParams,
  IDataSourcePage,
} from '../types';
import {
  applyOrder,
  buildEvaluationSchema,
  evaluateExpressions,
  matchesFilter,
} from './query-evaluator';

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

function awaitWithSignal<T>(
  work: Promise<T>,
  signal: AbortSignal | undefined,
): Promise<T> {
  if (signal === undefined) {
    return work;
  }
  if (signal.aborted) {
    work.catch(() => undefined);
    return Promise.reject(signal.reason ?? new Error('Aborted'));
  }
  return new Promise<T>((resolve, reject) => {
    const onAbort = () => {
      work.catch(() => undefined);
      reject(signal.reason ?? new Error('Aborted'));
    };
    signal.addEventListener('abort', onAbort, { once: true });
    work.then(
      (value) => {
        signal.removeEventListener('abort', onAbort);
        resolve(value);
      },
      (error: unknown) => {
        signal.removeEventListener('abort', onAbort);
        reject(error);
      },
    );
  });
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

      // `filter` and `filters` are never both set (Core rejects that
      // upstream); a chain is an AND across its entries.
      let filtered = rows;
      if (params.filter) {
        filtered = filtered.filter((row) =>
          matchesFilter(row, params.filter, evaluationSchema),
        );
      }
      if (Array.isArray(params.filters)) {
        for (const filter of params.filters) {
          filtered = filtered.filter((row) =>
            matchesFilter(row, filter, evaluationSchema),
          );
        }
      }

      const shaped =
        Array.isArray(params.expressions) && params.expressions.length > 0
          ? evaluateExpressions(
              filtered,
              params.expressions,
              evaluationSchema,
              params.orderBy,
              Array.isArray(params.groupBy) ? params.groupBy : undefined,
            )
          : applyOrder(filtered, params.orderBy, evaluationSchema);

      // A page (rather than a bare array) tells Core that offset/limit were
      // applied here and `total` is the whole filtered set.
      if (typeof params.offset === 'number' && params.offset >= 0) {
        const limit =
          typeof params.limit === 'number' && params.limit > 0
            ? params.limit
            : undefined;
        const start = params.offset;
        const end = limit === undefined ? undefined : start + limit;
        return { rows: shaped.slice(start, end), total: shaped.length };
      }

      if (typeof params.limit === 'number' && params.limit > 0) {
        return shaped.slice(0, params.limit);
      }
      return shaped;
    },
  };
}

export const sanctionsDataSources: IDataSource[] =
  SANCTIONS_LISTS.map(createListDataSource);
