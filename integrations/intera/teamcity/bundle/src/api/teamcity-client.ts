import {
  DEFAULT_REQUEST_TIMEOUT_MS,
  MAX_RESPONSE_BYTES,
  TOTAL_COUNT_HEADER,
} from '../constants';
import type { IApiEnvelope, IDataPage, IDataQuery, IPluginConfig } from '../types';

export const BUILD_TYPES_QUERY_API_PATH = '/api/BuildType/query';
export const TEST_RESULTS_QUERY_API_PATH = '/api/TestResult/query';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function buildApiUrl(config: IPluginConfig, apiPath: string): URL {
  const normalizedPath = apiPath.startsWith('/') ? apiPath : `/${apiPath}`;
  return new URL(normalizedPath, `${config.serviceUrl.replace(/\/+$/, '')}/`);
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
      throw new Error(`TeamCity API response exceeded ${MAX_RESPONSE_BYTES} bytes for ${path}`);
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
        throw new Error(`TeamCity API response exceeded ${MAX_RESPONSE_BYTES} bytes for ${path}`);
      }
      chunks.push(value);
    }
  }

  return Buffer.concat(chunks, totalBytes).toString('utf8');
}

function assertApiEnvelope(value: unknown, path: string): IApiEnvelope<unknown> {
  if (!isRecord(value) || typeof value.ok !== 'boolean' || !('data' in value) || !('error' in value)) {
    throw new Error(`TeamCity API returned an invalid response for ${path}: expected { ok, data, error }`);
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

function describeFailure(status: number, bodyText: string, path: string, secret: string): string {
  try {
    const parsed = JSON.parse(bodyText) as unknown;
    if (isRecord(parsed) && typeof parsed.error === 'string' && parsed.error.trim().length > 0) {
      const sanitized = redactSecret(parsed.error.trim(), secret).slice(0, 500);
      return `TeamCity API ${status} for ${path}: ${sanitized}`;
    }
  } catch {
    // Ignore non-JSON body so raw HTML or upstream payloads are never echoed.
  }

  return `TeamCity API request failed with status ${status} for ${path}`;
}

export class TeamCityClient {
  constructor(private readonly config: IPluginConfig) {}

  queryBuildTypes(query: IDataQuery, signal?: AbortSignal): Promise<unknown> {
    return this.postQuery(BUILD_TYPES_QUERY_API_PATH, query, signal);
  }

  queryTestResults(query: IDataQuery, signal?: AbortSignal): Promise<unknown> {
    return this.postQuery(TEST_RESULTS_QUERY_API_PATH, query, signal);
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
        throw new Error(`TeamCity API returned an empty response body for ${apiPath}`);
      }

      let parsed: unknown;
      try {
        parsed = JSON.parse(bodyText) as unknown;
      } catch {
        throw new Error(`TeamCity API returned invalid JSON for ${apiPath}`);
      }

      const envelope = assertApiEnvelope(parsed, apiPath);

      if (!envelope.ok) {
        const message = typeof envelope.error === 'string' && envelope.error.trim().length > 0
          ? redactSecret(envelope.error.trim(), this.config.serviceToken).slice(0, 500)
          : `TeamCity API returned ok=false for ${apiPath}`;
        throw new Error(message);
      }

      if (envelope.data === null) {
        throw new Error(`TeamCity API returned ok=true without data for ${apiPath}`);
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
        throw new Error(`TeamCity API request timed out after ${DEFAULT_REQUEST_TIMEOUT_MS} ms for ${apiPath}`);
      }
      if (error instanceof Error && this.config.serviceToken.length > 0 && error.message.includes(this.config.serviceToken)) {
        throw new Error(redactSecret(error.message, this.config.serviceToken));
      }

      throw error;
    }
  }
}

export function createTeamCityClient(config: IPluginConfig): TeamCityClient {
  return new TeamCityClient(config);
}
