const LOOPBACK_HOSTNAMES = new Set(['localhost', '127.0.0.1', '[::1]']);
const MAX_ERROR_DETAIL_LENGTH = 240;

export interface IBoundedBytesResponse {
  status: number;
  ok: boolean;
  headers: Headers;
  body: Buffer;
}

export interface IBoundedTextResponse {
  status: number;
  ok: boolean;
  headers: Headers;
  body: string;
}

/**
 * Validates that an endpoint or download URL uses `https:` (allowing `http:`
 * exclusively on loopback `localhost`, `127.0.0.1`, `[::1]`) and carries
 * neither userinfo credentials nor a URL fragment.
 */
export function validateHttpsUrl(rawUrl: string, label: string): string {
  const value = rawUrl.trim();
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new Error(`${label} is not a valid URL`);
  }
  if (url.protocol !== 'https:' && url.protocol !== 'http:') {
    throw new Error(`${label} must be an https URL`);
  }
  if (
    url.protocol === 'http:' &&
    !LOOPBACK_HOSTNAMES.has(url.hostname.toLowerCase())
  ) {
    throw new Error(
      `${label} must use https (plain http is only allowed for loopback hosts localhost, 127.0.0.1, [::1])`,
    );
  }
  if (
    url.username !== '' ||
    url.password !== '' ||
    hasAuthorityUserinfo(value, url.protocol)
  ) {
    throw new Error(`${label} must not contain userinfo credentials`);
  }
  if (url.hash !== '' || value.includes('#')) {
    throw new Error(`${label} must not contain a URL fragment`);
  }
  return url.toString();
}

export async function fetchBoundedText(
  url: string,
  init: Omit<RequestInit, 'signal'>,
  options: {
    timeoutMs: number;
    maxBytes: number;
    timeoutMessage: string;
    unreachablePrefix: string;
    sizeLabel: string;
    signal?: AbortSignal;
    secrets?: readonly string[];
  },
): Promise<IBoundedTextResponse> {
  const response = await fetchBoundedBytes(url, init, options);
  return {
    status: response.status,
    ok: response.ok,
    headers: response.headers,
    body: response.body.toString('utf8'),
  };
}

/**
 * Executes an HTTP request combining `AbortSignal.timeout(timeoutMs)` with the
 * optional caller `AbortSignal` via `AbortSignal.any`, and streams the response
 * body under a strict `maxBytes` byte cap.
 */
export async function fetchBoundedBytes(
  url: string,
  init: Omit<RequestInit, 'signal'>,
  options: {
    timeoutMs: number;
    maxBytes: number;
    timeoutMessage: string;
    unreachablePrefix: string;
    sizeLabel: string;
    signal?: AbortSignal;
    secrets?: readonly string[];
  },
): Promise<IBoundedBytesResponse> {
  const {
    timeoutMs,
    maxBytes,
    timeoutMessage,
    unreachablePrefix,
    sizeLabel,
    signal: callerSignal,
    secrets = [],
  } = options;

  if (callerSignal?.aborted) {
    throw callerSignal.reason ?? new Error('Request aborted');
  }

  const timeoutSignal = AbortSignal.timeout(timeoutMs);
  const combinedSignal = callerSignal
    ? AbortSignal.any([timeoutSignal, callerSignal])
    : timeoutSignal;

  try {
    const response = await raceSignal(
      fetch(url, { ...init, signal: combinedSignal }),
      combinedSignal,
    );
    const body = await raceSignal(
      readResponseBytesBounded(response, maxBytes, sizeLabel),
      combinedSignal,
    );
    return {
      status: response.status,
      ok: response.ok,
      headers: response.headers,
      body,
    };
  } catch (err) {
    if (callerSignal?.aborted) {
      throw callerSignal.reason ?? err;
    }
    if (timeoutSignal.aborted) {
      throw new Error(timeoutMessage);
    }
    if (isSizeLimitError(err, sizeLabel)) {
      throw err;
    }
    const detail = redactSecrets(
      err instanceof Error ? err.message : String(err),
      secrets,
    );
    throw new Error(`${unreachablePrefix}${detail}`);
  }
}

/**
 * Scrubs secret values, collapses control characters, and bounds diagnostic
 * text extracted from structured API error fields.
 */
export function redactSecrets(
  text: string,
  secrets: readonly string[],
): string {
  let sanitized = text.replace(/[\r\n\t]+/g, ' ').trim();
  for (const secret of secrets) {
    if (secret.length < 4) {
      continue;
    }
    if (sanitized.includes(secret)) {
      sanitized = sanitized.split(secret).join('[REDACTED]');
    }
    const encoded = encodeURIComponent(secret);
    if (encoded.length >= 4 && sanitized.includes(encoded)) {
      sanitized = sanitized.split(encoded).join('[REDACTED]');
    }
  }
  if (sanitized.length > MAX_ERROR_DETAIL_LENGTH) {
    sanitized = `${sanitized.slice(0, MAX_ERROR_DETAIL_LENGTH)}…`;
  }
  return sanitized;
}

function hasAuthorityUserinfo(rawUrl: string, protocol: string): boolean {
  const rest = rawUrl.slice(protocol.length + 2);
  const boundary = rest.search(/[/?#]/);
  const authority = boundary < 0 ? rest : rest.slice(0, boundary);
  return authority.includes('@');
}

function raceSignal<T>(work: Promise<T>, signal: AbortSignal): Promise<T> {
  if (signal.aborted) {
    work.catch(() => undefined);
    return Promise.reject(signal.reason ?? new Error('Request aborted'));
  }
  return new Promise<T>((resolve, reject) => {
    const onAbort = () => {
      work.catch(() => undefined);
      reject(signal.reason ?? new Error('Request aborted'));
    };
    signal.addEventListener('abort', onAbort, { once: true });
    work.then(
      (value) => {
        signal.removeEventListener('abort', onAbort);
        resolve(value);
      },
      (error: unknown) => {
        signal.removeEventListener('abort', onAbort);
        reject(error);
      },
    );
  });
}

async function readResponseBytesBounded(
  response: Response,
  maxBytes: number,
  label: string,
): Promise<Buffer> {
  const declaredLength = Number(response.headers.get('content-length'));
  if (Number.isFinite(declaredLength) && declaredLength > maxBytes) {
    await response.body?.cancel().catch(() => undefined);
    throw new Error(
      `${label} response exceeds the ${maxBytes}-byte size limit (Content-Length: ${declaredLength})`,
    );
  }

  if (response.body === null) {
    const buf = Buffer.from(await response.arrayBuffer());
    if (buf.byteLength > maxBytes) {
      throw new Error(
        `${label} response exceeds the ${maxBytes}-byte size limit`,
      );
    }
    return buf;
  }

  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let totalBytes = 0;
  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) {
        break;
      }
      totalBytes += value.byteLength;
      if (totalBytes > maxBytes) {
        await reader.cancel().catch(() => undefined);
        throw new Error(
          `${label} response exceeds the ${maxBytes}-byte size limit`,
        );
      }
      chunks.push(value);
    }
  } finally {
    reader.releaseLock();
  }
  return Buffer.concat(chunks, totalBytes);
}

function isSizeLimitError(err: unknown, label: string): boolean {
  return (
    err instanceof Error &&
    err.message.startsWith(`${label} response exceeds the `)
  );
}
