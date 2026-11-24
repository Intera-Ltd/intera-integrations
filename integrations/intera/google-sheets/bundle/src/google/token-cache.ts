import { MAX_TOKEN_CACHE_ENTRIES, TOKEN_REFRESH_SKEW_SEC } from '../constants';
import type { IServiceAccount } from '../types';
import {
  credentialFingerprint,
  requestAccessToken,
  type IAccessToken,
} from './service-account';

/**
 * Bounded LRU access-token cache scoped by SHA-256 credential fingerprint.
 *
 * Instances using the same service account share one token and one in-flight
 * exchange; instances with distinct credentials never share cached tokens.
 */
interface ICacheEntry {
  token: IAccessToken;
}

interface IInFlightToken {
  promise: Promise<IAccessToken>;
  controller: AbortController;
  waiters: number;
}

const tokens = new Map<string, ICacheEntry>();
const inFlight = new Map<string, IInFlightToken>();

export async function getAccessToken(
  account: IServiceAccount,
  signal?: AbortSignal,
): Promise<string> {
  if (signal?.aborted) {
    throw signal.reason ?? new Error('Request aborted');
  }

  const key = credentialFingerprint(account);

  const cached = tokens.get(key);
  if (cached !== undefined) {
    if (!isNearExpiry(cached.token)) {
      tokens.delete(key);
      tokens.set(key, cached);
      return cached.token.token;
    }
    tokens.delete(key);
  }

  let flight = inFlight.get(key);
  if (flight === undefined) {
    const controller = new AbortController();
    const promise = requestAccessToken(account, controller.signal)
      .then((token) => {
        storeToken(key, token);
        return token;
      })
      .finally(() => {
        inFlight.delete(key);
      });
    flight = { promise, controller, waiters: 0 };
    inFlight.set(key, flight);
  }

  const token = await waitForFlight(flight, signal);
  return token.token;
}

export function invalidateAccessToken(account: IServiceAccount): void {
  tokens.delete(credentialFingerprint(account));
}

export function clearTokenCache(): void {
  tokens.clear();
}

export function getTokenCacheSize(): number {
  return tokens.size;
}

function waitForFlight(
  flight: IInFlightToken,
  signal?: AbortSignal,
): Promise<IAccessToken> {
  if (signal === undefined) {
    flight.waiters += 1;
    return flight.promise.finally(() => {
      flight.waiters -= 1;
    });
  }

  flight.waiters += 1;
  return new Promise<IAccessToken>((resolve, reject) => {
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

function storeToken(key: string, token: IAccessToken): void {
  sweepExpiredTokens();
  tokens.delete(key);
  tokens.set(key, { token });
  while (tokens.size > MAX_TOKEN_CACHE_ENTRIES) {
    const oldestKey = tokens.keys().next().value;
    if (oldestKey === undefined) {
      break;
    }
    tokens.delete(oldestKey);
  }
}

function sweepExpiredTokens(): void {
  for (const [key, entry] of tokens) {
    if (isNearExpiry(entry.token)) {
      tokens.delete(key);
    }
  }
}

function isNearExpiry(token: IAccessToken): boolean {
  return token.expiresAt - Date.now() <= TOKEN_REFRESH_SKEW_SEC * 1000;
}
