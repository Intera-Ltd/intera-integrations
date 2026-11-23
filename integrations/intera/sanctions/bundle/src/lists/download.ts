import { DOWNLOAD_TIMEOUT_MS } from '../constants';

/**
 * Downloads a list file as text.
 *
 * The request goes out from the Core process itself — no service sits
 * in between — so a site that firewalls Core's egress breaks this integration.
 * The error names the host so an administrator can see which address failed.
 */
export async function downloadText(url: string): Promise<string> {
  const host = safeHost(url);
  let response: Response;
  try {
    response = await fetch(url, {
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
  if (!response.ok) {
    throw new Error(
      `Downloading the sanctions list from ${host} answered HTTP ${response.status}.`,
    );
  }
  return response.text();
}

function safeHost(url: string): string {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    throw new Error(`"${url}" is not a valid URL`);
  }
  if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') {
    throw new Error(`"${url}" must use http or https`);
  }
  return parsed.host;
}
