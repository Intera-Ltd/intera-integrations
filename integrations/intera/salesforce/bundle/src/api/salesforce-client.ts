import {
  DEFAULT_REQUEST_TIMEOUT_MS,
  TOTAL_COUNT_HEADER,
} from '../constants';
import type {
  IDataPage,
  IDataQuery,
  ISalesforceApiResponse,
  ISalesforceConfig,
} from '../types';

export const ACCOUNTS_QUERY_API_PATH = '/api/account/query';
export const OPPORTUNITIES_QUERY_API_PATH = '/api/opportunity/query';
export const CASES_QUERY_API_PATH = '/api/case/query';
export const TASKS_QUERY_API_PATH = '/api/task/query';
export const EVENTS_QUERY_API_PATH = '/api/event/query';
export const ORDERS_QUERY_API_PATH = '/api/order/query';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function buildApiUrl(config: ISalesforceConfig, apiPath: string): URL {
  const baseUrl = config.apiBaseUrl.replace(/\/+$/, '');
  const normalizedPath = apiPath.startsWith('/') ? apiPath : `/${apiPath}`;
  return new URL(normalizedPath, `${baseUrl}/`);
}

function assertApiEnvelope<T>(value: unknown, path: string): ISalesforceApiResponse<T> {
  if (!isRecord(value)) {
    throw new Error(`Salesforce API returned an invalid response for ${path}: expected object`);
  }

  if (typeof value.ok !== 'boolean' || !('data' in value) || !('error' in value)) {
    throw new Error(`Salesforce API returned an invalid response for ${path}: expected { ok, data, error }`);
  }

  return value as unknown as ISalesforceApiResponse<T>;
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
      return `Salesforce API ${status} for ${path}: ${parsed.error.trim()}`;
    }
  } catch {
    // Ignore non-JSON body so raw upstream responses are never echoed.
  }

  return `Salesforce API request failed with status ${status} for ${path}`;
}

function unwrapApiResponse<T>(response: ISalesforceApiResponse<T>, path: string): T {
  if (!response.ok) {
    const message = typeof response.error === 'string' && response.error.trim().length > 0
      ? response.error.trim()
      : `Salesforce API returned ok=false for ${path}`;
    throw new Error(message);
  }

  if (response.data === null) {
    throw new Error(`Salesforce API returned ok=true without data for ${path}`);
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

  throw new Error(`Salesforce ${name} response must contain an object in data`);
}

export class SalesforceClient {
  constructor(private readonly config: ISalesforceConfig) {}

  async queryRaw(apiPath: string, query: IDataQuery, signal?: AbortSignal): Promise<unknown> {
    return this.postEnvelopeData(apiPath, query, signal);
  }

  async queryObject(apiPath: string, query: IDataQuery, name: string, signal?: AbortSignal): Promise<Record<string, unknown>> {
    const data = await this.postEnvelopeData(apiPath, query, signal);
    return normalizeQueryResult(data, name);
  }

  private async postEnvelopeData(apiPath: string, body: unknown, signal?: AbortSignal): Promise<unknown> {
    const { envelope, totalCount } = await this.requestEnvelope(apiPath, 'POST', body, signal);
    const data = unwrapApiResponse(envelope, apiPath);

    if (totalCount !== null && Array.isArray(data)) {
      return { rows: data, total: totalCount } satisfies IDataPage;
    }

    return data;
  }

  private async requestEnvelope(
    apiPath: string,
    method: 'POST',
    body: unknown,
    callerSignal?: AbortSignal,
  ): Promise<{ envelope: ISalesforceApiResponse<unknown>; totalCount: number | null }> {
    const timeoutSignal = AbortSignal.timeout(DEFAULT_REQUEST_TIMEOUT_MS);
    const signal = callerSignal ? AbortSignal.any([callerSignal, timeoutSignal]) : timeoutSignal;

    try {
      const url = buildApiUrl(this.config, apiPath);
      const headers: Record<string, string> = {
        Accept: 'application/json',
        'Content-Type': 'application/json',
        ...(this.config.accessToken ? { Authorization: `Bearer ${this.config.accessToken}` } : {}),
      };

      const response = await fetch(url, {
        method,
        signal,
        headers,
        body: JSON.stringify(body),
      });

      const bodyText = await response.text();

      if (!response.ok) {
        throw new Error(describeFailure(response.status, bodyText, apiPath));
      }

      if (bodyText.trim().length === 0) {
        throw new Error(`Salesforce API returned an empty response body for ${apiPath}`);
      }

      const parsed = JSON.parse(bodyText) as unknown;
      return {
        envelope: assertApiEnvelope<unknown>(parsed, apiPath),
        totalCount: readTotalCountHeader(response),
      };
    } catch (error) {
      if (callerSignal?.aborted) {
        throw callerSignal.reason ?? error;
      }
      if (error instanceof Error && (error.name === 'TimeoutError' || error.name === 'AbortError')) {
        throw new Error(`Salesforce API request timed out after ${DEFAULT_REQUEST_TIMEOUT_MS} ms for ${apiPath}`);
      }

      throw error;
    }
  }
}

export function createSalesforceClient(config: ISalesforceConfig): SalesforceClient {
  return new SalesforceClient(config);
}
