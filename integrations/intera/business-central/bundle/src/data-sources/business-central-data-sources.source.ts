import {
  normalizeDataQuery,
  parseJsonArrayParam,
} from '@intera-integrations/shared';
import { createBusinessCentralClient } from '../api/business-central-client';
import { readBusinessCentralConfig } from '../config';
import {
  BC_COMPANIES_SOURCE_ID,
  BC_COMPANIES_SOURCE_NAME,
  BC_CUSTOMERS_SOURCE_ID,
  BC_CUSTOMERS_SOURCE_NAME,
  BC_GL_ENTRIES_SOURCE_ID,
  BC_GL_ENTRIES_SOURCE_NAME,
  BC_PURCHASE_INVOICES_SOURCE_ID,
  BC_PURCHASE_INVOICES_SOURCE_NAME,
  BC_SALES_INVOICES_SOURCE_ID,
  BC_SALES_INVOICES_SOURCE_NAME,
  BC_VENDORS_SOURCE_ID,
  BC_VENDORS_SOURCE_NAME,
  COMPANY_ID_FIELD,
  COMPANY_ID_FIELD_ALIAS,
} from '../constants';
import type {
  FilterExpressionCondition,
  FilterNode,
  IDataQuery,
  IDataSource,
  IDataSourceContext,
  IDataSourceFieldDescriptor,
  IDataSourceParams,
  RowExpression,
} from '../types';

const COMPANY_ID_FIELD_DESCRIPTOR: IDataSourceFieldDescriptor = {
  name: COMPANY_ID_FIELD,
  type: 'string',
  label: 'Company ID (required filter, "=" only — pick a value from bc-companies)',
};

export const BC_COMPANIES_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'id', type: 'string', label: 'Company ID' },
  { name: 'name', type: 'string', label: 'Name' },
  { name: 'display_name', type: 'string', label: 'Display Name' },
  { name: 'business_profile_id', type: 'string', label: 'Business Profile ID' },
];

export const BC_CUSTOMERS_FIELDS: IDataSourceFieldDescriptor[] = [
  COMPANY_ID_FIELD_DESCRIPTOR,
  { name: 'id', type: 'string', label: 'Customer ID' },
  { name: 'number', type: 'string', label: 'Number' },
  { name: 'display_name', type: 'string', label: 'Display Name' },
  { name: 'type', type: 'string', label: 'Type' },
  { name: 'address_line1', type: 'string', label: 'Address' },
  { name: 'city', type: 'string', label: 'City' },
  { name: 'country', type: 'string', label: 'Country' },
  { name: 'postal_code', type: 'string', label: 'Postal Code' },
  { name: 'phone_number', type: 'string', label: 'Phone' },
  { name: 'email', type: 'string', label: 'Email' },
  { name: 'currency_code', type: 'string', label: 'Currency' },
  { name: 'balance_due', type: 'number', label: 'Balance Due' },
  { name: 'credit_limit', type: 'number', label: 'Credit Limit' },
  { name: 'blocked', type: 'string', label: 'Blocked' },
  { name: 'tax_registration_number', type: 'string', label: 'Tax Registration Number' },
  { name: 'payment_terms_id', type: 'string', label: 'Payment Terms' },
  { name: 'last_modified_date_time', type: 'date', label: 'Last Modified' },
];

export const BC_VENDORS_FIELDS: IDataSourceFieldDescriptor[] = [
  COMPANY_ID_FIELD_DESCRIPTOR,
  { name: 'id', type: 'string', label: 'Vendor ID' },
  { name: 'number', type: 'string', label: 'Number' },
  { name: 'display_name', type: 'string', label: 'Display Name' },
  { name: 'address_line1', type: 'string', label: 'Address' },
  { name: 'city', type: 'string', label: 'City' },
  { name: 'country', type: 'string', label: 'Country' },
  { name: 'postal_code', type: 'string', label: 'Postal Code' },
  { name: 'phone_number', type: 'string', label: 'Phone' },
  { name: 'email', type: 'string', label: 'Email' },
  { name: 'currency_code', type: 'string', label: 'Currency' },
  { name: 'balance', type: 'number', label: 'Balance' },
  { name: 'blocked', type: 'string', label: 'Blocked' },
  { name: 'tax_registration_number', type: 'string', label: 'Tax Registration Number' },
  { name: 'payment_terms_id', type: 'string', label: 'Payment Terms' },
  { name: 'last_modified_date_time', type: 'date', label: 'Last Modified' },
];

export const BC_SALES_INVOICES_FIELDS: IDataSourceFieldDescriptor[] = [
  COMPANY_ID_FIELD_DESCRIPTOR,
  { name: 'id', type: 'string', label: 'Invoice ID' },
  { name: 'number', type: 'string', label: 'Number' },
  { name: 'external_document_number', type: 'string', label: 'External Document Number' },
  { name: 'invoice_date', type: 'date', label: 'Invoice Date' },
  { name: 'posting_date', type: 'date', label: 'Posting Date' },
  { name: 'due_date', type: 'date', label: 'Due Date' },
  { name: 'customer_id', type: 'string', label: 'Customer ID' },
  { name: 'customer_number', type: 'string', label: 'Customer Number' },
  { name: 'customer_name', type: 'string', label: 'Customer Name' },
  { name: 'currency_code', type: 'string', label: 'Currency' },
  { name: 'status', type: 'string', label: 'Status' },
  { name: 'discount_amount', type: 'number', label: 'Discount Amount' },
  { name: 'total_amount_excluding_tax', type: 'number', label: 'Total Excl. Tax' },
  { name: 'total_tax_amount', type: 'number', label: 'Total Tax' },
  { name: 'total_amount_including_tax', type: 'number', label: 'Total Incl. Tax' },
  { name: 'remaining_amount', type: 'number', label: 'Remaining Amount' },
  { name: 'last_modified_date_time', type: 'date', label: 'Last Modified' },
];

export const BC_PURCHASE_INVOICES_FIELDS: IDataSourceFieldDescriptor[] = [
  COMPANY_ID_FIELD_DESCRIPTOR,
  { name: 'id', type: 'string', label: 'Invoice ID' },
  { name: 'number', type: 'string', label: 'Number' },
  { name: 'vendor_invoice_number', type: 'string', label: 'Vendor Invoice Number' },
  { name: 'invoice_date', type: 'date', label: 'Invoice Date' },
  { name: 'posting_date', type: 'date', label: 'Posting Date' },
  { name: 'due_date', type: 'date', label: 'Due Date' },
  { name: 'vendor_id', type: 'string', label: 'Vendor ID' },
  { name: 'vendor_number', type: 'string', label: 'Vendor Number' },
  { name: 'vendor_name', type: 'string', label: 'Vendor Name' },
  { name: 'currency_code', type: 'string', label: 'Currency' },
  { name: 'status', type: 'string', label: 'Status' },
  { name: 'total_amount_excluding_tax', type: 'number', label: 'Total Excl. Tax' },
  { name: 'total_tax_amount', type: 'number', label: 'Total Tax' },
  { name: 'total_amount_including_tax', type: 'number', label: 'Total Incl. Tax' },
  { name: 'remaining_amount', type: 'number', label: 'Remaining Amount' },
  { name: 'last_modified_date_time', type: 'date', label: 'Last Modified' },
];

export const BC_GL_ENTRIES_FIELDS: IDataSourceFieldDescriptor[] = [
  COMPANY_ID_FIELD_DESCRIPTOR,
  { name: 'id', type: 'string', label: 'Entry ID' },
  { name: 'entry_number', type: 'number', label: 'Entry Number' },
  { name: 'posting_date', type: 'date', label: 'Posting Date' },
  { name: 'document_number', type: 'string', label: 'Document Number' },
  { name: 'document_type', type: 'string', label: 'Document Type' },
  { name: 'account_id', type: 'string', label: 'Account ID' },
  { name: 'account_number', type: 'string', label: 'Account Number' },
  { name: 'description', type: 'string', label: 'Description' },
  { name: 'debit_amount', type: 'number', label: 'Debit Amount' },
  { name: 'credit_amount', type: 'number', label: 'Credit Amount' },
  { name: 'additional_currency_debit_amount', type: 'number', label: 'Add. Currency Debit' },
  { name: 'additional_currency_credit_amount', type: 'number', label: 'Add. Currency Credit' },
];

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export { normalizeDataQuery };

function isCompanyIdFieldName(field: string): boolean {
  return field === COMPANY_ID_FIELD || field === COMPANY_ID_FIELD_ALIAS;
}

function rowExpressionReferencesCompanyId(expr: RowExpression | undefined): boolean {
  if (!isRecord(expr)) {
    return false;
  }

  switch (expr.type) {
    case 'field':
      return typeof expr.field === 'string' && isCompanyIdFieldName(expr.field);
    case 'dateDiff':
      return (
        rowExpressionReferencesCompanyId(expr.start as RowExpression | undefined) ||
        rowExpressionReferencesCompanyId(expr.end as RowExpression | undefined)
      );
    case 'math':
      return (
        rowExpressionReferencesCompanyId(expr.left as RowExpression | undefined) ||
        rowExpressionReferencesCompanyId(expr.right as RowExpression | undefined)
      );
    default:
      return false;
  }
}

function filterNodeReferencesCompanyId(node: FilterNode): boolean {
  if (!isRecord(node)) {
    return false;
  }

  if (node.type === 'condition') {
    const refMatch =
      isRecord(node.value) &&
      node.value.type === 'field' &&
      typeof node.value.name === 'string' &&
      isCompanyIdFieldName(node.value.name);
    return (typeof node.field === 'string' && isCompanyIdFieldName(node.field)) || refMatch;
  }

  if (node.type === 'expression') {
    return (
      rowExpressionReferencesCompanyId(node.left) ||
      rowExpressionReferencesCompanyId(node.right)
    );
  }

  if (node.type === 'group' && Array.isArray(node.items)) {
    return node.items.some(filterNodeReferencesCompanyId);
  }

  return false;
}

function stripCompanyIdFromExpression(node: FilterExpressionCondition): { companyId?: string; node?: FilterNode } {
  if (!filterNodeReferencesCompanyId(node)) {
    return { node };
  }

  if (node.operator !== '=') {
    throw new Error(`Filter on "${COMPANY_ID_FIELD}" must use the "=" operator (got "${node.operator}").`);
  }

  const leftField = isRecord(node.left) && node.left.type === 'field' && typeof node.left.field === 'string' ? node.left.field : undefined;
  const rightField = isRecord(node.right) && node.right.type === 'field' && typeof node.right.field === 'string' ? node.right.field : undefined;
  const leftValue = isRecord(node.left) && node.left.type === 'value' ? node.left.value : undefined;
  const rightValue = isRecord(node.right) && node.right.type === 'value' ? node.right.value : undefined;

  let rawCompanyId: unknown;
  if (leftField !== undefined && isCompanyIdFieldName(leftField) && isRecord(node.right) && node.right.type === 'value') {
    rawCompanyId = rightValue;
  } else if (rightField !== undefined && isCompanyIdFieldName(rightField) && isRecord(node.left) && node.left.type === 'value') {
    rawCompanyId = leftValue;
  } else {
    throw new Error(`Filter on "${COMPANY_ID_FIELD}" must compare "${COMPANY_ID_FIELD}" directly to a non-empty string value.`);
  }

  if (typeof rawCompanyId !== 'string' || rawCompanyId.trim().length === 0) {
    throw new Error(`Filter on "${COMPANY_ID_FIELD}" must have a non-empty string value.`);
  }

  return { companyId: rawCompanyId.trim() };
}

function stripCompanyIdFromNode(node: FilterNode): { companyId?: string; node?: FilterNode } {
  if (!isRecord(node)) {
    throw new Error('Filter entry must be an object.');
  }

  if (node.type === 'condition') {
    if (!isCompanyIdFieldName(node.field)) {
      if (isRecord(node.value) && node.value.type === 'field' && typeof node.value.name === 'string' && isCompanyIdFieldName(node.value.name)) {
        throw new Error(`Filter on "${COMPANY_ID_FIELD}" must compare "${COMPANY_ID_FIELD}" directly to a non-empty string value.`);
      }
      return { node };
    }

    if (node.operator !== '=') {
      throw new Error(`Filter on "${COMPANY_ID_FIELD}" must use the "=" operator (got "${node.operator}").`);
    }

    if (typeof node.value !== 'string' || node.value.trim().length === 0) {
      throw new Error(`Filter on "${COMPANY_ID_FIELD}" must have a non-empty string value.`);
    }

    return { companyId: node.value.trim() };
  }

  if (node.type === 'expression') {
    return stripCompanyIdFromExpression(node);
  }

  if (node.type !== 'group' || !Array.isArray(node.items)) {
    return { node };
  }

  if (node.operator !== 'AND') {
    if (filterNodeReferencesCompanyId(node)) {
      throw new Error(
        `Filter on "${COMPANY_ID_FIELD}" must be a top-level "=" condition combined with AND, not inside "${node.operator}".`,
      );
    }

    return { node };
  }

  let companyId: string | undefined;
  const items: FilterNode[] = [];

  for (const item of node.items) {
    const stripped = stripCompanyIdFromNode(item);

    if (stripped.companyId !== undefined) {
      if (companyId !== undefined && companyId !== stripped.companyId) {
        throw new Error(`Conflicting "${COMPANY_ID_FIELD}" filter values in the same query.`);
      }

      companyId = stripped.companyId;
    }

    if (stripped.node !== undefined) {
      items.push(stripped.node);
    }
  }

  return items.length > 0 ? { companyId, node: { type: 'group', operator: node.operator, items } } : { companyId };
}

function extractCompanyId(rawParams: IDataSourceParams): { companyId: string; strippedParams: IDataSourceParams } {
  const filterItems = [
    ...parseJsonArrayParam(rawParams.filter, 'filter'),
    ...parseJsonArrayParam(rawParams.filters, 'filters'),
  ] as FilterNode[];

  let companyId: string | undefined;
  const remaining: FilterNode[] = [];

  for (const item of filterItems) {
    const stripped = stripCompanyIdFromNode(item);

    if (stripped.companyId !== undefined) {
      if (companyId !== undefined && companyId !== stripped.companyId) {
        throw new Error(`Conflicting "${COMPANY_ID_FIELD}" filter values in the same query.`);
      }

      companyId = stripped.companyId;
    }

    if (stripped.node !== undefined) {
      remaining.push(stripped.node);
    }
  }

  if (companyId === undefined) {
    throw new Error(
      `A filter condition "${COMPANY_ID_FIELD}" = "<company id from ${BC_COMPANIES_SOURCE_ID}>" is required for this DataSource.`,
    );
  }

  const { filter: _filter, filters: _filters, ...restParams } = rawParams;

  return {
    companyId,
    strippedParams: { ...restParams, ...(remaining.length > 0 ? { filters: remaining } : {}) },
  };
}

type BcCompanyScopedExecutor = (
  client: ReturnType<typeof createBusinessCentralClient>,
  companyId: string,
  query: IDataQuery,
  signal?: AbortSignal,
) => Promise<unknown>;

function createBcCompanyScopedDataSource(
  id: string,
  name: string,
  description: string,
  fields: IDataSourceFieldDescriptor[],
  execute: BcCompanyScopedExecutor,
): IDataSource {
  return {
    id,
    name,
    description,
    fields,
    async getData(params: IDataSourceParams, context: IDataSourceContext): Promise<unknown> {
      const config = readBusinessCentralConfig(context);
      const { companyId, strippedParams } = extractCompanyId(params);
      const query = normalizeDataQuery(strippedParams);
      const client = createBusinessCentralClient(config);
      return execute(client, companyId, query, context?.signal);
    },
  };
}

export const bcCompaniesDataSource: IDataSource = {
  id: BC_COMPANIES_SOURCE_ID,
  name: BC_COMPANIES_SOURCE_NAME,
  description: 'Companies in the Business Central tenant — run this first to pick a company id for the other Business Central DataSources.',
  fields: BC_COMPANIES_FIELDS,
  async getData(params: IDataSourceParams, context: IDataSourceContext): Promise<unknown> {
    const config = readBusinessCentralConfig(context);
    const query = normalizeDataQuery(params);
    const client = createBusinessCentralClient(config);
    return client.queryCompaniesRaw(query, context?.signal);
  },
};

export const bcCustomersDataSource: IDataSource = createBcCompanyScopedDataSource(
  BC_CUSTOMERS_SOURCE_ID,
  BC_CUSTOMERS_SOURCE_NAME,
  'Business Central customer master data.',
  BC_CUSTOMERS_FIELDS,
  (client, companyId, query, signal) => client.queryCustomersRaw(companyId, query, signal),
);

export const bcVendorsDataSource: IDataSource = createBcCompanyScopedDataSource(
  BC_VENDORS_SOURCE_ID,
  BC_VENDORS_SOURCE_NAME,
  'Business Central vendor/supplier master data.',
  BC_VENDORS_FIELDS,
  (client, companyId, query, signal) => client.queryVendorsRaw(companyId, query, signal),
);

export const bcSalesInvoicesDataSource: IDataSource = createBcCompanyScopedDataSource(
  BC_SALES_INVOICES_SOURCE_ID,
  BC_SALES_INVOICES_SOURCE_NAME,
  'Business Central sales invoices.',
  BC_SALES_INVOICES_FIELDS,
  (client, companyId, query, signal) => client.querySalesInvoicesRaw(companyId, query, signal),
);

export const bcPurchaseInvoicesDataSource: IDataSource = createBcCompanyScopedDataSource(
  BC_PURCHASE_INVOICES_SOURCE_ID,
  BC_PURCHASE_INVOICES_SOURCE_NAME,
  'Business Central purchase invoices.',
  BC_PURCHASE_INVOICES_FIELDS,
  (client, companyId, query, signal) => client.queryPurchaseInvoicesRaw(companyId, query, signal),
);

export const bcGeneralLedgerEntriesDataSource: IDataSource = createBcCompanyScopedDataSource(
  BC_GL_ENTRIES_SOURCE_ID,
  BC_GL_ENTRIES_SOURCE_NAME,
  'Business Central general ledger entries (standard fields only — no dimensions, see field notes).',
  BC_GL_ENTRIES_FIELDS,
  (client, companyId, query, signal) => client.queryGeneralLedgerEntriesRaw(companyId, query, signal),
);

export const businessCentralDataSources: IDataSource[] = [
  bcCompaniesDataSource,
  bcCustomersDataSource,
  bcVendorsDataSource,
  bcSalesInvoicesDataSource,
  bcPurchaseInvoicesDataSource,
  bcGeneralLedgerEntriesDataSource,
];
