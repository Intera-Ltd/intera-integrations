import {
  ITEMS_DATA_SOURCE_ID,
  ITEMS_DATA_SOURCE_NAME,
  ITEMS_PATH,
} from './constants';
import { getJson, resolveServiceUrl } from './service-client';
import type {
  IDataSource,
  IDataSourceContext,
  IDataSourceParams,
  IFieldDescriptor,
} from './types';

export interface IItemRow {
  id: string;
  name: string;
  amount: number;
  updatedAt: string;
}

const FIELDS: IFieldDescriptor[] = [
  { name: 'id', type: 'string', label: 'ID', identity: true },
  { name: 'name', type: 'string', label: 'Name' },
  { name: 'amount', type: 'number', label: 'Amount' },
  { name: 'updatedAt', type: 'date', label: 'Updated at' },
];

function toRow(value: unknown, index: number): IItemRow {
  if (typeof value !== 'object' || value === null) {
    throw new Error(`Item ${index} is not an object`);
  }
  const item = value as Record<string, unknown>;
  const { id, name, amount, updatedAt } = item;
  if (
    typeof id !== 'string' ||
    typeof name !== 'string' ||
    typeof amount !== 'number' ||
    typeof updatedAt !== 'string'
  ) {
    throw new Error(`Item ${index} does not match {id, name, amount, updatedAt}`);
  }
  return { id, name, amount, updatedAt };
}

function page(rows: IItemRow[], params: IDataSourceParams): IItemRow[] {
  const offset =
    typeof params.offset === 'number' && params.offset > 0 ? params.offset : 0;
  const limit =
    typeof params.limit === 'number' && params.limit >= 0
      ? params.limit
      : undefined;
  return rows.slice(offset, limit === undefined ? undefined : offset + limit);
}

export const itemsDataSource: IDataSource = {
  id: ITEMS_DATA_SOURCE_ID,
  name: ITEMS_DATA_SOURCE_NAME,
  description: 'Sample records served by the `hello-dotnet` container.',
  fields: FIELDS,
  async getData(
    params: IDataSourceParams,
    context: IDataSourceContext,
  ): Promise<IItemRow[]> {
    const baseUrl = resolveServiceUrl(context.config);
    const body = await getJson(baseUrl, ITEMS_PATH, context.signal);
    if (!Array.isArray(body)) {
      throw new Error(`GET ${ITEMS_PATH} did not return a list`);
    }
    return page(body.map(toRow), params);
  },
};
