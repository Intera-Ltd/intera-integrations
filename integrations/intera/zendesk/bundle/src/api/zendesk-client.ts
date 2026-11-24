import {
  CALLBACK_RELAY_TIMEOUT_MS,
  DEFAULT_REQUEST_TIMEOUT_MS,
  STATUS_TIMEOUT_MS,
  TOTAL_COUNT_HEADER,
} from '../constants';
import type { IApiEnvelope, IDataPage, IDataQuery, IOAuthStatus, IPluginConfig } from '../types';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function buildApiUrl(config: IPluginConfig, apiPath: string): URL {
  const normalizedPath = apiPath.startsWith('/') ? apiPath : `/${apiPath}`;
  return new URL(normalizedPath, `${config.apiBaseUrl.replace(/\/+$/, '')}/`);
}

function assertApiEnvelope(value: unknown, path: string): IApiEnvelope<unknown> {
  if (!isRecord(value) || typeof value.ok !== 'boolean' || !('data' in value) || !('error' in value)) {
    throw new Error(`Zendesk API returned an invalid response for ${path}: expected { ok, data, error }`);
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

function redactSecret(message: string, secret: string): string {
  return secret.length > 0 ? message.split(secret).join('[REDACTED]') : message;
}

function describeFailure(status: number, bodyText: string, path: string, secret: string): string {
  try {
    const parsed = JSON.parse(bodyText) as unknown;
    if (isRecord(parsed) && typeof parsed.error === 'string' && parsed.error.trim().length > 0) {
      return redactSecret(`Zendesk API ${status} for ${path}: ${parsed.error.trim()}`, secret);
    }
  } catch {
    // Ignore non-JSON body so raw upstream responses are never echoed.
  }

  return `Zendesk API request failed with status ${status} for ${path}`;
}

export class ZendeskClient {
  constructor(private readonly config: IPluginConfig) {}

  query(apiPath: string, query: IDataQuery, signal?: AbortSignal): Promise<unknown> {
    return this.send('POST', apiPath, query, DEFAULT_REQUEST_TIMEOUT_MS, signal);
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
      throw new Error(describeFailure(response.status, bodyText, path, this.config.serviceToken));
    }

    const parsed = JSON.parse(bodyText) as unknown;
    if (!isRecord(parsed) || typeof parsed.has_refresh_token !== 'boolean') {
      throw new Error(`Zendesk API returned an unexpected response shape for ${path}`);
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
        throw new Error(`Zendesk API request timed out after ${timeoutMs} ms for ${apiPath.split('?')[0]}`);
      }
      if (error instanceof Error) {
        throw new Error(redactSecret(error.message, this.config.serviceToken));
      }

      throw error;
    }
  }

  private async send(
    method: 'GET' | 'POST' | 'PUT',
    apiPath: string,
    body: unknown,
    timeoutMs = DEFAULT_REQUEST_TIMEOUT_MS,
    callerSignal?: AbortSignal,
  ): Promise<unknown> {
    const timeoutSignal = AbortSignal.timeout(timeoutMs);
    const signal = callerSignal ? AbortSignal.any([callerSignal, timeoutSignal]) : timeoutSignal;

    try {
      const response = await fetch(buildApiUrl(this.config, apiPath), {
        method,
        signal,
        headers: {
          Accept: 'application/json',
          Authorization: `Bearer ${this.config.serviceToken}`,
          ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        },
        ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
      });

      const bodyText = await response.text();

      if (!response.ok) {
        throw new Error(describeFailure(response.status, bodyText, apiPath, this.config.serviceToken));
      }

      if (bodyText.trim().length === 0) {
        throw new Error(`Zendesk API returned an empty response body for ${apiPath}`);
      }

      const envelope = assertApiEnvelope(JSON.parse(bodyText) as unknown, apiPath);

      if (!envelope.ok) {
        const message = typeof envelope.error === 'string' && envelope.error.trim().length > 0
          ? redactSecret(envelope.error.trim(), this.config.serviceToken)
          : `Zendesk API returned ok=false for ${apiPath}`;
        throw new Error(message);
      }

      if (envelope.data === null) {
        throw new Error(`Zendesk API returned ok=true without data for ${apiPath}`);
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
        throw new Error(`Zendesk API request timed out after ${timeoutMs} ms for ${apiPath}`);
      }
      if (error instanceof Error) {
        throw new Error(redactSecret(error.message, this.config.serviceToken));
      }

      throw error;
    }
  }
}

export function createZendeskClient(config: IPluginConfig): ZendeskClient {
  return new ZendeskClient(config);
}
