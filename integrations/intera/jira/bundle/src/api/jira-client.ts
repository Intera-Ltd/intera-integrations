import { DEFAULT_REQUEST_TIMEOUT_MS, JQL_SCOPE_HEADER, SITE_HEADER, TOTAL_COUNT_HEADER } from '../constants';
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
    throw new Error(`Jira API returned an invalid response for ${path}: expected { ok, data, error }`);
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
      return `Jira API ${status} for ${path}: ${parsed.error.trim()}`;
    }
  } catch {
    // Ignore non-JSON body so raw upstream responses are never echoed.
  }

  return `Jira API request failed with status ${status} for ${path}`;
}

export class JiraClient {
  constructor(private readonly config: IPluginConfig) {}

  query(apiPath: string, query: IDataQuery, signal?: AbortSignal): Promise<unknown> {
    return this.send('POST', apiPath, query, signal);
  }

  get(apiPath: string, signal?: AbortSignal): Promise<unknown> {
    return this.send('GET', apiPath, undefined, signal);
  }

  private async send(
    method: 'GET' | 'POST',
    apiPath: string,
    body: IDataQuery | undefined,
    callerSignal?: AbortSignal,
  ): Promise<unknown> {
    const timeoutSignal = AbortSignal.timeout(DEFAULT_REQUEST_TIMEOUT_MS);
    const signal = callerSignal ? AbortSignal.any([callerSignal, timeoutSignal]) : timeoutSignal;

    try {
      const response = await fetch(buildApiUrl(this.config, apiPath), {
        method,
        signal,
        headers: {
          Accept: 'application/json',
          ...(this.config.accessToken ? { Authorization: `Bearer ${this.config.accessToken}` } : {}),
          ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
          ...(this.config.siteUrl ? { [SITE_HEADER]: this.config.siteUrl } : {}),
          ...(this.config.jqlScope ? { [JQL_SCOPE_HEADER]: encodeURIComponent(this.config.jqlScope) } : {}),
        },
        ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
      });

      const bodyText = await response.text();

      if (!response.ok) {
        throw new Error(describeFailure(response.status, bodyText, apiPath));
      }

      if (bodyText.trim().length === 0) {
        throw new Error(`Jira API returned an empty response body for ${apiPath}`);
      }

      const envelope = assertApiEnvelope(JSON.parse(bodyText) as unknown, apiPath);

      if (!envelope.ok) {
        const message = typeof envelope.error === 'string' && envelope.error.trim().length > 0
          ? envelope.error.trim()
          : `Jira API returned ok=false for ${apiPath}`;
        throw new Error(message);
      }

      if (envelope.data === null) {
        throw new Error(`Jira API returned ok=true without data for ${apiPath}`);
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
        throw new Error(`Jira API request timed out after ${DEFAULT_REQUEST_TIMEOUT_MS} ms for ${apiPath}`);
      }

      throw error;
    }
  }
}

export function createJiraClient(config: IPluginConfig): JiraClient {
  return new JiraClient(config);
}
