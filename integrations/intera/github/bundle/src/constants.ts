export const INTEGRATION_ID = 'intera-github-integration';
export const INTEGRATION_NAME = 'GitHub';
export const INTEGRATION_VERSION = '3.2.0';

export const DIRECT_API_KIND = 'direct_api' as const;
export const DATA_SOURCE_CAPABILITY = 'datasource';
// GitHub meters a token per hour (5 000 requests for a user, 5 000–15 000 for an app installation), and every
// source but one is a call per repository. Five minutes of host cache keeps a wall of widgets inside that;
// repository activity does not need to be fresher on a dashboard.
export const DEFAULT_CACHE_TTL_SEC = 300;

export const ACCESS_TOKEN_CONFIG_FIELD = 'accessToken';
export const APP_ID_CONFIG_FIELD = 'appId';
export const APP_PRIVATE_KEY_CONFIG_FIELD = 'appPrivateKey';
export const INSTALLATION_ID_CONFIG_FIELD = 'installationId';
export const OWNER_CONFIG_FIELD = 'owner';
export const REPOSITORIES_CONFIG_FIELD = 'repositories';
export const INCLUDE_ARCHIVED_CONFIG_FIELD = 'includeArchivedRepositories';
export const HISTORY_DAYS_CONFIG_FIELD = 'historyDays';
export const API_URL_CONFIG_FIELD = 'apiUrl';

export const GITHUB_API_BASE_URL = 'https://api.github.com';
/** The REST API version every request pins (`X-GitHub-Api-Version`), so a new default cannot move a column. */
export const GITHUB_API_VERSION = '2022-11-28';

/** Prefix of every data source id. */
export const SOURCE_ID_PREFIX = 'github_';

/** GitHub's own page-size ceiling (`per_page` is at most 100 on every paginated endpoint). */
export const PAGE_SIZE = 100;

/**
 * Requests in flight per credential. GitHub's secondary limits allow 100 concurrent requests and advise against
 * bursts; a read is a call per repository, and four keeps a cold dashboard over a hundred repositories well inside
 * both.
 */
export const MAX_PARALLEL_REQUESTS = 4;

/** One HTTP request, not one read: a read is many requests. */
export const REQUEST_TIMEOUT_MS = 60_000;

/**
 * A 429, or a 403 that is a rate limit, is waited out for its `Retry-After` or until `x-ratelimit-reset`, at most
 * this long a wait and at most {@link MAX_RETRIES} times. A primary limit that resets later than that fails the read
 * with the time it resets, rather than holding Core's request open for most of an hour.
 */
export const MAX_RETRY_AFTER_SEC = 60;
export const MAX_RETRIES = 4;

/**
 * How long a listing fetched from GitHub is reused. Keyed by the credential, the path and the query, and shared
 * between concurrent callers (single flight), so six widgets over one repository cost one read. The repository list
 * and the owner change rarely and are kept longer.
 */
export const CACHE_TTL_MS = 120_000;
export const REFERENCE_CACHE_TTL_MS = 900_000;

/**
 * Validators (`ETag`) and bodies kept for conditional requests. A `304 Not Modified` to an authorized request does
 * not count against GitHub's primary rate limit, so a page that did not change since the last read costs nothing;
 * this many pages are remembered per process, the oldest dropped first.
 */
export const MAX_CONDITIONAL_ENTRIES = 5_000;

/** Records one listing may produce. Past it a read fails, never truncates. */
export const MAX_RECORDS_PER_LIST = 50_000;

/** Records one read may produce over all its repositories. */
export const MAX_RECORDS_PER_READ = 100_000;

/**
 * Days of history the event-like sources read when the filter gives no lower bound: workflow runs, commits,
 * deployments, artifacts, and the closed issues and pull requests (open ones are always read whole). INTERA keeps
 * its own history as metric samples, so an integration answers the current state; thirty days
 * is the default retention window, and a filter with its own lower bound replaces it.
 */
export const DEFAULT_HISTORY_DAYS = 30;

/** Pull requests whose reviews one `github_pull_requests` read may fetch, one call each, when `reviewed_by` is shown. */
export const MAX_REVIEW_FETCHES = 500;

/** Deployments whose latest status one `github_deployments` read may fetch, one call each. */
export const MAX_DEPLOYMENT_STATUS_FETCHES = 1_000;

/**
 * An installation token lives an hour; it is replaced this long before it expires, so a read that starts with it
 * never ends with a 401.
 */
export const INSTALLATION_TOKEN_REFRESH_MARGIN_MS = 300_000;

/** Separator of multi-valued text columns (labels, topics, assignees, …). */
export const MULTI_VALUE_SEPARATOR = ', ';
