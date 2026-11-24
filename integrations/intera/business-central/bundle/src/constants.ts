export const INTEGRATION_ID = 'intera-businesscentral-integration';
export const INTEGRATION_NAME = 'Microsoft Dynamics 365 Business Central';
export const INTEGRATION_VERSION = '3.2.0';

export const DIRECT_API_KIND = 'direct_api';
export const DATA_SOURCE_CAPABILITY = 'datasource';
export const DEFAULT_CACHE_TTL_SEC = 30;

export const INTERA_SERVICE_URL_CONFIG_FIELD = 'intera_service_url';
export const INTERA_SERVICE_TOKEN_CONFIG_FIELD = 'intera_service_token';
export const DEFAULT_REQUEST_TIMEOUT_MS = 200_000;
export const MAX_RESPONSE_BYTES = 64 * 1024 * 1024;

/**
 * Response header BusinessCentral.Api sets on a paged query (`QueryRequest.Skip`), carrying the row count
 * of the whole filtered set. Mirrors `ApiControllerBase.TotalCountHeaderName`.
 */
export const TOTAL_COUNT_HEADER = 'X-Total-Count';

// Company-scoped DataSources require a top-level "=" filter condition on company_id, populated from a row
// returned by the bc-companies DataSource. Business Central is multi-company, so company_id acts as a URL
// routing parameter ({companyId}/customers/query) rather than an entity column: it is extracted from the
// filter tree and stripped before the remaining filter is sent to Intera.BusinessCentral.Api.
export const COMPANY_ID_FIELD = 'company_id';
export const COMPANY_ID_FIELD_ALIAS = 'companyId';

export const BC_COMPANIES_SOURCE_ID = 'bc-companies';
export const BC_COMPANIES_SOURCE_NAME = 'Business Central Companies';

export const BC_CUSTOMERS_SOURCE_ID = 'bc-customers';
export const BC_CUSTOMERS_SOURCE_NAME = 'Business Central Customers';

export const BC_VENDORS_SOURCE_ID = 'bc-vendors';
export const BC_VENDORS_SOURCE_NAME = 'Business Central Vendors';

export const BC_SALES_INVOICES_SOURCE_ID = 'bc-sales-invoices';
export const BC_SALES_INVOICES_SOURCE_NAME = 'Business Central Sales Invoices';

export const BC_PURCHASE_INVOICES_SOURCE_ID = 'bc-purchase-invoices';
export const BC_PURCHASE_INVOICES_SOURCE_NAME = 'Business Central Purchase Invoices';

export const BC_GL_ENTRIES_SOURCE_ID = 'bc-gl-entries';
export const BC_GL_ENTRIES_SOURCE_NAME = 'Business Central General Ledger Entries';

export const DATA_SOURCE_IDS = [
  BC_COMPANIES_SOURCE_ID,
  BC_CUSTOMERS_SOURCE_ID,
  BC_VENDORS_SOURCE_ID,
  BC_SALES_INVOICES_SOURCE_ID,
  BC_PURCHASE_INVOICES_SOURCE_ID,
  BC_GL_ENTRIES_SOURCE_ID,
] as const;

export const DATA_SOURCE_NAMES = {
  [BC_COMPANIES_SOURCE_ID]: BC_COMPANIES_SOURCE_NAME,
  [BC_CUSTOMERS_SOURCE_ID]: BC_CUSTOMERS_SOURCE_NAME,
  [BC_VENDORS_SOURCE_ID]: BC_VENDORS_SOURCE_NAME,
  [BC_SALES_INVOICES_SOURCE_ID]: BC_SALES_INVOICES_SOURCE_NAME,
  [BC_PURCHASE_INVOICES_SOURCE_ID]: BC_PURCHASE_INVOICES_SOURCE_NAME,
  [BC_GL_ENTRIES_SOURCE_ID]: BC_GL_ENTRIES_SOURCE_NAME,
} as const;
