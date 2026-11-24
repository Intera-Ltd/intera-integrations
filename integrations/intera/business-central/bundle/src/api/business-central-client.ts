import {
  DEFAULT_REQUEST_TIMEOUT_MS,
  MAX_RESPONSE_BYTES,
  TOTAL_COUNT_HEADER,
} from '../constants';
import type {
  IBusinessCentralApiResponse,
  IBusinessCentralConfig,
  IDataPage,
  IDataQuery,
} from '../types';

const BC_COMPANIES_QUERY_API_PATH = '/api/BusinessCentral/companies/query';

function companyScopedPath(companyId: string, entitySegment: string): string {
  return `/api/BusinessCentral/${encodeURIComponent(companyId)}/${entitySegment}/query`;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function buildApiUrl(config: IBusinessCentralConfig, apiPath: string): URL {
  const baseUrl = config.serviceUrl.replace(/\/+$/, '');
  const normalizedPath = apiPath.startsWith('/') ? apiPath : `/${apiPath}`;
  return new URL(normalizedPath, `${baseUrl}/`);
}

function redactSecret(text: string, secret: string): string {
  return secret.length > 0 ? text.split(secret).join('[REDACTED]') : text;
}

async function readBoundedText(response: Response, path: string): Promise<string> {
  const contentLength = response.headers.get('content-length');
  if (contentLength !== null) {
    const declared = Number.parseInt(contentLength, 10);
    if (Number.isFinite(declared) && declared > MAX_RESPONSE_BYTES) {
      await response.body?.cancel();
      throw new Error(`Business Central API response exceeded ${MAX_RESPONSE_BYTES} bytes for ${path}`);
    }
  }

  if (response.body === null) {
    return '';
  }

  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let totalBytes = 0;

  while (true) {
    const { done, value } = await reader.read();
    if (done) {
      break;
    }
    if (value !== undefined) {
      totalBytes += value.byteLength;
      if (totalBytes > MAX_RESPONSE_BYTES) {
        await reader.cancel();
        throw new Error(`Business Central API response exceeded ${MAX_RESPONSE_BYTES} bytes for ${path}`);
      }
      chunks.push(value);
    }
  }

  return Buffer.concat(chunks, totalBytes).toString('utf8');
}

function assertApiEnvelope<T>(value: unknown, path: string): IBusinessCentralApiResponse<T> {
  if (!isRecord(value) || typeof value.ok !== 'boolean' || !('data' in value) || !('error' in value)) {
    throw new Error(`Business Central API returned an invalid response for ${path}: expected { ok, data, error }`);
  }

  return value as unknown as IBusinessCentralApiResponse<T>;
}

function readTotalCountHeader(response: Response): number | null {
  const raw = response.headers.get(TOTAL_COUNT_HEADER);
  if (raw === null) {
    return null;
  }

  const parsed = Number.parseInt(raw, 10);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
}

function describeFailure(status: number, bodyText: string, path: string, secret: string): string {
  try {
    const parsed = JSON.parse(bodyText) as unknown;
    if (isRecord(parsed) && typeof parsed.error === 'string' && parsed.error.trim().length > 0) {
      const sanitized = redactSecret(parsed.error.trim(), secret).slice(0, 500);
      return `Business Central API ${status} for ${path}: ${sanitized}`;
    }
  } catch {
    // Ignore non-JSON body so raw HTML or upstream payloads are never echoed.
  }

  return `Business Central API request failed with status ${status} for ${path}`;
}

function unwrapApiResponse<T>(response: IBusinessCentralApiResponse<T>, path: string, secret: string): T {
  if (!response.ok) {
    const message = typeof response.error === 'string' && response.error.trim().length > 0
      ? redactSecret(response.error.trim(), secret).slice(0, 500)
      : `Business Central API returned ok=false for ${path}`;
    throw new Error(message);
  }

  if (response.data === null) {
    throw new Error(`Business Central API returned ok=true without data for ${path}`);
  }

  return response.data;
}

export class BusinessCentralClient {
  constructor(private readonly config: IBusinessCentralConfig) {}

  async queryCompaniesRaw(query: IDataQuery, signal?: AbortSignal): Promise<unknown> {
    return this.postEnvelopeData(BC_COMPANIES_QUERY_API_PATH, query, signal);
  }

  async queryCustomersRaw(companyId: string, query: IDataQuery, signal?: AbortSignal): Promise<unknown> {
    return this.postEnvelopeData(companyScopedPath(companyId, 'customers'), query, signal);
  }

  async queryVendorsRaw(companyId: string, query: IDataQuery, signal?: AbortSignal): Promise<unknown> {
    return this.postEnvelopeData(companyScopedPath(companyId, 'vendors'), query, signal);
  }

  async querySalesInvoicesRaw(companyId: string, query: IDataQuery, signal?: AbortSignal): Promise<unknown> {
    return this.postEnvelopeData(companyScopedPath(companyId, 'sales-invoices'), query, signal);
  }

  async queryPurchaseInvoicesRaw(companyId: string, query: IDataQuery, signal?: AbortSignal): Promise<unknown> {
    return this.postEnvelopeData(companyScopedPath(companyId, 'purchase-invoices'), query, signal);
  }

  async queryGeneralLedgerEntriesRaw(companyId: string, query: IDataQuery, signal?: AbortSignal): Promise<unknown> {
    return this.postEnvelopeData(companyScopedPath(companyId, 'general-ledger-entries'), query, signal);
  }

  private async postEnvelopeData(apiPath: string, body: unknown, signal?: AbortSignal): Promise<unknown> {
    const { envelope, totalCount } = await this.requestEnvelope(apiPath, body, signal);
    const data = unwrapApiResponse(envelope, apiPath, this.config.serviceToken);

    if (totalCount !== null && Array.isArray(data)) {
      return { rows: data, total: totalCount } satisfies IDataPage;
    }

    return data;
  }

  private async requestEnvelope(
    apiPath: string,
    body: unknown,
    callerSignal?: AbortSignal,
  ): Promise<{ envelope: IBusinessCentralApiResponse<unknown>; totalCount: number | null }> {
    const timeoutSignal = AbortSignal.timeout(DEFAULT_REQUEST_TIMEOUT_MS);
    const signal = callerSignal ? AbortSignal.any([callerSignal, timeoutSignal]) : timeoutSignal;

    try {
      const response = await fetch(buildApiUrl(this.config, apiPath), {
        method: 'POST',
        signal,
        headers: {
          Accept: 'application/json',
          Authorization: `Bearer ${this.config.serviceToken}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(body),
      });

      const bodyText = await readBoundedText(response, apiPath);

      if (!response.ok) {
        throw new Error(describeFailure(response.status, bodyText, apiPath, this.config.serviceToken));
      }

      if (bodyText.trim().length === 0) {
        throw new Error(`Business Central API returned an empty response body for ${apiPath}`);
      }

      let parsed: unknown;
      try {
        parsed = JSON.parse(bodyText) as unknown;
      } catch {
        throw new Error(`Business Central API returned invalid JSON for ${apiPath}`);
      }

      return {
        envelope: assertApiEnvelope<unknown>(parsed, apiPath),
        totalCount: readTotalCountHeader(response),
      };
    } catch (error) {
      if (callerSignal?.aborted) {
        throw callerSignal.reason ?? error;
      }
      if (error instanceof Error && (error.name === 'TimeoutError' || error.name === 'AbortError')) {
        throw new Error(`Business Central API request timed out after ${DEFAULT_REQUEST_TIMEOUT_MS} ms for ${apiPath}`);
      }
      if (error instanceof Error && this.config.serviceToken.length > 0 && error.message.includes(this.config.serviceToken)) {
        throw new Error(redactSecret(error.message, this.config.serviceToken));
      }

      throw error;
    }
  }
}

export function createBusinessCentralClient(config: IBusinessCentralConfig): BusinessCentralClient {
  return new BusinessCentralClient(config);
}
