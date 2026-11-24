import { validateListUrl } from '../config';
import { DOWNLOAD_TIMEOUT_MS, MAX_DOWNLOAD_BYTES } from '../constants';

/**
 * Downloads a list file as UTF-8 text while enforcing {@link MAX_DOWNLOAD_BYTES}
 * on both `Content-Length` and the incoming byte stream so an oversized or
 * unbounded response cannot exhaust Core's heap.
 *
 * The request goes out from the Core process itself — no service sits
 * in between — so a site that firewalls Core's egress breaks this integration.
 * The error names the host so an administrator can see which address failed.
 */
export async function downloadText(
  url: string,
  maxBytes = MAX_DOWNLOAD_BYTES,
): Promise<string> {
  const validated = validateListUrl(url);
  const host = new URL(validated).host;
  let response: Response;
  try {
    response = await fetch(validated, {
      signal: AbortSignal.timeout(DOWNLOAD_TIMEOUT_MS),
      redirect: 'follow',
    });
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    throw new Error(
      `Could not download the sanctions list from ${host}: ${reason}. ` +
        'The Core host needs outbound HTTPS to that address.',
    );
  }
  if (response.url.length > 0) {
    validateListUrl(response.url, `Redirect target from ${host}`);
  }
  if (!response.ok) {
    await response.body?.cancel().catch(() => undefined);
    throw new Error(
      `Downloading the sanctions list from ${host} answered HTTP ${response.status}.`,
    );
  }
  return readBodyBounded(response, host, maxBytes);
}

async function readBodyBounded(
  response: Response,
  host: string,
  maxBytes: number,
): Promise<string> {
  const declaredLength = Number(response.headers.get('content-length'));
  if (Number.isFinite(declaredLength) && declaredLength > maxBytes) {
    await response.body?.cancel().catch(() => undefined);
    throw new Error(
      `Sanctions list from ${host} exceeds the ${maxBytes}-byte size limit (Content-Length: ${declaredLength}).`,
    );
  }
  if (response.body === null) {
    const text = await response.text();
    if (Buffer.byteLength(text, 'utf8') > maxBytes) {
      throw new Error(
        `Sanctions list from ${host} exceeds the ${maxBytes}-byte size limit.`,
      );
    }
    return text;
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
          `Sanctions list from ${host} exceeds the ${maxBytes}-byte size limit.`,
        );
      }
      chunks.push(value);
    }
  } finally {
    reader.releaseLock();
  }
  return Buffer.concat(chunks, totalBytes).toString('utf8');
}
