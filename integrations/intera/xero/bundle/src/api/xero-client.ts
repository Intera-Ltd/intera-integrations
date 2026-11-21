import { DEFAULT_REQUEST_TIMEOUT_MS, TENANT_HEADER, TOTAL_COUNT_HEADER } from '../constants';
import type { IApiEnvelope, IDataPage, IDataQuery, IPluginConfig } from '../types';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function buildApiUrl(config: IPluginConfig, apiPath: string): URL {
  const normalizedPath = apiPath.startsWith('/') ? apiPath : `/${apiPath}`;
  return new URL(normalizedPath, `${config.apiBaseUrl.replace(/\/+$/, '')}/`);
}

function assertApiEnvelope(value: unknown, path: string): IApiEnvelope<unknown> {
  if (!isRecord(value) || typeof value.ok !== 'boolean' || !('data' in value) || !('error' in value)) {
    throw new Error(`Xero API returned an invalid response for ${path}: expected { ok, data, error }`);
  }

  return value as unknown as IApiEnvelope<unknown>;
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
      return `Xero API ${status} for ${path}: ${parsed.error.trim()}`;
    }
  } catch {
    // Ignore non-JSON body so raw upstream responses are never echoed.
  }

  return `Xero API request failed with status ${status} for ${path}`;
}

export class XeroClient {
  constructor(private readonly config: IPluginConfig) {}

  query(apiPath: string, query: IDataQuery, signal?: AbortSignal): Promise<unknown> {
    return this.postQuery(apiPath, query, signal);
  }

  private async postQuery(apiPath: string, body: IDataQuery, callerSignal?: AbortSignal): Promise<unknown> {
    const timeoutSignal = AbortSignal.timeout(DEFAULT_REQUEST_TIMEOUT_MS);
    const signal = callerSignal ? AbortSignal.any([callerSignal, timeoutSignal]) : timeoutSignal;

    try {
      const response = await fetch(buildApiUrl(this.config, apiPath), {
        method: 'POST',
        signal,
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/json',
          ...(this.config.accessToken ? { Authorization: `Bearer ${this.config.accessToken}` } : {}),
          ...(this.config.tenantId ? { [TENANT_HEADER]: this.config.tenantId } : {}),
        },
        body: JSON.stringify(body),
      });

      const bodyText = await response.text();

      if (!response.ok) {
        throw new Error(describeFailure(response.status, bodyText, apiPath));
      }

      if (bodyText.trim().length === 0) {
        throw new Error(`Xero API returned an empty response body for ${apiPath}`);
      }

      const envelope = assertApiEnvelope(JSON.parse(bodyText) as unknown, apiPath);

      if (!envelope.ok) {
        const message = typeof envelope.error === 'string' && envelope.error.trim().length > 0
          ? envelope.error.trim()
          : `Xero API returned ok=false for ${apiPath}`;
        throw new Error(message);
      }

      if (envelope.data === null) {
        throw new Error(`Xero API returned ok=true without data for ${apiPath}`);
      }

      const totalCount = readTotalCountHeader(response);
      if (totalCount !== null && Array.isArray(envelope.data)) {
        return { rows: envelope.data, total: totalCount } satisfies IDataPage;
      }

      return envelope.data;
    } catch (error) {
      if (callerSignal?.aborted) {
        throw callerSignal.reason ?? error;
      }
      if (error instanceof Error && (error.name === 'TimeoutError' || error.name === 'AbortError')) {
        throw new Error(`Xero API request timed out after ${DEFAULT_REQUEST_TIMEOUT_MS} ms for ${apiPath}`);
      }

      throw error;
    }
  }
}

export function createXeroClient(config: IPluginConfig): XeroClient {
  return new XeroClient(config);
}
