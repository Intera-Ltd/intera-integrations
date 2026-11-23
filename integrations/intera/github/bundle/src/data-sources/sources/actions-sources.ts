import { MAX_DEPLOYMENT_STATUS_FETCHES } from '../../constants';
import type { GitHubApiError, GitHubObject } from '../../github/github-client';
import { mapLimited } from '../../github/map-limited';
import { repoPath } from '../../github/reader';
import type { GitHubRecord } from '../../types';
import {
  LOOKUP,
  before,
  bool,
  date,
  featureUnavailable,
  flag,
  loginOf,
  number,
  num,
  obj,
  repoField,
  secondsBetween,
  str,
  text,
  type IGitHubSourceDefinition,
} from '../source-definition';

/** Actions switched off for a repository, or a repository with nothing in it yet. */
const actionsUnavailable = (error: GitHubApiError) =>
  featureUnavailable(error, /actions .*disabled/i, /disabled .*actions/i, /repository is empty/i);

export const workflowsSource: IGitHubSourceDefinition = {
  id: 'github_workflows',
  name: 'Workflows',
  description:
    'GitHub Actions workflows of each repository in scope (`GET /repos/{owner}/{repo}/actions/workflows`): one row ' +
    'per workflow file, with its `state` (`active`, `disabled_manually`, `disabled_inactivity`, …). Needs ' +
    'Actions: read.',
  fields: [
    repoField(),
    text('workflow_id', 'Workflow ID', LOOKUP.workflow),
    text('workflow_name', 'Workflow'),
    text('state', 'State'),
    text('path', 'File'),
    date('created_at', 'Created'),
    date('updated_at', 'Updated'),
    text('html_url', 'Link'),
  ],
  load(reader, shape) {
    return reader.perRepository(
      shape,
      async (repository) => {
        const workflows = await reader.client.list(`${repoPath(repository)}/actions/workflows`, {}, { itemsKey: 'workflows' });
        return workflows.map((workflow) => ({
          repo: str(repository.name),
          workflow_id: str(workflow.id),
          workflow_name: str(workflow.name),
          state: str(workflow.state),
          path: str(workflow.path),
          created_at: str(workflow.created_at),
          updated_at: str(workflow.updated_at),
          html_url: str(workflow.html_url),
        }));
      },
      actionsUnavailable,
    );
  },
};

export const workflowRunsSource: IGitHubSourceDefinition = {
  id: 'github_workflow_runs',
  name: 'Workflow runs',
  description:
    'Workflow runs of each repository in scope, newest first, back to the lower bound the filter gives `created_at` ' +
    "— or the instance's history window (30 days by default) without one. `completed_at` is the run's last update " +
    'once it is `completed` (GitHub records no separate end time), and `duration_sec` runs from `started_at` to ' +
    'it, so a re-run counts from its own start. Needs Actions: read.',
  fields: [
    repoField(),
    text('run_id', 'Run ID'),
    number('run_number', 'Run number'),
    number('run_attempt', 'Attempt'),
    text('workflow_id', 'Workflow ID', LOOKUP.workflow),
    text('workflow', 'Workflow'),
    text('event', 'Event'),
    text('branch', 'Branch'),
    text('head_sha', 'Commit'),
    text('status', 'Status'),
    text('conclusion', 'Conclusion'),
    date('created_at', 'Created'),
    date('started_at', 'Started'),
    date('updated_at', 'Updated'),
    date('completed_at', 'Completed'),
    number('duration_sec', 'Duration, s'),
    text('actor', 'Actor'),
    text('triggering_actor', 'Triggered by'),
    text('html_url', 'Link'),
  ],
  load(reader, shape) {
    const since = reader.since(shape, 'created_at');
    return reader.perRepository(
      shape,
      async (repository) => {
        // No filter parameters on purpose: GitHub caps a filtered run listing (branch, status, created, …) at 1 000
        // results, while the plain one pages as deep as it goes. Newest first, so paging stops at the bound.
        const runs = await reader.client.list(
          `${repoPath(repository)}/actions/runs`,
          {},
          {
            itemsKey: 'workflow_runs',
            until: { key: String(since), reached: (run) => before(run.created_at, since) },
          },
        );
        return runs
          .filter((run) => !before(run.created_at, since))
          .map((run) => {
            const completedAt = run.status === 'completed' ? str(run.updated_at) : null;
            return {
              repo: str(repository.name),
              run_id: str(run.id),
              run_number: num(run.run_number),
              run_attempt: num(run.run_attempt),
              workflow_id: str(run.workflow_id),
              workflow: str(run.name),
              event: str(run.event),
              branch: str(run.head_branch),
              head_sha: str(run.head_sha),
              status: str(run.status),
              conclusion: str(run.conclusion),
              created_at: str(run.created_at),
              started_at: str(run.run_started_at),
              updated_at: str(run.updated_at),
              completed_at: completedAt,
              duration_sec: secondsBetween(run.run_started_at, completedAt),
              actor: loginOf(run.actor),
              triggering_actor: loginOf(run.triggering_actor),
              html_url: str(run.html_url),
            };
          });
      },
      actionsUnavailable,
    );
  },
};

export const actionsArtifactsSource: IGitHubSourceDefinition = {
  id: 'github_actions_artifacts',
  name: 'Actions artifacts',
  description:
    'Artifacts the workflow runs of each repository in scope uploaded, expired ones included (`expired`). Read ' +
    'whole — an artifact is storage that exists until it expires, not an event — unless the filter gives ' +
    '`created_at` a lower bound. A storage total is `sum(size_bytes)` where `expired` is false. Needs Actions: read.',
  fields: [
    repoField(),
    text('artifact_id', 'Artifact ID'),
    text('name', 'Artifact'),
    number('size_bytes', 'Size, bytes'),
    flag('expired', 'Expired'),
    date('created_at', 'Created'),
    date('updated_at', 'Updated'),
    date('expires_at', 'Expires'),
    text('workflow_run_id', 'Run ID'),
    text('head_branch', 'Branch'),
    text('head_sha', 'Commit'),
  ],
  load(reader, shape) {
    const bound = shape.lowerBound('created_at');
    const since = bound === undefined ? undefined : reader.since(shape, 'created_at');
    return reader.perRepository(
      shape,
      async (repository) => {
        const artifacts = await reader.client.list(
          `${repoPath(repository)}/actions/artifacts`,
          {},
          {
            itemsKey: 'artifacts',
            until:
              since === undefined
                ? undefined
                : { key: String(since), reached: (artifact) => before(artifact.created_at, since) },
          },
        );
        return artifacts.map((artifact) => {
          const run = obj(artifact.workflow_run);
          return {
            repo: str(repository.name),
            artifact_id: str(artifact.id),
            name: str(artifact.name),
            size_bytes: num(artifact.size_in_bytes),
            expired: bool(artifact.expired),
            created_at: str(artifact.created_at),
            updated_at: str(artifact.updated_at),
            expires_at: str(artifact.expires_at),
            workflow_run_id: str(run?.id),
            head_branch: str(run?.head_branch),
            head_sha: str(run?.head_sha),
          };
        });
      },
      actionsUnavailable,
    );
  },
};

const DEPLOYMENT_STATUS_FIELDS = ['status', 'status_at', 'status_description', 'environment_url'];

export const deploymentsSource: IGitHubSourceDefinition = {
  id: 'github_deployments',
  name: 'Deployments',
  description:
    'Deployments of each repository in scope, newest first, back to the lower bound the filter gives `created_at` ' +
    "— or the instance's history window without one. `status` is the deployment's latest status (`success`, " +
    '`failure`, `in_progress`, `inactive`, …), one call per deployment, and only when the query shows it; past ' +
    `${MAX_DEPLOYMENT_STATUS_FETCHES} deployments that read fails. Needs Deployments: read.`,
  fields: [
    repoField(),
    text('deployment_id', 'Deployment ID'),
    text('environment', 'Environment'),
    text('original_environment', 'Original environment'),
    text('ref', 'Ref'),
    text('sha', 'Commit'),
    text('task', 'Task'),
    text('description', 'Description'),
    text('creator', 'Creator'),
    flag('production_environment', 'Production'),
    flag('transient_environment', 'Transient'),
    date('created_at', 'Created'),
    date('updated_at', 'Updated'),
    text('status', 'Status'),
    date('status_at', 'Status time'),
    text('status_description', 'Status description'),
    text('environment_url', 'Environment URL'),
  ],
  async load(reader, shape) {
    const since = reader.since(shape, 'created_at');
    const environments = shape.values('environment');
    const rows = await reader.perRepository(shape, async (repository) => {
      // One environment named is GitHub's own filter; several are read whole and filtered here.
      const query = environments !== undefined && environments.length === 1 ? { environment: environments[0] } : {};
      const deployments = await reader.client.list(`${repoPath(repository)}/deployments`, query, {
        until: { key: String(since), reached: (deployment) => before(deployment.created_at, since) },
      });
      return deployments
        .filter((deployment) => !before(deployment.created_at, since))
        .map((deployment) => ({
          repo: str(repository.name),
          deployment_id: str(deployment.id),
          environment: str(deployment.environment),
          original_environment: str(deployment.original_environment),
          ref: str(deployment.ref),
          sha: str(deployment.sha),
          task: str(deployment.task),
          description: str(deployment.description),
          creator: loginOf(deployment.creator),
          production_environment: bool(deployment.production_environment),
          transient_environment: bool(deployment.transient_environment),
          created_at: str(deployment.created_at),
          updated_at: str(deployment.updated_at),
          status: null as string | null,
          status_at: null as string | null,
          status_description: null as string | null,
          environment_url: null as string | null,
          __statuses_url: `${repoPath(repository)}/deployments/${encodeURIComponent(String(deployment.id))}/statuses`,
        }));
    });

    const wantsStatus = DEPLOYMENT_STATUS_FIELDS.some((name) => shape.references(name));
    if (wantsStatus && rows.length > MAX_DEPLOYMENT_STATUS_FETCHES) {
      throw new Error(
        `GitHub: ${rows.length} deployments need their status read, one call each, above the cap of ` +
          `${MAX_DEPLOYMENT_STATUS_FETCHES} — narrow the read to repositories, an environment or a shorter period`,
      );
    }
    if (wantsStatus) {
      await mapLimited(rows, async (row: GitHubRecord) => {
        const statuses = await reader.client.get(String(row.__statuses_url), { per_page: 1 });
        const latest = Array.isArray(statuses) ? (statuses[0] as GitHubObject | undefined) : undefined;
        if (latest !== undefined) {
          row.status = str(latest.state);
          row.status_at = str(latest.created_at);
          row.status_description = str(latest.description);
          row.environment_url = str(latest.environment_url);
        }
      });
    }
    for (const row of rows) {
      delete row.__statuses_url;
    }
    return rows;
  },
};
