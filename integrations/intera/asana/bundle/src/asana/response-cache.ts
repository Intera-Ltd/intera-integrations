/**
 * Answers fetched from Asana, kept in the Core process for a short while.
 *
 * One entry is a whole listing (every page of one path and query), keyed by a hash of the token plus the request, so
 * two instances with different tokens never share an answer. The promise is stored before it settles, which makes
 * concurrent callers share one fetch (single flight) — a dashboard opening six widgets over one project reads it
 * once. A failed fetch is dropped at once, so the next caller retries instead of inheriting the error.
 *
 * Nothing is persisted: a Core restart costs one read per listing.
 */

interface ICacheEntry {
  expiresAt: number;
  value: Promise<unknown>;
}

/** Above this many entries, expired ones are swept on the next write. */
const SWEEP_THRESHOLD = 1_000;

const entries = new Map<string, ICacheEntry>();

function sweep(now: number): void {
  for (const [key, entry] of entries) {
    if (entry.expiresAt <= now) {
      entries.delete(key);
    }
  }
}

export function cached<T>(key: string, ttlMs: number, load: () => Promise<T>): Promise<T> {
  const now = Date.now();
  const hit = entries.get(key);
  if (hit !== undefined && hit.expiresAt > now) {
    return hit.value as Promise<T>;
  }

  if (entries.size >= SWEEP_THRESHOLD) {
    sweep(now);
  }

  const value = load();
  const entry: ICacheEntry = { expiresAt: now + ttlMs, value };
  entries.set(key, entry);
  value.catch(() => {
    // Only this entry: a newer one may already have replaced it.
    if (entries.get(key) === entry) {
      entries.delete(key);
    }
  });
  return value;
}
