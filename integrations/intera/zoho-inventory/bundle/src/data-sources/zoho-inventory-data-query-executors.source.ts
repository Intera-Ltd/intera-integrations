import { createZohoInventoryClient } from '../api/zoho-inventory-client';
import { readZohoInventoryConfig } from '../config';
import type { IDataQuery, IDataQueryContext, IDataQueryExecutor } from '../types';
import {
  ZOHO_INVENTORY_SOURCES,
  normalizeDataQuery,
  type IZohoInventorySourceDefinition,
} from './zoho-inventory-data-sources.source';

function createZohoInventoryDataQueryExecutor(source: IZohoInventorySourceDefinition): IDataQueryExecutor {
  return {
    key: source.id,
    label: source.name,
    fields: source.fields,
    async execute(context: IDataQueryContext, query: IDataQuery): Promise<Record<string, unknown>> {
      const client = createZohoInventoryClient(readZohoInventoryConfig(context));
      return client.queryObject(source.apiPath, normalizeDataQuery(query), source.name);
    },
  };
}

export const zohoInventoryDataQueryExecutors: IDataQueryExecutor[] = ZOHO_INVENTORY_SOURCES.map(
  createZohoInventoryDataQueryExecutor,
);
