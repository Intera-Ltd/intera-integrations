import {
  LIST_MAX_STALE_MS,
  LIST_REFRESH_INTERVAL_MS,
  LIST_RETRY_INTERVAL_MS,
  MAX_LIST_CACHE_ENTRIES,
} from '../constants';
import type { SanctionsRecord } from '../types';

/** A parsed list, as every data source of this package reads it. */
export interface ISanctionsList {
  rows: SanctionsRecord[];
  /** Distinct listed subjects, for the connection test's one-line summary. */
  entityCount: number;
  /** When the publisher generated the file — not when it was downloaded. */
  generatedAt: string | null;
}

interface ICacheEntry {
  list: ISanctionsList | null;
  fetchedAt: number;
  /** Earliest time another download may be attempted after a failure. */
  retryAfter: number;
  /** Stored failure reason reported during backoff when no usable copy exists. */
  lastErrorMessage: string | null;
  inFlight: Promise<ISanctionsList> | null;
}

/**
 * Holds each downloaded list in memory for a day.
 *
 * Keyed by the download URL, so an instance pointing at an overridden address
 * never reads another instance's copy. Module-level on purpose: Core loads a
 * plugin once per process, and every instance and every widget of the process
 * then shares one download — a dashboard opening six widgets on a cold cache
 * waits for one request, through {@link ICacheEntry.inFlight}.
 *
 * Nothing is persisted. A restart costs one download (~25 MB, a few seconds
 * for the EU file), which is cheaper than owning a file on the Core host.
 */
const entries = new Map<string, ICacheEntry>();

export async function getList(
  url: string,
  load: (url: string) => Promise<ISanctionsList>,
): Promise<ISanctionsList> {
  let entry = entries.get(url);
  if (entry === undefined) {
    pruneEntries();
    entry = {
      list: null,
      fetchedAt: 0,
      retryAfter: 0,
      lastErrorMessage: null,
      inFlight: null,
    };
    entries.set(url, entry);
  }

  const now = Date.now();
  if (entry.list !== null && now - entry.fetchedAt < LIST_REFRESH_INTERVAL_MS) {
    return entry.list;
  }
  if (entry.inFlight !== null) {
    if (entry.list !== null && now - entry.fetchedAt <= LIST_MAX_STALE_MS) {
      return entry.list;
    }
    return entry.inFlight;
  }
  if (now < entry.retryAfter) {
    if (entry.list !== null && now - entry.fetchedAt <= LIST_MAX_STALE_MS) {
      return entry.list;
    }
    const retryInSec = Math.max(1, Math.ceil((entry.retryAfter - now) / 1_000));
    const reason = entry.lastErrorMessage ?? 'Sanctions list download failed';
    throw new Error(
      `${reason} (retry backoff active; next attempt in ${retryInSec}s)`,
    );
  }

  const current = entry;
  const refresh = load(url).then(
    (list) => {
      current.list = list;
      current.fetchedAt = Date.now();
      current.retryAfter = 0;
      current.lastErrorMessage = null;
      current.inFlight = null;
      return list;
    },
    (error: unknown) => {
      current.inFlight = null;
      current.retryAfter = Date.now() + LIST_RETRY_INTERVAL_MS;
      current.lastErrorMessage =
        error instanceof Error ? error.message : String(error);
      throw error;
    },
  );
  current.inFlight = refresh;

  if (current.list === null) {
    return refresh;
  }

  // A copy exists but is due for a refresh. Wait for the new one; if the
  // download fails, keep serving the old copy only while within
  // LIST_MAX_STALE_MS rather than answering from a week-old sanctions list.
  const stale = current.list;
  try {
    return await refresh;
  } catch (error) {
    if (Date.now() - current.fetchedAt > LIST_MAX_STALE_MS) {
      throw error;
    }
    const reason = error instanceof Error ? error.message : String(error);
    console.warn(
      `[sanctions] refreshing ${new URL(url).host} failed, serving the copy ` +
        `downloaded ${new Date(current.fetchedAt).toISOString()}: ${reason}`,
    );
    return stale;
  }
}

export function clearListCache(): void {
  entries.clear();
}

function pruneEntries(): void {
  if (entries.size < MAX_LIST_CACHE_ENTRIES) {
    return;
  }
  for (const [key, entry] of entries) {
    if (entry.inFlight === null && entry.list === null) {
      entries.delete(key);
      if (entries.size < MAX_LIST_CACHE_ENTRIES) {
        return;
      }
    }
  }
  for (const [key, entry] of entries) {
    if (entry.inFlight === null) {
      entries.delete(key);
      if (entries.size < MAX_LIST_CACHE_ENTRIES) {
        return;
      }
    }
  }
}
