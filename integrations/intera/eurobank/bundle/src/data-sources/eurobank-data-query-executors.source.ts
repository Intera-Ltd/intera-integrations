import { createEurobankClient } from '../api/eurobank-client';
import { readEurobankConfig } from '../config';
import {
  EUROBANK_ACCOUNTS_SOURCE_ID,
  EUROBANK_ACCOUNTS_SOURCE_NAME,
} from '../constants';
import type { IDataQuery, IDataQueryContext, IDataQueryExecutor } from '../types';
import { EUROBANK_ACCOUNTS_FIELDS, normalizeDataQuery } from './eurobank-data-sources.source';

function createEurobankDataQueryExecutor(
  key: string,
  label: string,
  fields: IDataQueryExecutor['fields'],
  execute: (
    client: ReturnType<typeof createEurobankClient>,
    query: IDataQuery,
  ) => Promise<Record<string, unknown>>,
): IDataQueryExecutor {
  return {
    key,
    label,
    fields,
    async execute(context: IDataQueryContext, query: IDataQuery): Promise<Record<string, unknown>> {
      const client = createEurobankClient(readEurobankConfig(context));
      return execute(client, normalizeDataQuery(query));
    },
  };
}

export const eurobankAccountsExecutor: IDataQueryExecutor = createEurobankDataQueryExecutor(
  EUROBANK_ACCOUNTS_SOURCE_ID,
  EUROBANK_ACCOUNTS_SOURCE_NAME,
  EUROBANK_ACCOUNTS_FIELDS,
  (client, query) => client.queryAccounts(query),
);

export const eurobankDataQueryExecutors: IDataQueryExecutor[] = [eurobankAccountsExecutor];
