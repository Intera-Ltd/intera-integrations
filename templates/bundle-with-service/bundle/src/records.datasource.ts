import {
  RECORDS_DATA_SOURCE_ID,
  RECORDS_DATA_SOURCE_NAME,
  RECORDS_PATH,
} from './constants';
import { getJson, resolveServiceUrl } from './service-client';
import type {
  IDataSource,
  IDataSourceContext,
  IDataSourceParams,
  IFieldDescriptor,
} from './types';

export interface IRecordRow {
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

function toRow(value: unknown, index: number): IRecordRow {
  if (typeof value !== 'object' || value === null) {
    throw new Error(`Record ${index} is not an object`);
  }
  const item = value as Record<string, unknown>;
  const { id, name, amount, updatedAt } = item;
  if (
    typeof id !== 'string' ||
    typeof name !== 'string' ||
    typeof amount !== 'number' ||
    typeof updatedAt !== 'string'
  ) {
    throw new Error(`Record ${index} does not match the expected record shape`);
  }
  return { id, name, amount, updatedAt };
}

function page(rows: IRecordRow[], params: IDataSourceParams): IRecordRow[] {
  const offset =
    typeof params.offset === 'number' && params.offset > 0 ? params.offset : 0;
  const limit =
    typeof params.limit === 'number' && params.limit >= 0
      ? params.limit
      : undefined;
  return rows.slice(offset, limit === undefined ? undefined : offset + limit);
}

export const recordsDataSource: IDataSource = {
  id: RECORDS_DATA_SOURCE_ID,
  name: RECORDS_DATA_SOURCE_NAME,
  description: 'Sample records served by the integration container.',
  fields: FIELDS,
  async getData(
    params: IDataSourceParams,
    context: IDataSourceContext,
  ): Promise<IRecordRow[]> {
    const baseUrl = resolveServiceUrl(context.config);
    const body = await getJson(baseUrl, RECORDS_PATH, context.signal);
    if (!Array.isArray(body)) {
      throw new Error(`GET ${RECORDS_PATH} did not return a list`);
    }
    return page(body.map(toRow), params);
  },
};
