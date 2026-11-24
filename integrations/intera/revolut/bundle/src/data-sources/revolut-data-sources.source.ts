import { createRevolutClient } from '../api/revolut-client';
import { readRevolutConfig } from '../config';
import {
  REVOLUT_ACCOUNTS_SOURCE_ID,
  REVOLUT_ACCOUNTS_SOURCE_NAME,
  REVOLUT_BALANCES_SOURCE_ID,
  REVOLUT_BALANCES_SOURCE_NAME,
  REVOLUT_EXCHANGE_RATE_SOURCE_ID,
  REVOLUT_EXCHANGE_RATE_SOURCE_NAME,
  REVOLUT_TRANSACTIONS_SOURCE_ID,
  REVOLUT_TRANSACTIONS_SOURCE_NAME,
} from '../constants';
import type {
  IDataQuery,
  IDataSource,
  IDataSourceContext,
  IDataSourceFieldDescriptor,
  IDataSourceParams,
} from '../types';

export const REVOLUT_ACCOUNTS_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'id', type: 'string', label: 'Account ID' },
  { name: 'name', type: 'string', label: 'Name' },
  { name: 'currency', type: 'string', label: 'Currency' },
  { name: 'balance', type: 'number', label: 'Balance' },
  { name: 'state', type: 'string', label: 'State' },
  { name: 'public', type: 'boolean', label: 'Public' },
];

export const REVOLUT_BALANCES_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'account_id', type: 'string', label: 'Account ID' },
  { name: 'account_name', type: 'string', label: 'Account Name' },
  { name: 'currency', type: 'string', label: 'Currency' },
  { name: 'balance', type: 'number', label: 'Balance' },
  { name: 'state', type: 'string', label: 'State' },
];

export const REVOLUT_EXCHANGE_RATE_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'from_currency', type: 'string', label: 'From Currency' },
  { name: 'to_currency', type: 'string', label: 'To Currency' },
  { name: 'amount', type: 'number', label: 'Amount' },
  { name: 'to_amount', type: 'number', label: 'Converted Amount' },
  { name: 'rate', type: 'number', label: 'Rate' },
];

export const REVOLUT_TRANSACTIONS_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'transaction_id', type: 'string', label: 'Transaction ID' },
  { name: 'leg_id', type: 'string', label: 'Leg ID' },
  {
    name: 'type',
    type: 'enum',
    label: 'Type',
    enumValues: [
      'atm',
      'card_payment',
      'card_refund',
      'card_chargeback',
      'card_credit',
      'charge',
      'charge_refund',
      'exchange',
      'transfer',
      'loan',
      'fee',
      'refund',
      'topup',
      'topup_return',
      'tax',
      'tax_refund',
      'rev_payment',
    ],
  },
  {
    name: 'state',
    type: 'enum',
    label: 'State',
    enumValues: ['created', 'pending', 'completed', 'declined', 'failed', 'reverted'],
  },
  { name: 'reason_code', type: 'string', label: 'Reason Code' },
  { name: 'request_id', type: 'string', label: 'Request ID' },
  { name: 'reference', type: 'string', label: 'Reference' },
  { name: 'created_at', type: 'date', label: 'Created At' },
  { name: 'updated_at', type: 'date', label: 'Updated At' },
  { name: 'completed_at', type: 'date', label: 'Completed At' },
  { name: 'scheduled_for', type: 'date', label: 'Scheduled For' },
  { name: 'related_transaction_id', type: 'string', label: 'Related Transaction ID' },
  { name: 'account_id', type: 'string', label: 'Account ID' },
  { name: 'account_name', type: 'string', label: 'Account Name' },
  { name: 'amount', type: 'number', label: 'Amount' },
  { name: 'fee', type: 'number', label: 'Fee' },
  { name: 'currency', type: 'string', label: 'Currency' },
  { name: 'bill_amount', type: 'number', label: 'Bill Amount' },
  { name: 'bill_currency', type: 'string', label: 'Bill Currency' },
  { name: 'balance', type: 'number', label: 'Balance After' },
  { name: 'description', type: 'string', label: 'Description' },
  { name: 'counterparty_id', type: 'string', label: 'Counterparty ID' },
  { name: 'counterparty_account_id', type: 'string', label: 'Counterparty Account ID' },
  {
    name: 'counterparty_account_type',
    type: 'enum',
    label: 'Counterparty Account Type',
    enumValues: ['self', 'revolut', 'external'],
  },
  { name: 'merchant_name', type: 'string', label: 'Merchant' },
  { name: 'merchant_city', type: 'string', label: 'Merchant City' },
  { name: 'merchant_category_code', type: 'string', label: 'Merchant Category Code' },
  { name: 'merchant_country', type: 'string', label: 'Merchant Country' },
  { name: 'card_number', type: 'string', label: 'Card Number' },
  { name: 'card_holder', type: 'string', label: 'Card Holder' },
];

type RevolutQueryExecutor = (
  client: ReturnType<typeof createRevolutClient>,
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

function createRevolutQueryDataSource(
  id: string,
  name: string,
  description: string,
  fields: IDataSourceFieldDescriptor[],
  execute: RevolutQueryExecutor,
): IDataSource {
  return {
    id,
    name,
    description,
    fields,
    async getData(params: IDataSourceParams, context: IDataSourceContext): Promise<unknown> {
      const query = normalizeDataQuery(params);
      const client = createRevolutClient(readRevolutConfig(context));
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

export const revolutAccountsDataSource: IDataSource = createRevolutQueryDataSource(
  REVOLUT_ACCOUNTS_SOURCE_ID,
  REVOLUT_ACCOUNTS_SOURCE_NAME,
  'Revolut Accounts',
  REVOLUT_ACCOUNTS_FIELDS,
  (client, query, signal) => client.queryAccountsRaw(query, signal),
);

export const revolutBalancesDataSource: IDataSource = createRevolutQueryDataSource(
  REVOLUT_BALANCES_SOURCE_ID,
  REVOLUT_BALANCES_SOURCE_NAME,
  'Revolut Balances',
  REVOLUT_BALANCES_FIELDS,
  (client, query, signal) => client.queryBalancesRaw(query, signal),
);

export const revolutExchangeRateDataSource: IDataSource = createRevolutQueryDataSource(
  REVOLUT_EXCHANGE_RATE_SOURCE_ID,
  REVOLUT_EXCHANGE_RATE_SOURCE_NAME,
  'Revolut Exchange Rate',
  REVOLUT_EXCHANGE_RATE_FIELDS,
  (client, query, signal) => client.queryExchangeRateRaw(query, signal),
);

export const revolutTransactionsDataSource: IDataSource = createRevolutQueryDataSource(
  REVOLUT_TRANSACTIONS_SOURCE_ID,
  REVOLUT_TRANSACTIONS_SOURCE_NAME,
  'One row per transaction **leg**: an exchange or a transfer between own accounts is two rows, one per ' +
    'account. Count transactions with `countDistinct(transaction_id)`.\n' +
    'Without a filter on `created_at` the last **90 days** are read.',
  REVOLUT_TRANSACTIONS_FIELDS,
  (client, query, signal) => client.queryTransactionsRaw(query, signal),
);

export const revolutDataSources: IDataSource[] = [
  revolutAccountsDataSource,
  revolutBalancesDataSource,
  revolutExchangeRateDataSource,
  revolutTransactionsDataSource,
];
