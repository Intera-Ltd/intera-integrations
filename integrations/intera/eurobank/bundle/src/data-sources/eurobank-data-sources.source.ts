import { createEurobankClient } from '../api/eurobank-client';
import { readEurobankConfig } from '../config';
import {
  EUROBANK_ACCOUNTS_SOURCE_ID,
  EUROBANK_ACCOUNTS_SOURCE_NAME,
} from '../constants';
import type {
  IDataQuery,
  IDataSource,
  IDataSourceContext,
  IDataSourceFieldDescriptor,
  IDataSourceParams,
} from '../types';

export const EUROBANK_ACCOUNTS_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'account_name', type: 'string', label: 'Account Name' },
  { name: 'account_type', type: 'string', label: 'Account Type' },
  { name: 'account_type_description', type: 'string', label: 'Account Type Description' },
  { name: 'iban', type: 'string', label: 'IBAN' },
  { name: 'account_number', type: 'string', label: 'Account Number' },
  { name: 'currency', type: 'string', label: 'Currency' },
  { name: 'available_balance', type: 'number', label: 'Available Balance' },
  { name: 'current_balance', type: 'number', label: 'Current Balance' },
  { name: 'status', type: 'string', label: 'Status' },
];

type EurobankQueryExecutor = (
  client: ReturnType<typeof createEurobankClient>,
  query: IDataQuery,
  signal?: AbortSignal,
) => Promise<unknown>;

export function normalizeDataQuery(rawQuery: unknown): IDataQuery {
  if (!isRecord(rawQuery)) {
    throw new Error('DataSource query must be an object');
  }

  const result = parseJsonParam(rawQuery.result, 'result');
  const expressions = parseJsonParam(rawQuery.expressions, 'expressions');
  const filterItems = [
    ...parseJsonArrayParam(rawQuery.filter, 'filter'),
    ...parseJsonArrayParam(rawQuery.filters, 'filters'),
  ];

  const groupBy = parseJsonParam(rawQuery.groupBy, 'groupBy');
  const groupByKeys =
    Array.isArray(groupBy) && groupBy.every((key) => typeof key === 'string')
      ? (groupBy as string[])
      : undefined;

  const baseResult = isRecord(result)
    ? (result as IDataQuery['result'])
    : Array.isArray(expressions)
      ? ({ expressions } as IDataQuery['result'])
      : undefined;

  const normalizedResult =
    baseResult && groupByKeys?.length
      ? ({ ...baseResult, groupBy: groupByKeys } as IDataQuery['result'])
      : baseResult;

  return {
    ...(filterItems.length > 0 ? { filters: filterItems as NonNullable<IDataQuery['filters']> } : {}),
    ...(normalizedResult ? { result: normalizedResult } : {}),
    ...normalizePaging(rawQuery),
  };
}

function createEurobankQueryDataSource(
  id: string,
  name: string,
  description: string,
  fields: IDataSourceFieldDescriptor[],
  execute: EurobankQueryExecutor,
): IDataSource {
  return {
    id,
    name,
    description,
    fields,
    async getData(params: IDataSourceParams, context: IDataSourceContext): Promise<unknown> {
      const query = normalizeDataQuery(params);
      const client = createEurobankClient(readEurobankConfig(context));
      return execute(client, query, context?.signal);
    },
  };
}

function normalizePaging(rawQuery: Record<string, unknown>): Partial<IDataQuery> {
  const paging: Partial<IDataQuery> = {};

  const top = parseNonNegativeInteger(rawQuery.limit ?? rawQuery.top);
  if (top !== undefined && top > 0) {
    paging.top = top;
  }

  const skip = parseNonNegativeInteger(rawQuery.offset ?? rawQuery.skip);
  if (skip !== undefined) {
    paging.skip = skip;
  }

  const order = normalizeOrder(rawQuery.orderBy ?? rawQuery.order);
  if (order.length > 0) {
    paging.order = order;
  }

  return paging;
}

function normalizeOrder(value: unknown): NonNullable<IDataQuery['order']> {
  const parsed = typeof value === 'string' ? parseJsonParam(value, 'orderBy') : value;
  if (!Array.isArray(parsed)) {
    return [];
  }

  const order: NonNullable<IDataQuery['order']> = [];
  for (const entry of parsed) {
    if (!isRecord(entry)) {
      continue;
    }
    const field = entry.field;
    if (typeof field !== 'string' || field.length === 0) {
      continue;
    }
    const direction = entry.direction;
    order.push(direction === 'asc' || direction === 'desc' ? { field, direction } : { field });
  }

  return order;
}

function parseJsonArrayParam(value: unknown, fieldName: string): unknown[] {
  const parsed = parseJsonParam(value, fieldName);

  if (parsed === undefined) {
    return [];
  }

  return Array.isArray(parsed) ? parsed : [parsed];
}

function parseJsonParam(value: unknown, fieldName: string): unknown {
  if (typeof value !== 'string') {
    return value;
  }

  try {
    return JSON.parse(value) as unknown;
  } catch {
    throw new Error(`DataSource param "${fieldName}" must be valid JSON`);
  }
}

function parseNonNegativeInteger(value: unknown): number | undefined {
  const parsed = typeof value === 'number' ? value : Number.parseInt(String(value ?? ''), 10);
  return Number.isInteger(parsed) && parsed >= 0 ? parsed : undefined;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export const eurobankAccountsDataSource: IDataSource = createEurobankQueryDataSource(
  EUROBANK_ACCOUNTS_SOURCE_ID,
  EUROBANK_ACCOUNTS_SOURCE_NAME,
  'Eurobank Accounts',
  EUROBANK_ACCOUNTS_FIELDS,
  (client, query, signal) => client.queryAccountsRaw(query, signal),
);

export const eurobankDataSources: IDataSource[] = [eurobankAccountsDataSource];
