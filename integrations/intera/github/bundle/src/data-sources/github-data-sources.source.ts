import { readGitHubConfig } from '../config';
import { GitHubReader } from '../github/reader';
import type { IDataSource, IDataSourceContext, IDataSourcePage, IDataSourceParams } from '../types';
import { applyOrder, buildEvaluationSchema, evaluateExpressions, matchesFilter } from './query-evaluator';
import { analyzeQuery } from './query-shape';
import type { IGitHubSourceDefinition } from './source-definition';
import {
  actionsArtifactsSource,
  deploymentsSource,
  workflowRunsSource,
  workflowsSource,
} from './sources/actions-sources';
import { commitsSource, issuesSource, pullRequestsSource } from './sources/code-sources';
import { repositoriesSource } from './sources/repository-sources';
import {
  codeScanningAlertsSource,
  dependabotAlertsSource,
  secretScanningAlertsSource,
} from './sources/security-sources';

/** Ordered from repositories to CI/CD runs, deployments, code activity, and security alerts. */
export const GITHUB_SOURCES: IGitHubSourceDefinition[] = [
  repositoriesSource,
  workflowRunsSource,
  workflowsSource,
  deploymentsSource,
  commitsSource,
  pullRequestsSource,
  issuesSource,
  dependabotAlertsSource,
  codeScanningAlertsSource,
  secretScanningAlertsSource,
  actionsArtifactsSource,
];

/**
 * Waits for a read, or stops waiting when Core's deadline fires.
 *
 * The read itself is not cancelled: it is shared through the response cache with every concurrent caller, and
 * cancelling it for one would fail it for all. It runs to its own bounds (a timeout per request, a capped number of
 * retries) and leaves its answer in the cache, so the next scheduled run of a slow metric finds it there instead of
 * timing out again. Core is released at the deadline either way.
 */
function awaitWithSignal<T>(work: Promise<T>, signal: AbortSignal | undefined): Promise<T> {
  if (signal === undefined) {
    return work;
  }
  if (signal.aborted) {
    work.catch(() => undefined);
    return Promise.reject(signal.reason ?? new Error('Aborted'));
  }
  return new Promise<T>((resolve, reject) => {
    const onAbort = () => {
      work.catch(() => undefined);
      reject(signal.reason ?? new Error('Aborted'));
    };
    signal.addEventListener('abort', onAbort, { once: true });
    work.then(
      (value) => {
        signal.removeEventListener('abort', onAbort);
        resolve(value);
      },
      (error: unknown) => {
        signal.removeEventListener('abort', onAbort);
        reject(error);
      },
    );
  });
}

function createDataSource(definition: IGitHubSourceDefinition): IDataSource {
  const evaluationSchema = buildEvaluationSchema(definition.fields);

  return {
    id: definition.id,
    name: definition.name,
    description: definition.description,
    fields: definition.fields,

    async getData(params: IDataSourceParams, context: IDataSourceContext): Promise<unknown[] | IDataSourcePage> {
      const reader = new GitHubReader(readGitHubConfig(context));
      const rows = await awaitWithSignal(definition.load(reader, analyzeQuery(params)), context.signal);

      // `filter` and `filters` are never both set (Core rejects that upstream); a chain is an AND across its
      // entries. The load narrowed what it could; this applies the whole filter to what came back.
      let filtered = rows;
      if (params.filter) {
        filtered = filtered.filter((row) => matchesFilter(row, params.filter, evaluationSchema));
      }
      if (Array.isArray(params.filters)) {
        for (const filter of params.filters) {
          filtered = filtered.filter((row) => matchesFilter(row, filter, evaluationSchema));
        }
      }

      const shaped =
        Array.isArray(params.expressions) && params.expressions.length > 0
          ? evaluateExpressions(
              filtered,
              params.expressions,
              evaluationSchema,
              params.orderBy,
              Array.isArray(params.groupBy) ? params.groupBy : undefined,
            )
          : applyOrder(filtered, params.orderBy, evaluationSchema);

      // A page (rather than a bare array) tells Core that offset/limit were applied here and `total` is the whole
      // filtered set.
      if (typeof params.offset === 'number' && params.offset >= 0) {
        const limit = typeof params.limit === 'number' && params.limit > 0 ? params.limit : undefined;
        const start = params.offset;
        const end = limit === undefined ? undefined : start + limit;
        return { rows: shaped.slice(start, end), total: shaped.length };
      }

      if (typeof params.limit === 'number' && params.limit > 0) {
        return shaped.slice(0, params.limit);
      }
      return shaped;
    },
  };
}

export const githubDataSources: IDataSource[] = GITHUB_SOURCES.map(createDataSource);
