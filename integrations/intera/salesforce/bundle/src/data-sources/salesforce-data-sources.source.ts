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
import type {
  IDataQuery,
  IDataSource,
  IDataSourceContext,
  IDataSourceFieldDescriptor,
  IDataSourceParams,
} from '../types';

/**
 * Field descriptors, one list per data source. Every name must exist in the matching allowlist in
 * `Intera.Salesforce.Api/Models/Query/SalesforceQueryFields.cs` — the SOQL builder rejects a field
 * it does not know, so a rename has to happen on both sides at once.
 *
 * Only standard Salesforce fields are here. Custom `__c` fields differ per org and cannot live in a
 * list compiled into a plugin build; supporting them means reading the org's own describe at
 * runtime, which is a separate feature.
 *
 * `description` is present but is a long text area upstream: it can be shown, not filtered, sorted
 * or grouped on. Salesforce refuses all three, and the API turns that into a 400 naming the field.
 */
const OWNER_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'owner_id', type: 'string', label: 'Owner ID' },
  { name: 'owner_name', type: 'string', label: 'Owner' },
];

const AUDIT_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'created_date', type: 'date', label: 'Created' },
  { name: 'last_modified_date', type: 'date', label: 'Last Modified' },
];

export const ACCOUNT_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'account_id', type: 'string', label: 'Account ID' },
  { name: 'account_name', type: 'string', label: 'Account' },
  { name: 'account_number', type: 'string', label: 'Account Number' },
  { name: 'account_type', type: 'string', label: 'Type' },
  { name: 'industry', type: 'string', label: 'Industry' },
  { name: 'rating', type: 'string', label: 'Rating' },
  { name: 'ownership', type: 'string', label: 'Ownership' },
  { name: 'annual_revenue', type: 'number', label: 'Annual Revenue' },
  { name: 'employees', type: 'number', label: 'Employees' },
  { name: 'phone', type: 'string', label: 'Phone' },
  { name: 'fax', type: 'string', label: 'Fax' },
  { name: 'website', type: 'string', label: 'Website' },
  { name: 'billing_street', type: 'string', label: 'Billing Street' },
  { name: 'billing_city', type: 'string', label: 'Billing City' },
  { name: 'billing_state', type: 'string', label: 'Billing State' },
  { name: 'billing_postal_code', type: 'string', label: 'Billing Postal Code' },
  { name: 'billing_country', type: 'string', label: 'Billing Country' },
  { name: 'shipping_city', type: 'string', label: 'Shipping City' },
  { name: 'shipping_state', type: 'string', label: 'Shipping State' },
  { name: 'shipping_country', type: 'string', label: 'Shipping Country' },
  { name: 'parent_account_id', type: 'string', label: 'Parent Account ID' },
  { name: 'parent_account_name', type: 'string', label: 'Parent Account' },
  ...OWNER_FIELDS,
  { name: 'description', type: 'string', label: 'Description' },
  { name: 'last_activity_date', type: 'date', label: 'Last Activity' },
  ...AUDIT_FIELDS,
];

export const OPPORTUNITY_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'opportunity_id', type: 'string', label: 'Opportunity ID' },
  { name: 'opportunity_name', type: 'string', label: 'Opportunity' },
  { name: 'account_id', type: 'string', label: 'Account ID' },
  { name: 'account_name', type: 'string', label: 'Account' },
  { name: 'amount', type: 'number', label: 'Amount' },
  { name: 'expected_revenue', type: 'number', label: 'Expected Revenue' },
  { name: 'quantity', type: 'number', label: 'Quantity' },
  // Free text from the org's own pipeline setup, not a fixed list: declaring enumValues here would
  // put a made-up set of stage names in the host's filter dropdown.
  { name: 'stage', type: 'string', label: 'Stage' },
  { name: 'probability', type: 'number', label: 'Probability (%)' },
  { name: 'is_closed', type: 'boolean', label: 'Closed' },
  { name: 'is_won', type: 'boolean', label: 'Won' },
  { name: 'forecast_category', type: 'string', label: 'Forecast Category' },
  { name: 'opportunity_type', type: 'string', label: 'Type' },
  { name: 'lead_source', type: 'string', label: 'Lead Source' },
  { name: 'next_step', type: 'string', label: 'Next Step' },
  { name: 'campaign_id', type: 'string', label: 'Campaign ID' },
  { name: 'has_line_items', type: 'boolean', label: 'Has Products' },
  { name: 'fiscal_year', type: 'number', label: 'Fiscal Year' },
  { name: 'fiscal_quarter', type: 'number', label: 'Fiscal Quarter' },
  ...OWNER_FIELDS,
  { name: 'description', type: 'string', label: 'Description' },
  { name: 'close_date', type: 'date', label: 'Close Date' },
  { name: 'last_activity_date', type: 'date', label: 'Last Activity' },
  ...AUDIT_FIELDS,
];

export const CASE_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'case_id', type: 'string', label: 'Case ID' },
  { name: 'case_number', type: 'string', label: 'Case Number' },
  { name: 'subject', type: 'string', label: 'Subject' },
  { name: 'status', type: 'string', label: 'Status' },
  { name: 'priority', type: 'string', label: 'Priority' },
  { name: 'origin', type: 'string', label: 'Origin' },
  { name: 'case_type', type: 'string', label: 'Type' },
  { name: 'reason', type: 'string', label: 'Reason' },
  { name: 'is_closed', type: 'boolean', label: 'Closed' },
  { name: 'is_escalated', type: 'boolean', label: 'Escalated' },
  { name: 'account_id', type: 'string', label: 'Account ID' },
  { name: 'account_name', type: 'string', label: 'Account' },
  { name: 'contact_id', type: 'string', label: 'Contact ID' },
  { name: 'contact_name', type: 'string', label: 'Contact' },
  { name: 'contact_email', type: 'string', label: 'Contact Email' },
  ...OWNER_FIELDS,
  { name: 'description', type: 'string', label: 'Description' },
  { name: 'closed_date', type: 'date', label: 'Closed Date' },
  ...AUDIT_FIELDS,
];

export const TASK_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'task_id', type: 'string', label: 'Task ID' },
  { name: 'subject', type: 'string', label: 'Subject' },
  { name: 'status', type: 'string', label: 'Status' },
  { name: 'priority', type: 'string', label: 'Priority' },
  { name: 'is_closed', type: 'boolean', label: 'Closed' },
  { name: 'is_high_priority', type: 'boolean', label: 'High Priority' },
  { name: 'subtype', type: 'string', label: 'Subtype' },
  { name: 'call_type', type: 'string', label: 'Call Type' },
  { name: 'call_disposition', type: 'string', label: 'Call Result' },
  { name: 'call_duration_seconds', type: 'number', label: 'Call Duration (s)' },
  { name: 'is_recurrence', type: 'boolean', label: 'Recurring' },
  ...OWNER_FIELDS,
  { name: 'who_id', type: 'string', label: 'Contact/Lead ID' },
  { name: 'who_name', type: 'string', label: 'Contact/Lead' },
  { name: 'what_id', type: 'string', label: 'Related To ID' },
  // Selectable only. "Related to" can point at a Case, which has a number rather than a name, so
  // Salesforce refuses this traversal in WHERE, ORDER BY and GROUP BY. Filter on what_id instead.
  { name: 'what_name', type: 'string', label: 'Related To' },
  { name: 'account_id', type: 'string', label: 'Account ID' },
  { name: 'description', type: 'string', label: 'Comments' },
  { name: 'activity_date', type: 'date', label: 'Due Date' },
  ...AUDIT_FIELDS,
];

export const EVENT_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'event_id', type: 'string', label: 'Event ID' },
  { name: 'subject', type: 'string', label: 'Subject' },
  { name: 'location', type: 'string', label: 'Location' },
  { name: 'show_as', type: 'string', label: 'Show As' },
  { name: 'subtype', type: 'string', label: 'Subtype' },
  { name: 'is_all_day', type: 'boolean', label: 'All Day' },
  { name: 'is_private', type: 'boolean', label: 'Private' },
  { name: 'is_recurrence', type: 'boolean', label: 'Recurring' },
  { name: 'duration_minutes', type: 'number', label: 'Duration (min)' },
  ...OWNER_FIELDS,
  { name: 'who_id', type: 'string', label: 'Contact/Lead ID' },
  { name: 'who_name', type: 'string', label: 'Contact/Lead' },
  { name: 'what_id', type: 'string', label: 'Related To ID' },
  { name: 'what_name', type: 'string', label: 'Related To' },
  { name: 'account_id', type: 'string', label: 'Account ID' },
  { name: 'description', type: 'string', label: 'Description' },
  { name: 'activity_date', type: 'date', label: 'Date' },
  { name: 'start_at', type: 'date', label: 'Start' },
  { name: 'end_at', type: 'date', label: 'End' },
  ...AUDIT_FIELDS,
];

export const ORDER_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'order_id', type: 'string', label: 'Order ID' },
  { name: 'order_number', type: 'string', label: 'Order Number' },
  { name: 'account_id', type: 'string', label: 'Account ID' },
  { name: 'account_name', type: 'string', label: 'Account' },
  { name: 'contract_id', type: 'string', label: 'Contract ID' },
  { name: 'status', type: 'string', label: 'Status' },
  // Draft or Activated in every org, unlike `status`, whose values the org configures.
  { name: 'status_code', type: 'string', label: 'Status Code' },
  { name: 'order_type', type: 'string', label: 'Type' },
  { name: 'total_amount', type: 'number', label: 'Total Amount' },
  { name: 'po_number', type: 'string', label: 'PO Number' },
  { name: 'order_reference_number', type: 'string', label: 'Order Reference' },
  { name: 'is_reduction_order', type: 'boolean', label: 'Reduction Order' },
  { name: 'billing_city', type: 'string', label: 'Billing City' },
  { name: 'billing_country', type: 'string', label: 'Billing Country' },
  { name: 'shipping_city', type: 'string', label: 'Shipping City' },
  { name: 'shipping_country', type: 'string', label: 'Shipping Country' },
  ...OWNER_FIELDS,
  { name: 'description', type: 'string', label: 'Description' },
  { name: 'effective_date', type: 'date', label: 'Start Date' },
  { name: 'end_date', type: 'date', label: 'End Date' },
  { name: 'po_date', type: 'date', label: 'PO Date' },
  { name: 'activated_date', type: 'date', label: 'Activated' },
  ...AUDIT_FIELDS,
];

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
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

function parseJsonArrayParam(value: unknown, fieldName: string): unknown[] {
  const parsed = parseJsonParam(value, fieldName);

  if (parsed === undefined) {
    return [];
  }

  return Array.isArray(parsed) ? parsed : [parsed];
}

// Core's IDataSourceParams sends `expressions` flat (top-level), not wrapped in `result` — `result` is
// only used by the legacy IKpiDataProvider.execute() query shape. Support both. When neither is set,
// `expressions` was explicitly omitted by the caller, meaning "return rows as-is" — omit `result`
// entirely so the API falls back to its own passthrough instead of us guessing a field list.
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

  // Explicit grouping keys (canonical contract, 2026-08-28). Core sends them flat on `params`,
  // exactly like `limit`/`offset`/`orderBy`, while the DSL carries them inside `result` — so they
  // have to be lifted in here, and they are only meaningful alongside expressions: on their own
  // there is nothing to group. Ten plugins were dropping them silently until 2026-09-03, which is
  // why an explicit groupBy did nothing even after the API learned to bind it.
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

/**
 * Maps core's paging/ordering params onto the DSL (host task grid-server-paging).
 *
 * `limit` → `top`, `offset` → `skip`, `orderBy` → `order`. `offset` is what makes the request a
 * PAGE: the API answers a query carrying `skip` with an `X-Total-Count` header, which the client
 * turns into `{ rows, total }`. Without it the response stays a bare array and the host pages
 * client-side.
 *
 * Salesforce caps `OFFSET` at 2000 and the API refuses anything larger with a 400 rather than
 * quietly truncating — so a grid whose filter matches more than 2000 rows can page through the
 * first 2000 and has to be narrowed past that.
 */
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

function parseNonNegativeInteger(value: unknown): number | undefined {
  const parsed = typeof value === 'number' ? value : Number.parseInt(String(value ?? ''), 10);
  return Number.isInteger(parsed) && parsed >= 0 ? parsed : undefined;
}

/**
 * Core sends `[{ field, direction }]`; the DSL wants the same shape. Malformed entries are dropped
 * rather than thrown on — an unordered page is a visible degradation, a 500 on a stray param is not.
 */
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
    // Direction is optional in the DSL (the API defaults to ascending), so an entry without one
    // is kept rather than dropped — narrowing a caller's order list would silently change the
    // rows a page contains.
    const direction = entry.direction;
    order.push(direction === 'asc' || direction === 'desc' ? { field, direction } : { field });
  }

  return order;
}

function createSalesforceQueryDataSource(
  id: string,
  name: string,
  description: string,
  fields: IDataSourceFieldDescriptor[],
  apiPath: string,
): IDataSource {
  return {
    id,
    name,
    description,
    fields,
    async getData(params: IDataSourceParams, context: IDataSourceContext): Promise<unknown> {
      const query = normalizeDataQuery(params);
      const client = createSalesforceClient(readSalesforceConfig(context));
      return client.queryRaw(apiPath, query, context?.signal);
    },
  };
}

export const accountsDataSource: IDataSource = createSalesforceQueryDataSource(
  ACCOUNTS_SOURCE_ID,
  ACCOUNTS_SOURCE_NAME,
  'Customers and companies. `account_id` is the key every other source here links back to. Filters and ordering are compiled into SOQL and run inside Salesforce, so a query over a large org costs one request.',
  ACCOUNT_FIELDS,
  ACCOUNTS_QUERY_API_PATH,
);

export const opportunitiesDataSource: IDataSource = createSalesforceQueryDataSource(
  OPPORTUNITIES_SOURCE_ID,
  OPPORTUNITIES_SOURCE_NAME,
  'The sales pipeline. `stage` holds the org’s own stage names, so a portable “won this quarter” filter should use `is_won` + `close_date` instead — those are Salesforce’s own derived flags.',
  OPPORTUNITY_FIELDS,
  OPPORTUNITIES_QUERY_API_PATH,
);

export const casesDataSource: IDataSource = createSalesforceQueryDataSource(
  CASES_SOURCE_ID,
  CASES_SOURCE_NAME,
  'Support cases, with the account and contact resolved in the same query — no second lookup needed to show a customer name.',
  CASE_FIELDS,
  CASES_QUERY_API_PATH,
);

export const tasksDataSource: IDataSource = createSalesforceQueryDataSource(
  TASKS_SOURCE_ID,
  TASKS_SOURCE_NAME,
  'Calls, e-mails and to-dos. One half of Salesforce’s “activities” — **Events** is the other, and a separate source because the two objects carry different fields. They line up on `activity_date`, `owner_id`, `who_id` and `what_id`.',
  TASK_FIELDS,
  TASKS_QUERY_API_PATH,
);

export const eventsDataSource: IDataSource = createSalesforceQueryDataSource(
  EVENTS_SOURCE_ID,
  EVENTS_SOURCE_NAME,
  'Meetings and anything else with a start and an end. The counterpart of **Tasks**; `activity_date` is the day it starts, `start_at` / `end_at` carry the time.',
  EVENT_FIELDS,
  EVENTS_QUERY_API_PATH,
);

export const ordersDataSource: IDataSource = createSalesforceQueryDataSource(
  ORDERS_SOURCE_ID,
  ORDERS_SOURCE_NAME,
  'Confirmed customer orders. Orders is a Salesforce feature an org can have switched off — this source then reports the error rather than an empty list, because “the object is not there” is not the same as “there are no orders”.',
  ORDER_FIELDS,
  ORDERS_QUERY_API_PATH,
);

export const salesforceDataSources: IDataSource[] = [
  accountsDataSource,
  opportunitiesDataSource,
  casesDataSource,
  tasksDataSource,
  eventsDataSource,
  ordersDataSource,
];
