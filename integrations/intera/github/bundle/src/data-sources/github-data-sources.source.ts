import {
  analyzeQuery,
  awaitWithSignal,
  buildEvaluationSchema,
  executeInMemoryQuery,
} from '@intera-integrations/shared';
import { readGitHubConfig } from '../config';
import { GitHubReader } from '../github/reader';
import type { IDataSource, IDataSourceContext, IDataSourcePage, IDataSourceParams } from '../types';
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

function createDataSource(definition: IGitHubSourceDefinition): IDataSource {
  const evaluationSchema = buildEvaluationSchema(definition.fields);

  return {
    id: definition.id,
    name: definition.name,
    description: definition.description,
    fields: definition.fields,

    async getData(params: IDataSourceParams, context: IDataSourceContext): Promise<unknown[] | IDataSourcePage> {
      const reader = new GitHubReader(readGitHubConfig(context));
      const rows = await awaitWithSignal(
        definition.load(reader, analyzeQuery(params, params.weekStart)),
        context.signal,
      );
      return executeInMemoryQuery(rows, params, evaluationSchema);
    },
  };
}

export const githubDataSources: IDataSource[] = GITHUB_SOURCES.map(createDataSource);
