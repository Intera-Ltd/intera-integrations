import { MAX_REVIEW_FETCHES, MULTI_VALUE_SEPARATOR } from '../../constants';
import type { GitHubApiError, GitHubObject } from '../../github/github-client';
import { mapLimited } from '../../github/map-limited';
import { repoPath, type GitHubReader } from '../../github/reader';
import type { IQueryShape } from '../query-shape';
import type { GitHubRecord } from '../../types';
import {
  before,
  bool,
  date,
  featureUnavailable,
  firstLine,
  flag,
  joined,
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

const DAY_MS = 86_400_000;

/** An empty repository answers 409 "Git Repository is empty." to a commit listing. */
const emptyRepository = (error: GitHubApiError) => featureUnavailable(error, /repository is empty/i);

/** Issues switched off for a repository answer 410 "Issues are disabled for this repo". */
const issuesUnavailable = (error: GitHubApiError) => featureUnavailable(error, /issues are disabled/i, /repository is empty/i);

/**
 * The lower bound of a pull request or issue read on its `updated_at`. Every one of `created_at`, `closed_at`,
 * `merged_at` and `updated_at` is at most the record's `updated_at`, so the tightest lower bound the filter gives any
 * of them bounds `updated_at` too — and without one, the instance's history window does.
 */
function updatedSince(reader: GitHubReader, shape: IQueryShape, fields: string[]): number {
  const bounds = fields.map((name) => shape.lowerBound(name)).filter((bound): bound is number => bound !== undefined);
  if (bounds.length === 0) {
    return reader.since(shape, 'updated_at');
  }
  return Math.floor(Math.max(...bounds) / DAY_MS) * DAY_MS;
}

/** An upper bound moved up to the end of its UTC day: still a superset, and one cache key all day. */
function endOfUtcDay(ms: number): number {
  return Math.ceil((ms + 1) / DAY_MS) * DAY_MS - 1;
}

export const commitsSource: IGitHubSourceDefinition = {
  id: 'github_commits',
  name: 'Commits',
  description:
    "Commits on each repository's default branch — or on the branches the filter names with `branch` = / IN, one " +
    'row per branch and commit — back to the lower bound the filter gives `timestamp`, or the instance\'s history ' +
    'window without one. `timestamp` is the commit date (when it landed, which is what GitHub\'s `since` reads); ' +
    '`authored_at` is when it was written. `author` is the GitHub account, empty when the commit\'s email matches ' +
    'none. Needs Contents: read.',
  fields: [
    repoField(),
    text('sha', 'Commit'),
    text('branch', 'Branch'),
    text('author', 'Author'),
    text('author_name', 'Author name'),
    text('author_email', 'Author email'),
    text('committer', 'Committer'),
    date('timestamp', 'Committed'),
    date('authored_at', 'Authored'),
    text('message', 'Message'),
    number('parents', 'Parents'),
    flag('is_merge', 'Merge commit'),
    text('html_url', 'Link'),
  ],
  load(reader, shape) {
    const since = reader.since(shape, 'timestamp');
    const upper = shape.upperBound('timestamp');
    const until = upper === undefined ? undefined : endOfUtcDay(upper);
    const named = shape.values('branch');
    return reader.perRepository(
      shape,
      async (repository) => {
        const branches = named ?? [str(repository.default_branch) ?? 'main'];
        const perBranch = await mapLimited(branches, async (branch) => {
          let commits: GitHubObject[];
          try {
            commits = await reader.client.list(`${repoPath(repository)}/commits`, {
              sha: branch,
              since: new Date(since).toISOString(),
              until: until === undefined ? undefined : new Date(until).toISOString(),
            });
          } catch (error) {
            // A branch the filter names that this repository does not have: no commits, not a failure.
            if ((error as GitHubApiError).status === 404 && named !== undefined) return [];
            throw error;
          }
          return commits.map((commit) => {
            const detail = obj(commit.commit);
            const authorDetail = obj(detail?.author);
            const committerDetail = obj(detail?.committer);
            const parents = Array.isArray(commit.parents) ? commit.parents.length : null;
            return {
              repo: str(repository.name),
              sha: str(commit.sha),
              branch,
              author: loginOf(commit.author),
              author_name: str(authorDetail?.name),
              author_email: str(authorDetail?.email),
              committer: loginOf(commit.committer),
              timestamp: str(committerDetail?.date),
              authored_at: str(authorDetail?.date),
              message: firstLine(detail?.message),
              parents,
              is_merge: parents === null ? null : parents > 1,
              html_url: str(commit.html_url),
            };
          });
        }, 4);
        return perBranch.flat();
      },
      emptyRepository,
    );
  },
};

/** Requested reviewers — people and teams — as one text column. */
function requestedReviewers(pull: GitHubObject): string | null {
  const people = Array.isArray(pull.requested_reviewers) ? pull.requested_reviewers.map(loginOf) : [];
  const teams = Array.isArray(pull.requested_teams)
    ? pull.requested_teams.map((team) => {
        const slug = str(obj(team)?.slug);
        return slug === null ? null : `@${slug}`;
      })
    : [];
  const all = [...people, ...teams].filter((entry): entry is string => entry !== null);
  return all.length > 0 ? all.join(MULTI_VALUE_SEPARATOR) : null;
}

function pullState(pull: GitHubObject): string | null {
  if (pull.state === 'open') return 'open';
  if (typeof pull.merged_at === 'string') return 'merged';
  return str(pull.state);
}

export const pullRequestsSource: IGitHubSourceDefinition = {
  id: 'github_pull_requests',
  name: 'Pull requests',
  description:
    'Pull requests of each repository in scope: every open one, plus those closed or merged since the lower bound ' +
    "the filter gives `created_at`, `updated_at`, `closed_at` or `merged_at` — or within the instance's history " +
    'window without one. `state` is `open`, `merged` or `closed` (closed without merging), as GitHub shows it. ' +
    '`reviewers` are the requested reviewers still pending (teams as `@slug`); `reviewed_by` is who submitted a ' +
    `review — one call per pull request, only when the query shows it, refused past ${MAX_REVIEW_FETCHES}. ` +
    'Needs Pull requests: read.',
  fields: [
    repoField(),
    number('number', 'Number'),
    text('title', 'Title'),
    text('state', 'State'),
    flag('is_merged', 'Merged'),
    flag('draft', 'Draft'),
    text('author', 'Author'),
    text('reviewers', 'Requested reviewers'),
    text('reviewed_by', 'Reviewed by'),
    text('assignees', 'Assignees'),
    text('labels', 'Labels'),
    text('milestone', 'Milestone'),
    text('base_branch', 'Base branch'),
    text('head_branch', 'Head branch'),
    date('created_at', 'Created'),
    date('updated_at', 'Updated'),
    date('closed_at', 'Closed'),
    date('merged_at', 'Merged'),
    number('hours_to_merge', 'Hours to merge'),
    text('html_url', 'Link'),
  ],
  async load(reader, shape) {
    const since = updatedSince(reader, shape, ['created_at', 'updated_at', 'closed_at', 'merged_at']);
    const states = shape.values('state');
    const wantsOpen = states === undefined || states.includes('open');
    const wantsClosed = states === undefined || states.some((state) => state !== 'open');
    const rows = await reader.perRepository(shape, async (repository) => {
      const path = `${repoPath(repository)}/pulls`;
      const [open, closed] = await Promise.all([
        wantsOpen ? reader.client.list(path, { state: 'open' }) : Promise.resolve([]),
        wantsClosed
          ? reader.client.list(
              path,
              { state: 'closed', sort: 'updated', direction: 'desc' },
              { until: { key: String(since), reached: (pull) => before(pull.updated_at, since) } },
            )
          : Promise.resolve([]),
      ]);
      return [...open, ...closed.filter((pull) => !before(pull.updated_at, since))].map((pull) => {
        const hours = secondsBetween(pull.created_at, pull.merged_at);
        return {
          repo: str(repository.name),
          number: num(pull.number),
          title: str(pull.title),
          state: pullState(pull),
          is_merged: typeof pull.merged_at === 'string',
          draft: bool(pull.draft),
          author: loginOf(pull.user),
          reviewers: requestedReviewers(pull),
          reviewed_by: null as string | null,
          assignees: joined(pull.assignees, loginOf),
          labels: joined(pull.labels, (label) => str(obj(label)?.name)),
          milestone: str(obj(pull.milestone)?.title),
          base_branch: str(obj(pull.base)?.ref),
          head_branch: str(obj(pull.head)?.ref),
          created_at: str(pull.created_at),
          updated_at: str(pull.updated_at),
          closed_at: str(pull.closed_at),
          merged_at: str(pull.merged_at),
          hours_to_merge: hours === null ? null : Math.round((hours / 3_600) * 10) / 10,
          html_url: str(pull.html_url),
          __reviews_url: `${path}/${String(pull.number)}/reviews`,
        };
      });
    });

    if (shape.references('reviewed_by')) {
      if (rows.length > MAX_REVIEW_FETCHES) {
        throw new Error(
          `GitHub: ${rows.length} pull requests need their reviews read for "reviewed_by", one call each, above the ` +
            `cap of ${MAX_REVIEW_FETCHES} — narrow the read to repositories or a shorter period`,
        );
      }
      await mapLimited(rows, async (row: GitHubRecord) => {
        const reviews = await reader.client.list(String(row.__reviews_url));
        const people = new Set<string>();
        for (const review of reviews) {
          const login = loginOf(review.user);
          if (login !== null && review.state !== 'PENDING') people.add(login);
        }
        row.reviewed_by = people.size > 0 ? [...people].join(MULTI_VALUE_SEPARATOR) : null;
      });
    }
    for (const row of rows) {
      delete row.__reviews_url;
    }
    return rows;
  },
};

export const issuesSource: IGitHubSourceDefinition = {
  id: 'github_issues',
  name: 'Issues',
  description:
    'Issues of each repository in scope, pull requests left out (GitHub lists them together): every open one, ' +
    'plus those closed since the lower bound the filter gives `created_at`, `updated_at` or `closed_at` — or within ' +
    "the instance's history window without one. `assignee` is the first assignee, `assignees` all of them; " +
    '`labels` are comma-joined. Needs Issues: read.',
  fields: [
    repoField(),
    number('number', 'Number'),
    text('title', 'Title'),
    text('state', 'State'),
    text('state_reason', 'State reason'),
    text('issue_type', 'Type'),
    text('labels', 'Labels'),
    text('assignee', 'Assignee'),
    text('assignees', 'Assignees'),
    text('author', 'Author'),
    text('milestone', 'Milestone'),
    number('comments', 'Comments'),
    date('created_at', 'Created'),
    date('updated_at', 'Updated'),
    date('closed_at', 'Closed'),
    number('hours_to_close', 'Hours to close'),
    text('html_url', 'Link'),
  ],
  load(reader, shape) {
    const since = updatedSince(reader, shape, ['created_at', 'updated_at', 'closed_at']);
    const states = shape.values('state');
    const wantsOpen = states === undefined || states.includes('open');
    const wantsClosed = states === undefined || states.includes('closed');
    return reader.perRepository(
      shape,
      async (repository) => {
        if (repository.has_issues === false) {
          return [];
        }
        const path = `${repoPath(repository)}/issues`;
        const [open, closed] = await Promise.all([
          wantsOpen ? reader.client.list(path, { state: 'open' }) : Promise.resolve([]),
          wantsClosed
            ? reader.client.list(path, {
                state: 'closed',
                sort: 'updated',
                direction: 'desc',
                since: new Date(since).toISOString(),
              })
            : Promise.resolve([]),
        ]);
        return [...open, ...closed]
          .filter((issue) => issue.pull_request === undefined)
          .map((issue) => {
            const hours = secondsBetween(issue.created_at, issue.closed_at);
            return {
              repo: str(repository.name),
              number: num(issue.number),
              title: str(issue.title),
              state: str(issue.state),
              state_reason: str(issue.state_reason),
              issue_type: str(obj(issue.type)?.name),
              labels: joined(issue.labels, (label) => (typeof label === 'string' ? label : str(obj(label)?.name))),
              assignee: loginOf(issue.assignee),
              assignees: joined(issue.assignees, loginOf),
              author: loginOf(issue.user),
              milestone: str(obj(issue.milestone)?.title),
              comments: num(issue.comments),
              created_at: str(issue.created_at),
              updated_at: str(issue.updated_at),
              closed_at: str(issue.closed_at),
              hours_to_close: hours === null ? null : Math.round((hours / 3_600) * 10) / 10,
              html_url: str(issue.html_url),
            };
          });
      },
      issuesUnavailable,
    );
  },
};
