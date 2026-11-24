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
import type { IDataQuery, IDataQueryContext, IDataQueryExecutor } from '../types';
import {
  REVOLUT_ACCOUNTS_FIELDS,
  REVOLUT_BALANCES_FIELDS,
  REVOLUT_EXCHANGE_RATE_FIELDS,
  REVOLUT_TRANSACTIONS_FIELDS,
  normalizeDataQuery,
} from './revolut-data-sources.source';

function createRevolutDataQueryExecutor(
  key: string,
  label: string,
  fields: IDataQueryExecutor['fields'],
  execute: (
    client: ReturnType<typeof createRevolutClient>,
    query: IDataQuery,
  ) => Promise<Record<string, unknown>>,
): IDataQueryExecutor {
  return {
    key,
    label,
    fields,
    async execute(context: IDataQueryContext, query: IDataQuery): Promise<Record<string, unknown>> {
      const client = createRevolutClient(readRevolutConfig(context));
      return execute(client, normalizeDataQuery(query));
    },
  };
}

export const revolutAccountsExecutor: IDataQueryExecutor = createRevolutDataQueryExecutor(
  REVOLUT_ACCOUNTS_SOURCE_ID,
  REVOLUT_ACCOUNTS_SOURCE_NAME,
  REVOLUT_ACCOUNTS_FIELDS,
  (client, query) => client.queryAccounts(query),
);

export const revolutBalancesExecutor: IDataQueryExecutor = createRevolutDataQueryExecutor(
  REVOLUT_BALANCES_SOURCE_ID,
  REVOLUT_BALANCES_SOURCE_NAME,
  REVOLUT_BALANCES_FIELDS,
  (client, query) => client.queryBalances(query),
);

export const revolutExchangeRateExecutor: IDataQueryExecutor = createRevolutDataQueryExecutor(
  REVOLUT_EXCHANGE_RATE_SOURCE_ID,
  REVOLUT_EXCHANGE_RATE_SOURCE_NAME,
  REVOLUT_EXCHANGE_RATE_FIELDS,
  (client, query) => client.queryExchangeRate(query),
);

export const revolutTransactionsExecutor: IDataQueryExecutor = createRevolutDataQueryExecutor(
  REVOLUT_TRANSACTIONS_SOURCE_ID,
  REVOLUT_TRANSACTIONS_SOURCE_NAME,
  REVOLUT_TRANSACTIONS_FIELDS,
  (client, query) => client.queryTransactions(query),
);

export const revolutDataQueryExecutors: IDataQueryExecutor[] = [
  revolutAccountsExecutor,
  revolutBalancesExecutor,
  revolutExchangeRateExecutor,
  revolutTransactionsExecutor,
];
