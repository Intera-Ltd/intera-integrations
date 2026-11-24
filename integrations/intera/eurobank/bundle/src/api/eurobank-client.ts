import {
  CALLBACK_RELAY_TIMEOUT_MS,
  DEFAULT_REQUEST_TIMEOUT_MS,
  STATUS_TIMEOUT_MS,
  TOTAL_COUNT_HEADER,
} from '../constants';
import type {
  IDataPage,
  IDataQuery,
  IEurobankApiResponse,
  IEurobankConfig,
  IOAuthStatus,
} from '../types';

const EUROBANK_ACCOUNTS_QUERY_API_PATH = '/api/eurobank/accounts/query';

export function createEurobankClient(config: IEurobankConfig): EurobankClient {
  return new EurobankClient(config);
}

export class EurobankClient {
  constructor(private readonly config: IEurobankConfig) {}

  async queryAccounts(query: IDataQuery, signal?: AbortSignal): Promise<Record<string, unknown>> {
    const data = await this.queryAccountsRaw(query, signal);
    return normalizeQueryResult(data, 'accounts query');
  }

  queryAccountsRaw(query: IDataQuery, signal?: AbortSignal): Promise<unknown> {
    return this.send('POST', EUROBANK_ACCOUNTS_QUERY_API_PATH, query, DEFAULT_REQUEST_TIMEOUT_MS, signal);
  }

  get(apiPath: string, timeoutMs = DEFAULT_REQUEST_TIMEOUT_MS, signal?: AbortSignal): Promise<unknown> {
    return this.send('GET', apiPath, undefined, timeoutMs, signal);
  }

  put(apiPath: string, body: unknown, timeoutMs = DEFAULT_REQUEST_TIMEOUT_MS, signal?: AbortSignal): Promise<unknown> {
    return this.send('PUT', apiPath, body, timeoutMs, signal);
  }

  async getOAuthStatus(signal?: AbortSignal): Promise<IOAuthStatus> {
    const path = '/oauth/status';
    const response = await this.fetchWithTimeout(
      path,
      {
        method: 'GET',
        headers: {
          Accept: 'application/json',
          Authorization: `Bearer ${this.config.serviceToken}`,
        },
      },
      STATUS_TIMEOUT_MS,
      signal,
    );
    const bodyText = await response.text();

    if (!response.ok) {
      throw new Error(describeFailure(response.status, bodyText, path));
    }

    const parsed = JSON.parse(bodyText) as unknown;
    if (!isRecord(parsed) || typeof parsed.has_refresh_token !== 'boolean') {
      throw new Error(`Eurobank API returned an unexpected response shape for ${path}`);
    }

    return parsed as unknown as IOAuthStatus;
  }

  async relayOAuthCallback(
    query: URLSearchParams,
    signal?: AbortSignal,
  ): Promise<{ status: number; contentType: string; body: string }> {
    const path = `/oauth/callback?${query.toString()}`;
    const response = await this.fetchWithTimeout(
      path,
      {
        method: 'GET',
        headers: {
          Accept: 'text/html',
          Authorization: `Bearer ${this.config.serviceToken}`,
        },
      },
      CALLBACK_RELAY_TIMEOUT_MS,
      signal,
    );

    return {
      status: response.status,
      contentType: response.headers.get('content-type') ?? 'text/html; charset=utf-8',
      body: await response.text(),
    };
  }

  private async send(
    method: 'GET' | 'POST' | 'PUT',
    apiPath: string,
    body: unknown,
    timeoutMs = DEFAULT_REQUEST_TIMEOUT_MS,
    callerSignal?: AbortSignal,
  ): Promise<unknown> {
    const response = await this.fetchWithTimeout(
      apiPath,
      {
        method,
        headers: {
          Accept: 'application/json',
          Authorization: `Bearer ${this.config.serviceToken}`,
          ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        },
        ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
      },
      timeoutMs,
      callerSignal,
    );

    const bodyText = await response.text();

    if (!response.ok) {
      throw new Error(describeFailure(response.status, bodyText, apiPath));
    }

    if (bodyText.trim().length === 0) {
      throw new Error(`Eurobank API returned an empty response body for ${apiPath}`);
    }

    const envelope = assertApiEnvelope<unknown>(JSON.parse(bodyText) as unknown, apiPath);
    const data = unwrapApiResponse(envelope, apiPath);

    const totalCount = readTotalCountHeader(response);
    if (totalCount !== null && Array.isArray(data)) {
      return { rows: data, total: totalCount } satisfies IDataPage;
    }

    return data;
  }

  private async fetchWithTimeout(
    apiPath: string,
    init: RequestInit,
    timeoutMs: number,
    callerSignal?: AbortSignal,
  ): Promise<Response> {
    const timeoutSignal = AbortSignal.timeout(timeoutMs);
    const signal = callerSignal ? AbortSignal.any([callerSignal, timeoutSignal]) : timeoutSignal;

    try {
      return await fetch(buildApiUrl(this.config, apiPath), { ...init, signal });
    } catch (error) {
      if (callerSignal?.aborted) {
        throw callerSignal.reason ?? error;
      }
      if (error instanceof Error && (error.name === 'TimeoutError' || error.name === 'AbortError')) {
        throw new Error(`Eurobank API request timed out after ${timeoutMs} ms for ${apiPath.split('?')[0]}`);
      }

      throw error;
    }
  }
}

function buildApiUrl(config: IEurobankConfig, apiPath: string): URL {
  const normalizedPath = apiPath.startsWith('/') ? apiPath : `/${apiPath}`;
  return new URL(normalizedPath, `${config.apiBaseUrl.replace(/\/+$/, '')}/`);
}

function assertApiEnvelope<T>(value: unknown, path: string): IEurobankApiResponse<T> {
  if (!isRecord(value) || typeof value.ok !== 'boolean' || !('data' in value) || !('error' in value)) {
    throw new Error(`Eurobank API returned an invalid response for ${path}: expected { ok, data, error }`);
  }

  return value as unknown as IEurobankApiResponse<T>;
}

function unwrapApiResponse<T>(response: IEurobankApiResponse<T>, path: string): T {
  if (!response.ok) {
    const message =
      typeof response.error === 'string' && response.error.trim().length > 0
        ? response.error.trim()
        : `Eurobank API returned ok=false for ${path}`;
    throw new Error(message);
  }

  if (response.data === null) {
    throw new Error(`Eurobank API returned ok=true without data for ${path}`);
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

  throw new Error(`Eurobank ${name} response must contain an object in data`);
}

function describeFailure(status: number, bodyText: string, path: string): string {
  try {
    const parsed = JSON.parse(bodyText) as unknown;
    if (isRecord(parsed) && typeof parsed.error === 'string' && parsed.error.trim().length > 0) {
      return `Eurobank API ${status} for ${path}: ${parsed.error.trim()}`;
    }
  } catch {
    // Ignore non-JSON body so raw upstream responses are never echoed.
  }

  return `Eurobank API request failed with status ${status} for ${path}`;
}

function readTotalCountHeader(response: Response): number | null {
  const raw = response.headers.get(TOTAL_COUNT_HEADER);
  if (raw === null) {
    return null;
  }

  const parsed = Number.parseInt(raw, 10);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
