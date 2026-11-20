import {
  API_KEY_CONFIG_KEY,
  BASE_URL_CONFIG_KEY,
  REQUEST_TIMEOUT_MS,
} from './constants';

export interface IApiTarget {
  baseUrl: string;
  apiKey: string;
}

/** Reads the external API address and key from the connection config. */
export function resolveTarget(
  config: Record<string, unknown> | undefined,
): IApiTarget {
  const rawUrl = config?.[BASE_URL_CONFIG_KEY];
  const apiKey = config?.[API_KEY_CONFIG_KEY];
  if (typeof rawUrl !== 'string' || rawUrl.trim() === '') {
    throw new Error(`${BASE_URL_CONFIG_KEY} is not configured`);
  }
  if (typeof apiKey !== 'string' || apiKey === '') {
    throw new Error(`${API_KEY_CONFIG_KEY} is not configured`);
  }
  let url: URL;
  try {
    url = new URL(rawUrl.trim());
  } catch {
    throw new Error(`${BASE_URL_CONFIG_KEY} is not a valid URL`);
  }
  if (url.protocol !== 'https:' && url.protocol !== 'http:') {
    throw new Error(`${BASE_URL_CONFIG_KEY} must be an http or https URL`);
  }
  return { baseUrl: url.toString().replace(/\/+$/, ''), apiKey };
}

function requestSignal(signal: AbortSignal | undefined): AbortSignal {
  const timeout = AbortSignal.timeout(REQUEST_TIMEOUT_MS);
  return signal === undefined ? timeout : AbortSignal.any([signal, timeout]);
}

/** GET `<baseUrl><path>` with the API key; any non-2xx status is an error. */
export async function getJson(
  target: IApiTarget,
  path: string,
  signal?: AbortSignal,
): Promise<unknown> {
  const response = await fetch(`${target.baseUrl}${path}`, {
    method: 'GET',
    headers: {
      accept: 'application/json',
      authorization: `Bearer ${target.apiKey}`,
    },
    signal: requestSignal(signal),
  });
  if (!response.ok) {
    // Never include the key or the response body in errors: they reach logs.
    throw new Error(`API answered ${response.status} on GET ${path}`);
  }
  return response.json();
}
