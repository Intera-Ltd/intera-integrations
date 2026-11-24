import { MAX_TOKEN_CACHE_ENTRIES, TOKEN_REFRESH_SKEW_SEC } from '../constants';
import type { IAppCredentials } from '../types';
import {
  credentialFingerprint,
  requestAccessToken,
  type IAccessToken,
} from './client-credentials';

/**
 * Bounded LRU Entra ID access-token cache scoped by SHA-256 credential hash.
 *
 * Instances sharing one app registration share a single token and in-flight
 * sign-in; instances with distinct credentials never share cached tokens.
 */
interface IInFlightToken {
  promise: Promise<IAccessToken>;
  controller: AbortController;
  waiters: number;
}

const tokens = new Map<string, IAccessToken>();
const inFlight = new Map<string, IInFlightToken>();

export async function getAccessToken(
  credentials: IAppCredentials,
  signal?: AbortSignal,
): Promise<IAccessToken> {
  if (signal?.aborted) {
    throw signal.reason ?? new Error('Request aborted');
  }

  const key = credentialFingerprint(credentials);

  const cached = tokens.get(key);
  if (cached !== undefined) {
    if (!isNearExpiry(cached)) {
      tokens.delete(key);
      tokens.set(key, cached);
      return cached;
    }
    tokens.delete(key);
  }

  let flight = inFlight.get(key);
  if (flight === undefined) {
    const controller = new AbortController();
    const promise = requestAccessToken(credentials, controller.signal)
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

  return waitForFlight(flight, signal);
}

export function invalidateAccessToken(credentials: IAppCredentials): void {
  tokens.delete(credentialFingerprint(credentials));
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
  tokens.set(key, token);
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
    if (isNearExpiry(entry)) {
      tokens.delete(key);
    }
  }
}

function isNearExpiry(token: IAccessToken): boolean {
  return token.expiresAt - Date.now() <= TOKEN_REFRESH_SKEW_SEC * 1000;
}
