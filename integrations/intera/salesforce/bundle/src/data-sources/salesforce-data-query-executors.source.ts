import {
  ACCOUNTS_QUERY_API_PATH,
  CASES_QUERY_API_PATH,
  EVENTS_QUERY_API_PATH,
  OPPORTUNITIES_QUERY_API_PATH,
  ORDERS_QUERY_API_PATH,
  TASKS_QUERY_API_PATH,
  createSalesforceClient,
} from '../api/salesforce-client';
import { readSalesforceConfig } from '../config';
import {
  ACCOUNTS_SOURCE_ID,
  ACCOUNTS_SOURCE_NAME,
  CASES_SOURCE_ID,
  CASES_SOURCE_NAME,
  EVENTS_SOURCE_ID,
  EVENTS_SOURCE_NAME,
  OPPORTUNITIES_SOURCE_ID,
  OPPORTUNITIES_SOURCE_NAME,
  ORDERS_SOURCE_ID,
  ORDERS_SOURCE_NAME,
  TASKS_SOURCE_ID,
  TASKS_SOURCE_NAME,
} from '../constants';
import type { IDataQuery, IDataQueryContext, IDataQueryExecutor } from '../types';
import {
  ACCOUNT_FIELDS,
  CASE_FIELDS,
  EVENT_FIELDS,
  OPPORTUNITY_FIELDS,
  ORDER_FIELDS,
  TASK_FIELDS,
  normalizeDataQuery,
} from './salesforce-data-sources.source';

function createSalesforceDataQueryExecutor(
  key: string,
  label: string,
  fields: IDataQueryExecutor['fields'],
  apiPath: string,
): IDataQueryExecutor {
  return {
    key,
    label,
    fields,
    async execute(context: IDataQueryContext, query: IDataQuery): Promise<Record<string, unknown>> {
      const client = createSalesforceClient(readSalesforceConfig(context));
      return client.queryObject(apiPath, normalizeDataQuery(query), label);
    },
  };
}

export const accountsExecutor: IDataQueryExecutor = createSalesforceDataQueryExecutor(
  ACCOUNTS_SOURCE_ID,
  ACCOUNTS_SOURCE_NAME,
  ACCOUNT_FIELDS,
  ACCOUNTS_QUERY_API_PATH,
);

export const opportunitiesExecutor: IDataQueryExecutor = createSalesforceDataQueryExecutor(
  OPPORTUNITIES_SOURCE_ID,
  OPPORTUNITIES_SOURCE_NAME,
  OPPORTUNITY_FIELDS,
  OPPORTUNITIES_QUERY_API_PATH,
);

export const casesExecutor: IDataQueryExecutor = createSalesforceDataQueryExecutor(
  CASES_SOURCE_ID,
  CASES_SOURCE_NAME,
  CASE_FIELDS,
  CASES_QUERY_API_PATH,
);

export const tasksExecutor: IDataQueryExecutor = createSalesforceDataQueryExecutor(
  TASKS_SOURCE_ID,
  TASKS_SOURCE_NAME,
  TASK_FIELDS,
  TASKS_QUERY_API_PATH,
);

export const eventsExecutor: IDataQueryExecutor = createSalesforceDataQueryExecutor(
  EVENTS_SOURCE_ID,
  EVENTS_SOURCE_NAME,
  EVENT_FIELDS,
  EVENTS_QUERY_API_PATH,
);

export const ordersExecutor: IDataQueryExecutor = createSalesforceDataQueryExecutor(
  ORDERS_SOURCE_ID,
  ORDERS_SOURCE_NAME,
  ORDER_FIELDS,
  ORDERS_QUERY_API_PATH,
);

export const salesforceDataQueryExecutors: IDataQueryExecutor[] = [
  accountsExecutor,
  opportunitiesExecutor,
  casesExecutor,
  tasksExecutor,
  eventsExecutor,
  ordersExecutor,
];
