export const INTEGRATION_ID = 'intera-sanctions-integration';
export const INTEGRATION_NAME = 'SanctionsCheck';
export const INTEGRATION_VERSION = '1.2.0';

export const DIRECT_API_KIND = 'readonly_api' as const;
export const DATA_SOURCE_CAPABILITY = 'datasource';
export const DEFAULT_CACHE_TTL_SEC = 300;

/**
 * Optional override of the EU list's download address. Empty means
 * {@link EU_FSF_XML_URL}. It exists because the address carries a token the
 * Commission issued in 2017 and could one day replace — an instance edit is
 * then enough, rather than a plugin release.
 */
export const EU_LIST_URL_CONFIG_FIELD = 'eu_list_url';

/**
 * The EU Financial Sanctions Database's full export, XML schema 1.1. The token
 * is the public one the FSF page itself links to (base64 of "token-2017"); no
 * account is involved. The CSV export at the same place carries the same data
 * denormalized into one line per name × address × birth date × document, which
 * is why the XML is read instead.
 */
export const EU_FSF_XML_URL =
  'https://webgate.ec.europa.eu/fsd/fsf/public/files/xmlFullSanctionsList_1_1/content?token=dG9rZW4tMjAxNw';

export const EU_SANCTIONS_SOURCE_ID = 'eu_sanctions_list';
export const EU_SANCTIONS_SOURCE_NAME = 'EU sanctions list';
export const EU_SANCTIONS_SOURCE_DESCRIPTION =
  'EU Consolidated List of Financial Sanctions (European Commission, DG FISMA), ' +
  'one row per name variant of a listed person or organisation. Search by ' +
  '`name` or `name_normalized`; count subjects with countDistinct(entity_id). ' +
  'Downloaded once a day — `list_generated_at` says how current the list is.';

/**
 * How long a downloaded list is served before it is fetched again. The spec
 * asks for a daily refresh; the Commission publishes whenever an act changes,
 * with no schedule to align to.
 */
export const LIST_REFRESH_INTERVAL_MS = 24 * 60 * 60 * 1000;

/**
 * After a failed refresh the previous copy keeps being served, and the next
 * attempt waits this long — so an outage on the Commission's side costs one
 * request per quarter hour, not one per dashboard widget.
 */
export const LIST_RETRY_INTERVAL_MS = 15 * 60 * 1000;

/**
 * A copy older than this is no longer served when a refresh fails: the query
 * fails instead. A sanctions check answered from a list a week out of date
 * looks exactly like a current one, which is the one outcome worse than an
 * error.
 */
export const LIST_MAX_STALE_MS = 7 * 24 * 60 * 60 * 1000;

/** The EU file is ~25 MB and arrives in a few seconds; this is the ceiling. */
export const DOWNLOAD_TIMEOUT_MS = 120_000;

/** Separator of multi-valued text columns (birth dates, citizenships, …). */
export const MULTI_VALUE_SEPARATOR = '; ';
