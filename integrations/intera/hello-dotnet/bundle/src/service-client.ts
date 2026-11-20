import { REQUEST_TIMEOUT_MS, SERVICE_URL_CONFIG_KEY } from './constants';

/**
 * Reads the service address from the connection config. Core fills
 * `intera_service_url` for integrations installed with a Docker deployment;
 * the bundle never hardcodes an address.
 */
export function resolveServiceUrl(
  config: Record<string, unknown> | undefined,
): string {
  const raw = config?.[SERVICE_URL_CONFIG_KEY];
  if (typeof raw !== 'string' || raw.trim() === '') {
    throw new Error(
      `The service address is not configured (${SERVICE_URL_CONFIG_KEY}). Install the integration from Marketplace so that its container is started.`,
    );
  }
  let url: URL;
  try {
    url = new URL(raw.trim());
  } catch {
    throw new Error(`${SERVICE_URL_CONFIG_KEY} is not a valid URL`);
  }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw new Error(`${SERVICE_URL_CONFIG_KEY} must be an http or https URL`);
  }
  return url.toString().replace(/\/+$/, '');
}

/** Combines Core's cancellation signal with the bundle's own timeout. */
function requestSignal(signal: AbortSignal | undefined): AbortSignal {
  const timeout = AbortSignal.timeout(REQUEST_TIMEOUT_MS);
  return signal === undefined ? timeout : AbortSignal.any([signal, timeout]);
}

/** GET `<baseUrl><path>` and parse JSON; any non-2xx status is an error. */
export async function getJson(
  baseUrl: string,
  path: string,
  signal?: AbortSignal,
): Promise<unknown> {
  const response = await fetch(`${baseUrl}${path}`, {
    method: 'GET',
    headers: { accept: 'application/json' },
    signal: requestSignal(signal),
  });
  if (!response.ok) {
    throw new Error(`Service answered ${response.status} on GET ${path}`);
  }
  return response.json();
}
