import { MAX_RESPONSE_CACHE_BYTES, MAX_RESPONSE_CACHE_ENTRIES } from '../constants';

/**
 * Answers fetched from Asana, kept in the Core process for a short while.
 *
 * One entry is a whole listing (every page of one path and query), keyed by a hash of the token plus the request, so
 * two instances with different tokens never share an answer. The promise is stored before it settles, which makes
 * concurrent callers share one fetch (single flight) — a dashboard opening six widgets over one project reads it
 * once. A failed fetch is dropped at once, so the next caller retries instead of inheriting the error.
 *
 * Bounded by both entry count ({@link MAX_RESPONSE_CACHE_ENTRIES}) and estimated UTF-8/JSON byte footprint
 * ({@link MAX_RESPONSE_CACHE_BYTES}), with expired entries swept eagerly on writes and oldest entries evicted first.
 * Nothing is persisted: a Core restart costs one read per listing.
 */

export interface IResponseCacheStats {
  entries: number;
  bytes: number;
}

export interface IResponseCacheOptions {
  maxEntries?: number;
  maxBytes?: number;
}

export interface IResponseCache {
  cached<T>(key: string, ttlMs: number, load: () => Promise<T>): Promise<T>;
  stats(): IResponseCacheStats;
  clear(): void;
}

interface ICacheEntry {
  expiresAt: number;
  value: Promise<unknown>;
  bytes: number;
}

const defaultCache = createResponseCache();

export function cached<T>(key: string, ttlMs: number, load: () => Promise<T>): Promise<T> {
  return defaultCache.cached(key, ttlMs, load);
}

export function getResponseCacheStats(): IResponseCacheStats {
  return defaultCache.stats();
}

export function clearResponseCache(): void {
  defaultCache.clear();
}

export function createResponseCache(options: IResponseCacheOptions = {}): IResponseCache {
  const maxEntries = options.maxEntries ?? MAX_RESPONSE_CACHE_ENTRIES;
  const maxBytes = options.maxBytes ?? MAX_RESPONSE_CACHE_BYTES;
  const entries = new Map<string, ICacheEntry>();
  let totalBytes = 0;

  const removeEntry = (key: string, entry: ICacheEntry): void => {
    if (entries.get(key) === entry) {
      entries.delete(key);
      totalBytes -= entry.bytes;
    }
  };

  const sweepExpired = (now: number): void => {
    for (const [key, entry] of entries) {
      if (entry.expiresAt <= now) {
        entries.delete(key);
        totalBytes -= entry.bytes;
      }
    }
  };

  const evictOverflow = (): void => {
    while (entries.size > maxEntries || totalBytes > maxBytes) {
      const oldestKey = entries.keys().next().value;
      if (oldestKey === undefined) {
        break;
      }
      const oldest = entries.get(oldestKey);
      entries.delete(oldestKey);
      if (oldest !== undefined) {
        totalBytes -= oldest.bytes;
      }
    }
  };

  return {
    cached<T>(key: string, ttlMs: number, load: () => Promise<T>): Promise<T> {
      const now = Date.now();
      const hit = entries.get(key);
      if (hit !== undefined) {
        if (hit.expiresAt > now) {
          return hit.value as Promise<T>;
        }
        entries.delete(key);
        totalBytes -= hit.bytes;
      }

      sweepExpired(now);

      const keyBytes = Buffer.byteLength(key, 'utf8');
      if (maxEntries <= 0 || keyBytes > maxBytes) {
        return load();
      }

      const value = load();
      const entry: ICacheEntry = {
        expiresAt: now + ttlMs,
        value,
        bytes: keyBytes,
      };
      entries.set(key, entry);
      totalBytes += keyBytes;
      evictOverflow();

      value.then(
        (resolved) => {
          if (entries.get(key) !== entry) {
            return;
          }
          const settledAt = Date.now();
          if (entry.expiresAt <= settledAt) {
            removeEntry(key, entry);
            return;
          }
          const fullBytes = keyBytes + estimateBytes(resolved);
          if (fullBytes > maxBytes) {
            removeEntry(key, entry);
            return;
          }
          entries.delete(key);
          totalBytes += fullBytes - entry.bytes;
          entry.bytes = fullBytes;
          entries.set(key, entry);
          sweepExpired(settledAt);
          evictOverflow();
        },
        () => {
          removeEntry(key, entry);
        },
      );

      return value;
    },

    stats(): IResponseCacheStats {
      return { entries: entries.size, bytes: totalBytes };
    },

    clear(): void {
      entries.clear();
      totalBytes = 0;
    },
  };
}

/**
 * Estimates the UTF-8/JSON heap footprint of a parsed response value without
 * serializing it to a temporary string or overflowing on deep structures.
 */
export function estimateBytes(root: unknown): number {
  let bytes = 0;
  const seen = new Set<object>();
  const stack: unknown[] = [root];

  while (stack.length > 0) {
    const current = stack.pop();
    if (current === null || current === undefined) {
      bytes += 4;
      continue;
    }
    switch (typeof current) {
      case 'string':
        bytes += Buffer.byteLength(current, 'utf8');
        break;
      case 'number':
        bytes += 8;
        break;
      case 'boolean':
        bytes += 4;
        break;
      case 'object': {
        if (seen.has(current)) {
          break;
        }
        seen.add(current);
        if (Array.isArray(current)) {
          bytes += 16 + current.length * 8;
          for (let i = 0; i < current.length; i += 1) {
            stack.push(current[i]);
          }
        } else if (Buffer.isBuffer(current) || ArrayBuffer.isView(current)) {
          bytes += current.byteLength;
        } else {
          const record = current as Record<string, unknown>;
          const keys = Object.keys(record);
          bytes += 32;
          for (let i = 0; i < keys.length; i += 1) {
            const key = keys[i];
            bytes += Buffer.byteLength(key, 'utf8') + 8;
            stack.push(record[key]);
          }
        }
        break;
      }
      default:
        bytes += 8;
        break;
    }
  }
  return bytes;
}
