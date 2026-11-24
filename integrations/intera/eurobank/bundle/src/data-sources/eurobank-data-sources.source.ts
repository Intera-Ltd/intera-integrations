import { normalizeDataQuery } from '@intera-integrations/shared';
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

export { normalizeDataQuery };

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

export const eurobankAccountsDataSource: IDataSource = createEurobankQueryDataSource(
  EUROBANK_ACCOUNTS_SOURCE_ID,
  EUROBANK_ACCOUNTS_SOURCE_NAME,
  'Eurobank Accounts',
  EUROBANK_ACCOUNTS_FIELDS,
  (client, query, signal) => client.queryAccountsRaw(query, signal),
);

export const eurobankDataSources: IDataSource[] = [eurobankAccountsDataSource];
