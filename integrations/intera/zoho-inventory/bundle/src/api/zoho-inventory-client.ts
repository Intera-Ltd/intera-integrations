import {
  DEFAULT_REQUEST_TIMEOUT_MS,
  TOTAL_COUNT_HEADER,
} from '../constants';
import type {
  IDataPage,
  IDataQuery,
  IZohoInventoryApiResponse,
  IZohoInventoryConfig,
  IZohoOrganization,
  IZohoSchema,
} from '../types';

export const CUSTOMERS_QUERY_API_PATH = '/api/customer/query';
export const VENDORS_QUERY_API_PATH = '/api/vendor/query';
export const ITEMS_QUERY_API_PATH = '/api/item/query';
export const LOCATIONS_QUERY_API_PATH = '/api/location/query';
export const SALES_ORDERS_QUERY_API_PATH = '/api/salesorder/query';
export const SALES_ORDER_LINES_QUERY_API_PATH = '/api/salesorderline/query';
export const PURCHASE_ORDERS_QUERY_API_PATH = '/api/purchaseorder/query';
export const PURCHASE_ORDER_LINES_QUERY_API_PATH = '/api/purchaseorderline/query';
export const PURCHASE_RECEIVES_QUERY_API_PATH = '/api/purchasereceive/query';
export const PURCHASE_RECEIVE_LINES_QUERY_API_PATH = '/api/purchasereceiveline/query';
export const PACKAGES_QUERY_API_PATH = '/api/package/query';
export const PACKAGE_LINES_QUERY_API_PATH = '/api/packageline/query';
export const SHIPMENTS_QUERY_API_PATH = '/api/shipment/query';
export const SHIPMENT_LINES_QUERY_API_PATH = '/api/shipmentline/query';
export const CONTACTS_QUERY_API_PATH = '/api/contactperson/query';
export const SALESPERSONS_QUERY_API_PATH = '/api/salesperson/query';
export const INVENTORY_BY_ITEM_QUERY_API_PATH = '/api/inventorybyitem/query';
export const INVENTORY_BY_LOCATION_QUERY_API_PATH = '/api/inventorybylocation/query';
export const INVOICES_QUERY_API_PATH = '/api/invoice/query';
export const INVOICE_LINES_QUERY_API_PATH = '/api/invoiceline/query';
export const BILLS_QUERY_API_PATH = '/api/bill/query';
export const BILL_LINES_QUERY_API_PATH = '/api/billline/query';

export const SCHEMA_API_PATH = '/api/schema';
export const ORGANIZATION_API_PATH = '/api/organization';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function buildApiUrl(config: IZohoInventoryConfig, apiPath: string): URL {
  const baseUrl = config.apiBaseUrl.replace(/\/+$/, '');
  const normalizedPath = apiPath.startsWith('/') ? apiPath : `/${apiPath}`;
  return new URL(normalizedPath, `${baseUrl}/`);
}

function assertApiEnvelope<T>(value: unknown, path: string): IZohoInventoryApiResponse<T> {
  if (!isRecord(value)) {
    throw new Error(`Zoho Inventory API returned an invalid response for ${path}: expected object`);
  }

  if (typeof value.ok !== 'boolean' || !('data' in value) || !('error' in value)) {
    throw new Error(`Zoho Inventory API returned an invalid response for ${path}: expected { ok, data, error }`);
  }

  return value as unknown as IZohoInventoryApiResponse<T>;
}

function readTotalCountHeader(response: Response): number | null {
  const raw = response.headers.get(TOTAL_COUNT_HEADER);
  if (raw === null) {
    return null;
  }

  const parsed = Number.parseInt(raw, 10);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
}

function describeFailure(status: number, bodyText: string, path: string): string {
  try {
    const parsed = JSON.parse(bodyText) as unknown;
    if (isRecord(parsed) && typeof parsed.error === 'string' && parsed.error.trim().length > 0) {
      return `Zoho Inventory API ${status} for ${path}: ${parsed.error.trim()}`;
    }
  } catch {
    // Ignore non-JSON body so raw upstream responses are never echoed.
  }

  return `Zoho Inventory API request failed with status ${status} for ${path}`;
}

function unwrapApiResponse<T>(response: IZohoInventoryApiResponse<T>, path: string): T {
  if (!response.ok) {
    const message = typeof response.error === 'string' && response.error.trim().length > 0
      ? response.error.trim()
      : `Zoho Inventory API returned ok=false for ${path}`;
    throw new Error(message);
  }

  if (response.data === null) {
    throw new Error(`Zoho Inventory API returned ok=true without data for ${path}`);
  }

  return response.data;
}

function normalizeQueryResult(data: unknown, name: string): Record<string, unknown> {
  if (isRecord(data)) {
    return data;
  }

  if (Array.isArray(data) && data.length > 0 && isRecord(data[0])) {
    return data[0];
  }

  throw new Error(`Zoho Inventory ${name} response must contain an object in data`);
}

export class ZohoInventoryClient {
  constructor(private readonly config: IZohoInventoryConfig) {}

  async queryRaw(apiPath: string, query: IDataQuery, signal?: AbortSignal): Promise<unknown> {
    const { envelope, totalCount } = await this.request(apiPath, 'POST', query, signal);
    const data = unwrapApiResponse(envelope, apiPath);

    if (totalCount !== null && Array.isArray(data)) {
      return { rows: data, total: totalCount } satisfies IDataPage;
    }

    return data;
  }

  async queryObject(apiPath: string, query: IDataQuery, name: string, signal?: AbortSignal): Promise<Record<string, unknown>> {
    const { envelope } = await this.request(apiPath, 'POST', query, signal);
    return normalizeQueryResult(unwrapApiResponse(envelope, apiPath), name);
  }

  async getSchema(signal?: AbortSignal): Promise<IZohoSchema> {
    const { envelope } = await this.request(SCHEMA_API_PATH, 'GET', undefined, signal);
    return unwrapApiResponse(envelope as IZohoInventoryApiResponse<IZohoSchema>, SCHEMA_API_PATH);
  }

  async getOrganization(signal?: AbortSignal): Promise<IZohoOrganization> {
    const { envelope } = await this.request(ORGANIZATION_API_PATH, 'GET', undefined, signal);
    return unwrapApiResponse(envelope as IZohoInventoryApiResponse<IZohoOrganization>, ORGANIZATION_API_PATH);
  }

  private async request(
    apiPath: string,
    method: 'GET' | 'POST',
    body?: unknown,
    callerSignal?: AbortSignal,
  ): Promise<{ envelope: IZohoInventoryApiResponse<unknown>; totalCount: number | null }> {
    const timeoutSignal = AbortSignal.timeout(DEFAULT_REQUEST_TIMEOUT_MS);
    const signal = callerSignal ? AbortSignal.any([callerSignal, timeoutSignal]) : timeoutSignal;

    try {
      const url = buildApiUrl(this.config, apiPath);
      const headers: Record<string, string> = {
        Accept: 'application/json',
        ...(this.config.accessToken ? { Authorization: `Bearer ${this.config.accessToken}` } : {}),
        ...(method === 'POST' ? { 'Content-Type': 'application/json' } : {}),
      };

      const response = await fetch(url, {
        method,
        signal,
        headers,
        body: method === 'POST' ? JSON.stringify(body) : undefined,
      });

      const bodyText = await response.text();

      if (!response.ok) {
        throw new Error(describeFailure(response.status, bodyText, apiPath));
      }

      if (bodyText.trim().length === 0) {
        throw new Error(`Zoho Inventory API returned an empty response body for ${apiPath}`);
      }

      return {
        envelope: assertApiEnvelope<unknown>(JSON.parse(bodyText) as unknown, apiPath),
        totalCount: readTotalCountHeader(response),
      };
    } catch (error) {
      if (callerSignal?.aborted) {
        throw callerSignal.reason ?? error;
      }
      if (error instanceof Error && (error.name === 'TimeoutError' || error.name === 'AbortError')) {
        throw new Error(`Zoho Inventory API request timed out after ${DEFAULT_REQUEST_TIMEOUT_MS} ms for ${apiPath}`);
      }

      throw error;
    }
  }
}

export function createZohoInventoryClient(config: IZohoInventoryConfig): ZohoInventoryClient {
  return new ZohoInventoryClient(config);
}
