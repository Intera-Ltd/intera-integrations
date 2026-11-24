import {
  DOWNLOAD_TIMEOUT_MS,
  FILE_CACHE_ENTRIES,
  GRAPH_API_BASE,
  MAX_FILE_BYTES,
  MAX_JSON_RESPONSE_BYTES,
  REQUEST_TIMEOUT_MS,
} from '../constants';
import type { IExcelConfig } from '../types';
import { credentialFingerprint, type IAccessToken } from './client-credentials';
import {
  fetchBoundedBytes,
  redactSecrets,
  validateHttpsUrl,
  type IBoundedBytesResponse,
} from './http';
import { getAccessToken, invalidateAccessToken } from './token-cache';

/**
 * Microsoft Graph drive-item metadata and bounded `.xlsx` workbook downloader.
 *
 * Items are addressed strictly via `/drives/{drive-id}/items/{item-id}`, the
 * endpoint supported by item-scoped `Files.SelectedOperations.Selected` and
 * site-scoped `Sites.Selected` application permissions.
 */

export interface IDriveItem {
  name: string;
  size: number;
  /** Content tag (`cTag`, else `eTag`) used to validate cached workbook bytes. */
  contentTag: string | null;
  webUrl: string | null;
  lastModified: string | null;
  /** Pre-authenticated short-lived download URL when included by Graph. */
  downloadUrl: string | null;
}

interface ICachedFile {
  contentTag: string;
  bytes: Buffer;
}

interface IInFlightDownload {
  promise: Promise<Buffer>;
  controller: AbortController;
  waiters: number;
}

const SELECTED_SCOPES = new Set([
  'Files.SelectedOperations.Selected',
  'Sites.Selected',
]);

/** Bounded LRU workbook cache keyed by `<credentialSha256>|<driveId>/<itemId>`. */
const files = new Map<string, ICachedFile>();
const downloads = new Map<string, IInFlightDownload>();

export async function getDriveItem(
  config: IExcelConfig,
  signal?: AbortSignal,
): Promise<IDriveItem> {
  const payload = await requestJson<{
    name?: string;
    size?: number;
    cTag?: string;
    eTag?: string;
    webUrl?: string;
    lastModifiedDateTime?: string;
    file?: unknown;
    folder?: unknown;
    '@microsoft.graph.downloadUrl'?: string;
  }>(config, itemPath(config), signal);

  const name = payload.name ?? config.itemId;
  if (payload.file === undefined) {
    throw new Error(
      `"${name}" (item ${config.itemId}) is ${payload.folder !== undefined ? 'a folder' : 'not a file'}. ` +
        'item_id must point at the workbook itself.',
    );
  }

  return {
    name,
    size: typeof payload.size === 'number' ? payload.size : 0,
    contentTag: payload.cTag ?? payload.eTag ?? null,
    webUrl: payload.webUrl ?? null,
    lastModified: payload.lastModifiedDateTime ?? null,
    downloadUrl: payload['@microsoft.graph.downloadUrl'] ?? null,
  };
}

/**
 * Returns the workbook's compressed `.xlsx` bytes, serving from the bounded LRU
 * cache when the credential fingerprint, drive/item ID, and `contentTag` match.
 */
export async function downloadWorkbook(
  config: IExcelConfig,
  item: IDriveItem,
  signal?: AbortSignal,
): Promise<Buffer> {
  if (signal?.aborted) {
    throw signal.reason ?? new Error('Request aborted');
  }

  if (item.size > MAX_FILE_BYTES) {
    throw new Error(
      `"${item.name}" is ${formatMegabytes(item.size)}, over the ` +
        `${formatMegabytes(MAX_FILE_BYTES)} this integration reads. A workbook ` +
        'that size is usually a data dump that belongs in a database.',
    );
  }

  if (item.contentTag === null) {
    return fetchBytes(config, item, signal);
  }

  const credHash = credentialFingerprint(config.credentials);
  const key = `${credHash}|${config.driveId}/${config.itemId}`;

  const cached = files.get(key);
  if (cached !== undefined && cached.contentTag === item.contentTag) {
    files.delete(key);
    files.set(key, cached);
    return cached.bytes;
  }

  const downloadKey = `${key}#${item.contentTag}`;
  let flight = downloads.get(downloadKey);
  if (flight === undefined) {
    const contentTag = item.contentTag;
    const controller = new AbortController();
    const promise = fetchBytes(config, item, controller.signal)
      .then((bytes) => {
        files.delete(key);
        files.set(key, { contentTag, bytes });
        while (files.size > FILE_CACHE_ENTRIES) {
          const oldest = files.keys().next().value;
          if (oldest === undefined) {
            break;
          }
          files.delete(oldest);
        }
        return bytes;
      })
      .finally(() => {
        downloads.delete(downloadKey);
      });
    flight = { promise, controller, waiters: 0 };
    downloads.set(downloadKey, flight);
  }

  return waitForDownload(flight, signal);
}

export function clearWorkbookCache(): void {
  files.clear();
}

export function getWorkbookCacheSize(): number {
  return files.size;
}

function waitForDownload(
  flight: IInFlightDownload,
  signal?: AbortSignal,
): Promise<Buffer> {
  if (signal === undefined) {
    flight.waiters += 1;
    return flight.promise.finally(() => {
      flight.waiters -= 1;
    });
  }

  flight.waiters += 1;
  return new Promise<Buffer>((resolve, reject) => {
    let settled = false;
    const onAbort = () => {
      if (settled) {
        return;
      }
      settled = true;
      flight.waiters -= 1;
      if (flight.waiters <= 0) {
        flight.controller.abort(signal.reason);
      }
      flight.promise.catch(() => undefined);
      reject(signal.reason ?? new Error('Request aborted'));
    };

    signal.addEventListener('abort', onAbort, { once: true });
    flight.promise.then(
      (value) => {
        if (settled) {
          return;
        }
        settled = true;
        flight.waiters -= 1;
        signal.removeEventListener('abort', onAbort);
        resolve(value);
      },
      (error: unknown) => {
        if (settled) {
          return;
        }
        settled = true;
        flight.waiters -= 1;
        signal.removeEventListener('abort', onAbort);
        reject(error);
      },
    );
  });
}

/**
 * Fetches workbook bytes from the pre-authenticated download URL (or `/content`
 * manual redirect), omitting the Graph `Authorization` header when calling the
 * external storage host and enforcing {@link MAX_FILE_BYTES} while streaming.
 */
async function fetchBytes(
  config: IExcelConfig,
  item: IDriveItem,
  signal?: AbortSignal,
): Promise<Buffer> {
  let rawUrl = item.downloadUrl;
  if (rawUrl === null) {
    const response = await request(
      config,
      `${itemPath(config)}/content`,
      'manual',
      MAX_FILE_BYTES,
      signal,
    );
    const location = response.headers.get('location');
    if (response.status >= 300 && response.status < 400 && location !== null) {
      rawUrl = location;
    } else if (response.ok) {
      return response.body;
    } else {
      throw new Error(
        `Microsoft Graph answered ${response.status} to the download of "${item.name}" without a location`,
      );
    }
  }

  const downloadUrl = validateHttpsUrl(rawUrl, 'Workbook download URL');
  const response = await fetchBoundedBytes(
    downloadUrl,
    { method: 'GET' },
    {
      timeoutMs: DOWNLOAD_TIMEOUT_MS,
      maxBytes: MAX_FILE_BYTES,
      timeoutMessage: `Downloading "${item.name}" did not finish within ${DOWNLOAD_TIMEOUT_MS} ms`,
      unreachablePrefix: `Cannot download workbook "${item.name}": `,
      sizeLabel: `Workbook "${item.name}"`,
      signal,
      secrets: [config.credentials.clientSecret],
    },
  );

  if (!response.ok) {
    throw new Error(
      `Downloading "${item.name}" failed with ${response.status}. The ` +
        'download link lives only minutes, so this is usually transient.',
    );
  }
  return response.body;
}

async function requestJson<T>(
  config: IExcelConfig,
  path: string,
  signal?: AbortSignal,
): Promise<T> {
  const response = await request(
    config,
    path,
    'follow',
    MAX_JSON_RESPONSE_BYTES,
    signal,
  );
  const text = response.body.toString('utf8');
  try {
    return JSON.parse(text) as T;
  } catch {
    throw new Error(
      `Microsoft Graph answered ${response.status} with a non-JSON body`,
    );
  }
}

async function request(
  config: IExcelConfig,
  path: string,
  redirect: 'follow' | 'manual',
  maxBytes: number,
  signal?: AbortSignal,
): Promise<IBoundedBytesResponse> {
  for (let attempt = 0; ; attempt++) {
    const token = await getAccessToken(config.credentials, signal);
    const secrets = [config.credentials.clientSecret, token.token];
    const response = await send(
      `${GRAPH_API_BASE}${path}`,
      token.token,
      redirect,
      maxBytes,
      secrets,
      signal,
    );

    if (response.status === 401 && attempt === 0) {
      invalidateAccessToken(config.credentials);
      continue;
    }
    if (response.ok || (response.status >= 300 && response.status < 400)) {
      return response;
    }
    throw new Error(describeApiFailure(response, config, token, secrets));
  }
}

function send(
  url: string,
  token: string,
  redirect: 'follow' | 'manual',
  maxBytes: number,
  secrets: readonly string[],
  signal?: AbortSignal,
): Promise<IBoundedBytesResponse> {
  return fetchBoundedBytes(
    url,
    {
      method: 'GET',
      headers: {
        authorization: `Bearer ${token}`,
        accept: 'application/json',
      },
      redirect,
    },
    {
      timeoutMs: REQUEST_TIMEOUT_MS,
      maxBytes,
      timeoutMessage: `Microsoft Graph did not answer within ${REQUEST_TIMEOUT_MS} ms`,
      unreachablePrefix: 'Cannot reach Microsoft Graph: ',
      sizeLabel: 'Microsoft Graph',
      signal,
      secrets,
    },
  );
}

function itemPath(config: IExcelConfig): string {
  return `/drives/${encodeURIComponent(config.driveId)}/items/${encodeURIComponent(config.itemId)}`;
}

function formatMegabytes(bytes: number): string {
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function describeApiFailure(
  response: IBoundedBytesResponse,
  config: IExcelConfig,
  token: IAccessToken,
  secrets: readonly string[],
): string {
  const { status } = response;
  const text = response.body.toString('utf8');
  let message = '';
  try {
    const parsed = JSON.parse(text) as {
      error?: { code?: unknown; message?: unknown };
    };
    const code =
      typeof parsed.error?.code === 'string'
        ? redactSecrets(parsed.error.code, secrets)
        : '';
    const detail =
      typeof parsed.error?.message === 'string'
        ? redactSecrets(parsed.error.message, secrets)
        : '';
    message = [code, detail].filter((part) => part.length > 0).join(': ');
  } catch {
    // Never include raw non-JSON upstream response bodies in thrown errors.
    message = '';
  }

  const roles = token.roles.length > 0 ? token.roles.join(', ') : 'none';
  const hasSelectedPermission = token.roles.some((role) =>
    SELECTED_SCOPES.has(role),
  );
  const grant =
    `POST /drives/${config.driveId}/items/${config.itemId}/permissions with ` +
    `{"roles":["read"],"grantedToV2":{"application":{"id":"${config.credentials.clientId}"}}}`;

  if (status === 403 || status === 404) {
    if (!hasSelectedPermission) {
      return (
        `Microsoft Graph refused the workbook (${status}), and the app's token carries ` +
        `no Files.SelectedOperations.Selected or Sites.Selected (its application permissions: ${roles}). ` +
        'Add that application permission to the app registration and grant admin consent. ' +
        message
      ).trimEnd();
    }
    return (
      `Microsoft Graph refused the workbook (${status}). Either drive_id/item_id ` +
      `do not point at an existing file, or app ${config.credentials.clientId} was ` +
      `never granted this file — an admin grants it with ${grant}. ${message}`
    ).trimEnd();
  }
  if (status === 401) {
    return (
      'Microsoft Graph refused the access token (401) twice, with a freshly ' +
      `issued token the second time. Application permissions on the token: ${roles}. ` +
      message
    ).trimEnd();
  }
  if (status === 429 || status === 503) {
    const retryAfter = response.headers.get('retry-after');
    return (
      `Microsoft Graph is throttling this app (${status})` +
      (retryAfter === null ? '' : `; retry after ${retryAfter} s`) +
      '. Raising the instance cache TTL is the fix if it keeps happening. ' +
      message
    ).trimEnd();
  }
  if (status === 400) {
    return `Microsoft Graph rejected the request (400) — check drive_id and item_id. ${message}`.trimEnd();
  }
  return `Microsoft Graph answered ${status}. ${message}`.trimEnd();
}
