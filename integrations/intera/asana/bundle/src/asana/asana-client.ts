import { createHash } from 'node:crypto';
import {
  ASANA_API_BASE_URL,
  CACHE_TTL_MS,
  MAX_PARALLEL_REQUESTS,
  MAX_RECORDS_PER_LIST,
  MAX_RETRIES,
  MAX_RETRY_AFTER_SEC,
  PAGE_SIZE,
  REQUEST_TIMEOUT_MS,
} from '../constants';
import { cached } from './response-cache';

export type AsanaObject = Record<string, unknown>;
export type AsanaQuery = Record<string, string | number | boolean | undefined>;

/**
 * An answer Asana gave, or failed to give, carrying its HTTP status. `status` 0 means Asana was never reached.
 *
 * The message is written for the admin who reads it in a failed widget: it says what to fix on our side (the token,
 * the plan, the user's access) before Asana's own words, which name the field but not the remedy.
 */
export class AsanaApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = 'AsanaApiError';
  }
}

/** Asana's error body: `{"errors":[{"message","help","phrase"}]}`. */
function readErrorText(body: unknown): string {
  const errors = (body as { errors?: Array<{ message?: unknown }> } | null)?.errors;
  if (!Array.isArray(errors)) {
    return '';
  }
  return errors
    .map((error) => (typeof error?.message === 'string' ? error.message : ''))
    .filter((message) => message.length > 0)
    .join('; ');
}

function describeError(status: number, path: string, upstream: string): string {
  const detail = upstream.length > 0 ? `: ${upstream}` : '';
  switch (status) {
    case 401:
      return (
        `Asana rejected the access token (401)${detail}. A personal access token stops working when it is ` +
        'revoked or its owner is deactivated — put a new one in the instance.'
      );
    case 402:
      return `Asana: ${path} needs a paid Asana plan or add-on (402)${detail}`;
    case 403:
      return `Asana: the token's user may not read ${path} (403)${detail}`;
    case 404:
      return `Asana: ${path} was not found (404)${detail}`;
    case 429:
      return `Asana kept answering "too many requests" (429) for ${path}${detail}. Try again in a minute.`;
    default:
      return `Asana answered ${status} for ${path}${detail}`;
  }
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** A counting semaphore: at most `limit` requests of one token in flight, shared by every read in this process. */
class Gate {
  private active = 0;
  private readonly waiting: Array<() => void> = [];

  constructor(private readonly limit: number) {}

  async run<T>(task: () => Promise<T>): Promise<T> {
    if (this.active >= this.limit) {
      await new Promise<void>((resolve) => this.waiting.push(resolve));
    } else {
      this.active++;
    }
    try {
      return await task();
    } finally {
      const next = this.waiting.shift();
      if (next !== undefined) {
        // The slot passes straight to the next waiter; `active` stays as it is.
        next();
      } else {
        this.active--;
      }
    }
  }
}

const gates = new Map<string, Gate>();

function gateFor(tokenHash: string): Gate {
  let gate = gates.get(tokenHash);
  if (gate === undefined) {
    gate = new Gate(MAX_PARALLEL_REQUESTS);
    gates.set(tokenHash, gate);
  }
  return gate;
}

function buildUrl(path: string, query: AsanaQuery): string {
  const url = new URL(`${ASANA_API_BASE_URL}${path}`);
  for (const [name, value] of Object.entries(query)) {
    if (value !== undefined) {
      url.searchParams.set(name, String(value));
    }
  }
  return url.toString();
}

/**
 * The Asana REST API for one token.
 *
 * Every read goes through {@link cached}: a listing is fetched whole (all pages) once per {@link CACHE_TTL_MS} per
 * token and request, however many widgets ask. Reads take no caller signal on purpose — a listing is shared by every
 * concurrent caller, and one caller's deadline must not cancel it for the others. The caller stops *waiting* instead
 * (see `awaitWithSignal`), and the read itself is bounded by {@link REQUEST_TIMEOUT_MS} per request and the retry cap.
 */
export class AsanaClient {
  private readonly tokenHash: string;
  private readonly gate: Gate;

  constructor(private readonly token: string) {
    this.tokenHash = createHash('sha256').update(token).digest('hex');
    this.gate = gateFor(this.tokenHash);
  }

  /** One object (`data` of a non-list endpoint), cached. */
  get(path: string, query: AsanaQuery = {}, ttlMs = CACHE_TTL_MS): Promise<AsanaObject> {
    return cached(this.cacheKey('get', path, query), ttlMs, async () => {
      const body = await this.request(path, query);
      return (body.data ?? {}) as AsanaObject;
    });
  }

  /**
   * Every record of a paginated listing, cached. Pages are read one after another — each page's `offset` token comes
   * from the previous one — and the listing fails rather than truncates past {@link MAX_RECORDS_PER_LIST}.
   */
  list(path: string, query: AsanaQuery = {}, ttlMs = CACHE_TTL_MS): Promise<AsanaObject[]> {
    return cached(this.cacheKey('list', path, query), ttlMs, async () => {
      const records: AsanaObject[] = [];
      let offset: string | undefined;
      do {
        const body = await this.request(path, { ...query, limit: PAGE_SIZE, offset });
        if (Array.isArray(body.data)) {
          records.push(...(body.data as AsanaObject[]));
        }
        if (records.length > MAX_RECORDS_PER_LIST) {
          throw new AsanaApiError(400, `Asana: ${path} lists more than ${MAX_RECORDS_PER_LIST} records`);
        }
        const nextPage = body.next_page as { offset?: unknown } | null | undefined;
        offset = typeof nextPage?.offset === 'string' ? nextPage.offset : undefined;
      } while (offset !== undefined);
      return records;
    });
  }

  private cacheKey(kind: string, path: string, query: AsanaQuery): string {
    const sorted = Object.keys(query)
      .filter((name) => query[name] !== undefined)
      .sort()
      .map((name) => `${name}=${String(query[name])}`)
      .join('&');
    return `${this.tokenHash}|${kind}|${path}?${sorted}`;
  }

  /**
   * One request, with Asana's limits waited out: a 429 (and a 503, which Asana also sends under load) for its
   * `Retry-After`, a 500/502/504 or an unreachable host with a short backoff. Asana counts rejected requests against
   * the quota too, so the number of attempts is capped rather than retried until it works.
   */
  private async request(path: string, query: AsanaQuery): Promise<AsanaObject> {
    const url = buildUrl(path, query);
    for (let attempt = 0; ; attempt++) {
      const outcome = await this.gate.run(() => this.send(url));
      if ('error' in outcome) {
        if (attempt < 2) {
          await delay(1_000 * 2 ** attempt);
          continue;
        }
        throw new AsanaApiError(0, `Asana is unreachable (${path}): ${outcome.error}`);
      }

      const { response } = outcome;
      if (response.ok) {
        return (await response.json()) as AsanaObject;
      }

      const retryable =
        response.status === 429 ||
        response.status === 503 ||
        response.status === 500 ||
        response.status === 502 ||
        response.status === 504;
      if (retryable && attempt < MAX_RETRIES) {
        // Drain the body so the connection is released for the next attempt.
        await response.text().catch(() => '');
        await delay(this.retryDelayMs(response, attempt));
        continue;
      }

      let body: unknown = null;
      try {
        body = await response.json();
      } catch {
        body = null;
      }
      throw new AsanaApiError(response.status, describeError(response.status, path, readErrorText(body)));
    }
  }

  private retryDelayMs(response: Response, attempt: number): number {
    const header = Number(response.headers.get('retry-after'));
    const seconds = Number.isFinite(header) && header > 0 ? header : 2 ** (attempt + 1);
    // Jitter, so the eight requests that hit the limit together do not come back together.
    return Math.min(seconds, MAX_RETRY_AFTER_SEC) * 1_000 + Math.floor(Math.random() * 500);
  }

  private async send(url: string): Promise<{ response: Response } | { error: string }> {
    try {
      const response = await fetch(url, {
        headers: { Authorization: `Bearer ${this.token}`, Accept: 'application/json' },
        signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
      });
      return { response };
    } catch (error) {
      return { error: error instanceof Error ? error.message : String(error) };
    }
  }
}
