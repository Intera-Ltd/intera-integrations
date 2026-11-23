export const INTEGRATION_ID = 'intera-asana-integration';
export const INTEGRATION_NAME = 'Asana';
export const INTEGRATION_VERSION = '3.2.0';

export const DIRECT_API_KIND = 'direct_api' as const;
export const DATA_SOURCE_CAPABILITY = 'datasource';
// Asana meters every token per minute (150 requests on a free plan, 1 500 on a paid one), and a task read is one
// listing per project plus, for subtasks, stories and status updates, a fan-out. Five minutes of host cache keeps a
// wall of widgets inside that; project data does not need to be fresher on a dashboard.
export const DEFAULT_CACHE_TTL_SEC = 300;

export const ACCESS_TOKEN_CONFIG_FIELD = 'accessToken';
export const WORKSPACE_CONFIG_FIELD = 'workspace';
export const PROJECTS_CONFIG_FIELD = 'projects';
export const INCLUDE_ARCHIVED_CONFIG_FIELD = 'includeArchivedProjects';
export const INCLUDE_SUBTASKS_CONFIG_FIELD = 'includeSubtasks';

export const ASANA_API_BASE_URL = 'https://app.asana.com/api/1.0';

/** Prefix of every data source id. */
export const SOURCE_ID_PREFIX = 'asana_';

/** Asana's own page-size ceiling (`limit` is 1–100 on every paginated endpoint). */
export const PAGE_SIZE = 100;

/**
 * Requests in flight per token. Asana allows 50 concurrent GETs per token, shared with whatever else uses it; eight
 * keeps a cold dashboard well inside that and inside the per-minute quota's bursts.
 */
export const MAX_PARALLEL_REQUESTS = 8;

/** One HTTP request, not one read: a read is many requests. */
export const REQUEST_TIMEOUT_MS = 60_000;

/**
 * A 429 is waited out for its `Retry-After` (Asana: seconds, "often under 60"), at most this long a wait and at most
 * {@link MAX_RETRIES} times; then the read fails rather than holding Core's request open indefinitely.
 */
export const MAX_RETRY_AFTER_SEC = 60;
export const MAX_RETRIES = 4;

/**
 * How long a listing fetched from Asana is reused. Keyed by the token, the path and the query, and shared between
 * concurrent callers (single flight), so six widgets over one project cost one read. Reference data (workspaces,
 * users, teams, custom field definitions) changes rarely and is kept longer.
 */
export const CACHE_TTL_MS = 120_000;
export const REFERENCE_CACHE_TTL_MS = 900_000;

/**
 * Tasks one read may produce. Past it a read fails, never truncates — a count over a silently cut list looks exactly
 * like a right answer.
 */
export const MAX_TASKS_PER_READ = 20_000;

/** Records one listing may produce (users of a large organization, the custom fields of a workspace). */
export const MAX_RECORDS_PER_LIST = 200_000;

/**
 * Parent tasks whose subtasks one read may fetch, one call each. Past it the read fails and names the two ways out:
 * narrow it to projects, or turn subtasks off for the instance.
 */
export const MAX_SUBTASK_PARENTS = 2_000;

/** Asana nests subtasks at most five levels deep. */
export const MAX_SUBTASK_DEPTH = 5;

/** Tasks whose stories one `asana_task_activity` read may fetch, one call each. */
export const MAX_ACTIVITY_TASKS = 500;

/** Task ids a filter may name for them to be read one by one rather than through their projects. */
export const MAX_TASKS_BY_ID = 100;

/**
 * Lower bound sent to the time-tracking listing when the filter gives none: the workspace-level endpoint refuses a
 * read without a date bound, and this is "no bound" in the only form it accepts.
 */
export const UNBOUNDED_START_DATE = '1970-01-01';

/** Separator of multi-valued text columns (tags, email domains, enum options, …). */
export const MULTI_VALUE_SEPARATOR = ', ';
