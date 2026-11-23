import {
  LIST_MAX_STALE_MS,
  LIST_REFRESH_INTERVAL_MS,
  LIST_RETRY_INTERVAL_MS,
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
  loadedAt: number;
  /** Earliest time another download may be attempted after a failure. */
  retryAfter: number;
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
    entry = { list: null, loadedAt: 0, retryAfter: 0, inFlight: null };
    entries.set(url, entry);
  }

  const now = Date.now();
  if (entry.list !== null && now - entry.loadedAt < LIST_REFRESH_INTERVAL_MS) {
    return entry.list;
  }
  if (entry.inFlight !== null) {
    return entry.list ?? entry.inFlight;
  }
  if (entry.list !== null && now < entry.retryAfter) {
    return entry.list;
  }

  const current = entry;
  const refresh = load(url).then(
    (list) => {
      current.list = list;
      current.loadedAt = Date.now();
      current.retryAfter = 0;
      current.inFlight = null;
      return list;
    },
    (error: unknown) => {
      current.inFlight = null;
      current.retryAfter = Date.now() + LIST_RETRY_INTERVAL_MS;
      throw error;
    },
  );
  current.inFlight = refresh;

  if (current.list === null) {
    return refresh;
  }

  // A copy exists but is due for a refresh. Wait for the new one; if the
  // download fails, keep serving the old copy for a while rather than blanking
  // every dashboard over a publisher outage — the rows carry
  // `list_generated_at`, so the age of the answer stays visible.
  const stale = current.list;
  try {
    return await refresh;
  } catch (error) {
    if (Date.now() - current.loadedAt > LIST_MAX_STALE_MS) {
      throw error;
    }
    const reason = error instanceof Error ? error.message : String(error);
    console.warn(
      `[sanctions] refreshing ${new URL(url).host} failed, serving the copy ` +
        `downloaded ${new Date(current.loadedAt).toISOString()}: ${reason}`,
    );
    return stale;
  }
}
