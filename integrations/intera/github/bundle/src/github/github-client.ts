import { createHash, sign } from 'node:crypto';
import type { IAppCredential, IGitHubConfig } from '../config';
import {
  CACHE_TTL_MS,
  GITHUB_API_VERSION,
  INSTALLATION_TOKEN_REFRESH_MARGIN_MS,
  MAX_CONDITIONAL_ENTRIES,
  MAX_PARALLEL_REQUESTS,
  MAX_RECORDS_PER_LIST,
  MAX_RETRIES,
  MAX_RETRY_AFTER_SEC,
  PAGE_SIZE,
  REQUEST_TIMEOUT_MS,
} from '../constants';
import { cached } from './response-cache';

export type GitHubObject = Record<string, unknown>;
export type GitHubQuery = Record<string, string | number | boolean | undefined>;

/**
 * An answer GitHub gave, or failed to give, carrying its HTTP status. `status` 0 means GitHub was never reached.
 *
 * The message is written for the admin who reads it in a failed widget: it says what to fix on our side (the token,
 * its permissions, the app's installation) before GitHub's own words. `upstream` keeps GitHub's message alone, which
 * is what a source reads to tell "this feature is off for the repository" from "the token may not read it".
 */
export class GitHubApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly upstream = '',
  ) {
    super(message);
    this.name = 'GitHubApiError';
  }
}

/** GitHub's error body: `{"message": "...", "documentation_url": "...", "errors": [...]}`. */
function readErrorText(body: unknown): string {
  const message = (body as { message?: unknown } | null)?.message;
  return typeof message === 'string' ? message : '';
}

/**
 * GitHub's names of the permissions in `x-accepted-github-permissions`, as the token and app settings pages label
 * them — the words an admin has to find there.
 */
const PERMISSION_LABELS: Record<string, string> = {
  actions: 'Actions',
  contents: 'Contents',
  deployments: 'Deployments',
  issues: 'Issues',
  metadata: 'Metadata',
  pull_requests: 'Pull requests',
  secret_scanning_alerts: 'Secret scanning alerts',
  security_events: 'Code scanning alerts',
  vulnerability_alerts: 'Dependabot alerts',
};

/** `security_events=read; …` → `Code scanning alerts: read`, or undefined when the header is absent. */
function acceptedPermissions(header: string | null): string | undefined {
  if (header === null || header.trim().length === 0) {
    return undefined;
  }
  const alternatives = header.split(';').map((entry) =>
    entry
      .split(',')
      .map((pair) => {
        const [name, level] = pair.trim().split('=');
        return `${PERMISSION_LABELS[name] ?? name}: ${level ?? 'read'}`;
      })
      .join(' + '),
  );
  return alternatives.join(' or ');
}

function describeError(
  status: number,
  path: string,
  upstream: string,
  credential: IGitHubConfig['credential'],
  permission?: string,
): string {
  const detail = upstream.length > 0 ? `: ${upstream}` : '';
  switch (status) {
    case 401:
      return credential.kind === 'token'
        ? `GitHub rejected the access token (401)${detail}. A personal access token stops working when it expires or ` +
            'is revoked — put a new one in the instance.'
        : `GitHub rejected the app's credentials (401)${detail}. Check the app id and that the private key is the ` +
            "app's current one.";
    case 403:
      return (
        `GitHub: the ${credential.kind === 'token' ? "token's" : "app's"} access does not cover ${path} (403)${detail}. ` +
        (permission === undefined
          ? 'A fine-grained token or an app needs the read permission the source names in its description.'
          : `It needs the repository permission ${permission} — add it to the ` +
            `${credential.kind === 'token' ? 'token' : "app (and accept the new permission on its installation)"}.`)
      );
    case 404:
      return `GitHub: ${path} was not found (404)${detail}`;
    case 422:
      return `GitHub refused the request for ${path} (422)${detail}`;
    default:
      return `GitHub answered ${status} for ${path}${detail}`;
  }
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** A counting semaphore: at most `limit` requests of one credential in flight, shared by every read in this process. */
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

function gateFor(credentialHash: string): Gate {
  let gate = gates.get(credentialHash);
  if (gate === undefined) {
    gate = new Gate(MAX_PARALLEL_REQUESTS);
    gates.set(credentialHash, gate);
  }
  return gate;
}

/**
 * Validators of pages already read, for conditional requests. Keyed by the credential and the URL, so one
 * instance's validator is never offered with another's token. Insertion order is age: a hit moves an entry to the
 * end, and past {@link MAX_CONDITIONAL_ENTRIES} the oldest is dropped.
 */
interface IConditionalEntry {
  etag: string;
  body: unknown;
  next: string | undefined;
}

const conditionalEntries = new Map<string, IConditionalEntry>();

function rememberConditional(key: string, entry: IConditionalEntry): void {
  conditionalEntries.delete(key);
  conditionalEntries.set(key, entry);
  if (conditionalEntries.size > MAX_CONDITIONAL_ENTRIES) {
    const oldest = conditionalEntries.keys().next().value;
    if (oldest !== undefined) conditionalEntries.delete(oldest);
  }
}

/** The `rel="next"` target of a `Link` header, absolute as GitHub sends it. */
function nextLink(header: string | null): string | undefined {
  if (header === null) {
    return undefined;
  }
  for (const part of header.split(',')) {
    const match = /<([^>]+)>\s*;\s*rel="next"/.exec(part);
    if (match !== null) {
      return match[1];
    }
  }
  return undefined;
}

function base64Url(value: string | Buffer): string {
  return Buffer.from(value).toString('base64').replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
}

/**
 * The JWT a GitHub App signs to speak as itself: RS256, issued a minute in the past (GitHub's advice against clock
 * drift) and valid nine minutes, under GitHub's ten-minute ceiling.
 */
function appJwt(credential: IAppCredential): string {
  const now = Math.floor(Date.now() / 1_000);
  const header = base64Url(JSON.stringify({ alg: 'RS256', typ: 'JWT' }));
  const payload = base64Url(JSON.stringify({ iat: now - 60, exp: now + 540, iss: credential.appId }));
  const signingInput = `${header}.${payload}`;
  let signature: Buffer;
  try {
    signature = sign('sha256', Buffer.from(signingInput), credential.privateKeyPem);
  } catch (error) {
    throw new Error(
      `The GitHub App's private key could not sign: ${error instanceof Error ? error.message : String(error)}`,
    );
  }
  return `${signingInput}.${base64Url(signature)}`;
}

interface IInstallationToken {
  token: string;
  expiresAt: number;
}

/** Installation tokens per app credential, minted once and shared (single flight) until near their expiry. */
const installationTokens = new Map<string, Promise<IInstallationToken>>();

/** What one answer said about the primary rate limit, for the test-connection line. */
export interface IRateLimitState {
  limit?: number;
  remaining?: number;
}

/**
 * The GitHub REST API for one instance's credential.
 *
 * Every listing goes through {@link cached}: fetched whole (all pages) once per {@link CACHE_TTL_MS} per credential
 * and request, however many widgets ask. Beneath that, every page is a conditional request when it was read
 * before, and an unchanged page answers `304`, which GitHub does not count against the hourly limit. Reads take no
 * caller signal on purpose — a listing is shared by every concurrent caller, and one caller's deadline must not
 * cancel it for the others (see `awaitWithSignal`).
 */
export class GitHubClient {
  private readonly credentialHash: string;
  private readonly gate: Gate;
  rateLimit: IRateLimitState = {};

  constructor(private readonly config: IGitHubConfig) {
    const { credential } = config;
    const secret =
      credential.kind === 'token'
        ? `token|${credential.token}`
        : `app|${credential.appId}|${credential.installationId ?? ''}|${config.owner.toLowerCase()}|${credential.privateKeyPem}`;
    this.credentialHash = createHash('sha256').update(`${config.apiUrl}|${secret}`).digest('hex');
    this.gate = gateFor(this.credentialHash);
  }

  /** One response body (an object or an array), cached. */
  get(path: string, query: GitHubQuery = {}, ttlMs = CACHE_TTL_MS): Promise<unknown> {
    return cached(this.cacheKey('get', path, query), ttlMs, async () => (await this.request(this.url(path, query))).body);
  }

  /**
   * Every record of a paginated listing, cached. Pages follow the `Link` header (GitHub uses page numbers on some
   * endpoints and cursors on others; the header hides which). `itemsKey` names the array inside an object-shaped
   * page (`workflow_runs`, `artifacts`, …); a page that is an array is the records itself.
   *
   * `until` ends the paging early once a page's last record reaches it — the listings this is used on are newest
   * first, so a page that ends older than the read's lower bound is the last one needed. Records past the bound on
   * that page are kept; the evaluator applies the filter. Its `key` (the bound) is part of the cache key, so a read
   * with an earlier bound never gets a listing cut for a later one.
   */
  list(
    path: string,
    query: GitHubQuery = {},
    options: { itemsKey?: string; ttlMs?: number; until?: { key: string; reached: (record: GitHubObject) => boolean } } = {},
  ): Promise<GitHubObject[]> {
    const ttlMs = options.ttlMs ?? CACHE_TTL_MS;
    const kind = `list:${options.itemsKey ?? ''}:${options.until?.key ?? ''}`;
    return cached(this.cacheKey(kind, path, query), ttlMs, async () => {
      const records: GitHubObject[] = [];
      let url: string | undefined = this.url(path, { ...query, per_page: PAGE_SIZE });
      while (url !== undefined) {
        const page = await this.request(url);
        const items =
          options.itemsKey === undefined ? page.body : (page.body as GitHubObject | null)?.[options.itemsKey];
        const pageRecords = Array.isArray(items) ? (items as GitHubObject[]) : [];
        records.push(...pageRecords);
        if (records.length > MAX_RECORDS_PER_LIST) {
          throw new GitHubApiError(
            400,
            `GitHub: ${path} lists more than ${MAX_RECORDS_PER_LIST} records — narrow the read (fewer days, ` +
              'fewer repositories)',
          );
        }
        const last = pageRecords[pageRecords.length - 1];
        if (options.until !== undefined && last !== undefined && options.until.reached(last)) {
          break;
        }
        url = page.next;
      }
      return records;
    });
  }

  private url(path: string, query: GitHubQuery): string {
    const url = new URL(`${this.config.apiUrl}${path}`);
    for (const [name, value] of Object.entries(query)) {
      if (value !== undefined) {
        url.searchParams.set(name, String(value));
      }
    }
    return url.toString();
  }

  private cacheKey(kind: string, path: string, query: GitHubQuery): string {
    const sorted = Object.keys(query)
      .filter((name) => query[name] !== undefined)
      .sort()
      .map((name) => `${name}=${String(query[name])}`)
      .join('&');
    return `${this.credentialHash}|${kind}|${path}?${sorted}`;
  }

  private pathOf(url: string): string {
    return url.startsWith(this.config.apiUrl) ? url.slice(this.config.apiUrl.length).split('?')[0] : url;
  }

  // ---- authentication ----

  private async authorization(): Promise<string> {
    const { credential } = this.config;
    if (credential.kind === 'token') {
      return `Bearer ${credential.token}`;
    }
    const now = Date.now();
    let pending = installationTokens.get(this.credentialHash);
    if (pending !== undefined) {
      const settled = await pending.catch(() => undefined);
      if (settled !== undefined && settled.expiresAt - INSTALLATION_TOKEN_REFRESH_MARGIN_MS > now) {
        return `Bearer ${settled.token}`;
      }
      if (installationTokens.get(this.credentialHash) === pending) {
        installationTokens.delete(this.credentialHash);
      }
    }
    pending = installationTokens.get(this.credentialHash);
    if (pending === undefined) {
      pending = this.mintInstallationToken(credential);
      installationTokens.set(this.credentialHash, pending);
      const minted = pending;
      minted.catch(() => {
        if (installationTokens.get(this.credentialHash) === minted) installationTokens.delete(this.credentialHash);
      });
    }
    return `Bearer ${(await pending).token}`;
  }

  private invalidateInstallationToken(): void {
    installationTokens.delete(this.credentialHash);
  }

  /**
   * Trades the app's JWT for an installation token. The installation is the configured one, else the app's
   * installation on the instance's owner — looked up as an organization first, then as a user account.
   */
  private async mintInstallationToken(credential: IAppCredential): Promise<IInstallationToken> {
    const jwt = appJwt(credential);
    let installationId = credential.installationId;
    if (installationId === undefined) {
      const owner = encodeURIComponent(this.config.owner);
      const asOrganization = await this.appRequest(`/orgs/${owner}/installation`, 'GET', jwt);
      const installation =
        asOrganization.status === 404 ? await this.appRequest(`/users/${owner}/installation`, 'GET', jwt) : asOrganization;
      if (installation.status === 404) {
        throw new GitHubApiError(
          404,
          `GitHub: the app is not installed on ${this.config.owner} — install it there (the app's page → Install ` +
            'App), or name the installation id in the instance',
        );
      }
      if (installation.status !== 200) {
        throw new GitHubApiError(
          installation.status,
          describeError(installation.status, '/orgs/{owner}/installation', readErrorText(installation.body), credential),
        );
      }
      installationId = String((installation.body as GitHubObject).id);
    }

    const answer = await this.appRequest(
      `/app/installations/${encodeURIComponent(installationId)}/access_tokens`,
      'POST',
      jwt,
    );
    if (answer.status !== 201) {
      throw new GitHubApiError(
        answer.status,
        describeError(answer.status, `/app/installations/${installationId}/access_tokens`, readErrorText(answer.body), credential),
      );
    }
    const body = answer.body as { token?: unknown; expires_at?: unknown };
    if (typeof body.token !== 'string') {
      throw new GitHubApiError(502, 'GitHub answered an installation token request without a token');
    }
    const expiresAt = typeof body.expires_at === 'string' ? Date.parse(body.expires_at) : Number.NaN;
    return { token: body.token, expiresAt: Number.isNaN(expiresAt) ? Date.now() + 3_600_000 : expiresAt };
  }

  private async appRequest(path: string, method: 'GET' | 'POST', jwt: string): Promise<{ status: number; body: unknown }> {
    let response: Response;
    try {
      response = await fetch(`${this.config.apiUrl}${path}`, {
        method,
        headers: {
          Authorization: `Bearer ${jwt}`,
          Accept: 'application/vnd.github+json',
          'X-GitHub-Api-Version': GITHUB_API_VERSION,
          'User-Agent': 'intera-github-integration',
        },
        signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
      });
    } catch (error) {
      throw new GitHubApiError(
        0,
        `GitHub is unreachable (${path}): ${error instanceof Error ? error.message : String(error)}`,
      );
    }
    let body: unknown = null;
    try {
      body = await response.json();
    } catch {
      body = null;
    }
    return { status: response.status, body };
  }

  // ---- requests ----

  /**
   * One page, with GitHub's limits waited out. A 429, or a 403 that GitHub marks as a rate limit
   * (`x-ratelimit-remaining: 0`, or a secondary limit's `retry-after`), waits as long as GitHub says when that is
   * under {@link MAX_RETRY_AFTER_SEC}, and fails with the reset time otherwise. A 500/502/503/504 or an unreachable
   * host gets a short backoff. An app's 401 mints a new installation token once.
   */
  private async request(url: string): Promise<{ body: unknown; next: string | undefined }> {
    const conditionalKey = `${this.credentialHash}|${url}`;
    let refreshedToken = false;
    for (let attempt = 0; ; attempt++) {
      const known = conditionalEntries.get(conditionalKey);
      const authorization = await this.authorization();
      const outcome = await this.gate.run(() => this.send(url, authorization, known?.etag));
      if ('error' in outcome) {
        if (attempt < 2) {
          await delay(1_000 * 2 ** attempt);
          continue;
        }
        throw new GitHubApiError(0, `GitHub is unreachable (${this.pathOf(url)}): ${outcome.error}`);
      }

      const { response } = outcome;
      this.noteRateLimit(response);
      if (response.status === 304 && known !== undefined) {
        await response.text().catch(() => '');
        rememberConditional(conditionalKey, known);
        return { body: known.body, next: known.next };
      }
      if (response.ok) {
        const body = await response.json();
        const next = nextLink(response.headers.get('link'));
        const etag = response.headers.get('etag');
        if (etag !== null) {
          rememberConditional(conditionalKey, { etag, body, next });
        }
        return { body, next };
      }

      let body: unknown = null;
      try {
        body = await response.json();
      } catch {
        body = null;
      }
      const upstream = readErrorText(body);

      if (response.status === 401 && this.config.credential.kind === 'app' && !refreshedToken) {
        refreshedToken = true;
        this.invalidateInstallationToken();
        continue;
      }

      const wait = this.rateLimitWaitMs(response, upstream, attempt);
      if (wait !== undefined) {
        if (wait <= MAX_RETRY_AFTER_SEC * 1_000 && attempt < MAX_RETRIES) {
          await delay(wait);
          continue;
        }
        throw new GitHubApiError(response.status, this.describeRateLimit(response, url), upstream);
      }

      const transient = response.status === 500 || response.status === 502 || response.status === 503 || response.status === 504;
      if (transient && attempt < MAX_RETRIES) {
        await delay(1_000 * 2 ** attempt + Math.floor(Math.random() * 500));
        continue;
      }

      throw new GitHubApiError(
        response.status,
        describeError(
          response.status,
          this.pathOf(url),
          upstream,
          this.config.credential,
          acceptedPermissions(response.headers.get('x-accepted-github-permissions')),
        ),
        upstream,
      );
    }
  }

  /** How long to wait before retrying a rate-limited answer; undefined when the answer is not a rate limit. */
  private rateLimitWaitMs(response: Response, upstream: string, attempt: number): number | undefined {
    if (response.status !== 403 && response.status !== 429) {
      return undefined;
    }
    const retryAfter = Number(response.headers.get('retry-after'));
    const remaining = response.headers.get('x-ratelimit-remaining');
    const jitter = Math.floor(Math.random() * 500);
    if (Number.isFinite(retryAfter) && retryAfter > 0) {
      return retryAfter * 1_000 + jitter;
    }
    if (remaining === '0') {
      const reset = Number(response.headers.get('x-ratelimit-reset'));
      if (Number.isFinite(reset) && reset > 0) {
        return Math.max(reset * 1_000 - Date.now(), 0) + 1_000 + jitter;
      }
    }
    if (response.status === 429 || /rate limit/i.test(upstream)) {
      // A secondary limit without a header: GitHub's advice is to wait at least a minute.
      return 2 ** attempt * 15_000 + jitter;
    }
    return undefined;
  }

  private describeRateLimit(response: Response, url: string): string {
    const reset = Number(response.headers.get('x-ratelimit-reset'));
    const at = Number.isFinite(reset) && reset > 0 ? ` until ${new Date(reset * 1_000).toISOString()}` : '';
    return (
      `GitHub's rate limit is spent${at} (${this.pathOf(url)}). Fewer repositories or a longer cache helps; a ` +
      'GitHub App installation has a higher limit than a personal token.'
    );
  }

  private noteRateLimit(response: Response): void {
    const limit = Number(response.headers.get('x-ratelimit-limit'));
    const remaining = Number(response.headers.get('x-ratelimit-remaining'));
    if (Number.isFinite(limit) && response.headers.has('x-ratelimit-limit')) this.rateLimit.limit = limit;
    if (Number.isFinite(remaining) && response.headers.has('x-ratelimit-remaining')) this.rateLimit.remaining = remaining;
  }

  private async send(
    url: string,
    authorization: string,
    etag: string | undefined,
  ): Promise<{ response: Response } | { error: string }> {
    const headers: Record<string, string> = {
      Authorization: authorization,
      Accept: 'application/vnd.github+json',
      'X-GitHub-Api-Version': GITHUB_API_VERSION,
      'User-Agent': 'intera-github-integration',
    };
    if (etag !== undefined) {
      headers['If-None-Match'] = etag;
    }
    try {
      const response = await fetch(url, { headers, signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS) });
      return { response };
    } catch (error) {
      return { error: error instanceof Error ? error.message : String(error) };
    }
  }
}
