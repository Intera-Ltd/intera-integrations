import type { IGitHubConfig } from '../config';
import { MAX_RECORDS_PER_READ, REFERENCE_CACHE_TTL_MS } from '../constants';
import type { IQueryShape } from '@intera-integrations/shared';
import type { GitHubRecord } from '../types';
import { GitHubApiError, GitHubClient, type GitHubObject } from './github-client';
import { mapLimited } from './map-limited';

export interface IOwner {
  login: string;
  isOrganization: boolean;
}

const DAY_MS = 86_400_000;

function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/** Start of the UTC day holding `ms`: a bound moved down to it is still a superset, and stays one cache key all day. */
function startOfUtcDay(ms: number): number {
  return Math.floor(ms / DAY_MS) * DAY_MS;
}

/**
 * One instance's view of GitHub: its credential, its owner and its repository scope.
 *
 * The scope is the instance's `repositories` list. What it names is the ceiling of every read — the
 * `github_repositories` list itself, and each per-repository read under it — so a filter can only narrow it further.
 */
export class GitHubReader {
  readonly client: GitHubClient;

  constructor(readonly config: IGitHubConfig) {
    this.client = new GitHubClient(config);
  }

  /** The instance's owner, as GitHub spells it, and whether it is an organization. */
  async owner(): Promise<IOwner> {
    let account: GitHubObject;
    try {
      account = (await this.client.get(
        `/users/${encodeURIComponent(this.config.owner)}`,
        {},
        REFERENCE_CACHE_TTL_MS,
      )) as GitHubObject;
    } catch (error) {
      if (error instanceof GitHubApiError && error.status === 404) {
        throw new Error(`GitHub: there is no organization or user "${this.config.owner}"`);
      }
      throw error;
    }
    return { login: text(account.login) || this.config.owner, isOrganization: account.type === 'Organization' };
  }

  /**
   * Every repository of the owner the credential can see, archived included.
   *
   * An app reads what its installation was granted. A token reads the organization's list, which holds the private
   * repositories the token may see; for a user account only the token's own user's list (`/user/repos`) holds
   * private ones, and another user's shows the public ones alone.
   */
  async allRepositories(): Promise<GitHubObject[]> {
    const owner = await this.owner();
    const login = owner.login.toLowerCase();
    if (this.config.credential.kind === 'app') {
      const granted = await this.client.list('/installation/repositories', {}, { itemsKey: 'repositories', ttlMs: REFERENCE_CACHE_TTL_MS });
      return granted.filter((repository) => text((repository.owner as GitHubObject | null)?.login).toLowerCase() === login);
    }
    if (owner.isOrganization) {
      return this.client.list(`/orgs/${encodeURIComponent(owner.login)}/repos`, { type: 'all' }, { ttlMs: REFERENCE_CACHE_TTL_MS });
    }
    const me = (await this.client.get('/user', {}, REFERENCE_CACHE_TTL_MS).catch(() => null)) as GitHubObject | null;
    if (me !== null && text(me.login).toLowerCase() === login) {
      return this.client.list('/user/repos', { affiliation: 'owner', visibility: 'all' }, { ttlMs: REFERENCE_CACHE_TTL_MS });
    }
    return this.client.list(`/users/${encodeURIComponent(owner.login)}/repos`, { type: 'owner' }, { ttlMs: REFERENCE_CACHE_TTL_MS });
  }

  /**
   * The repositories this instance reads: the configured list, or every one. A configured name that matches no
   * repository fails the read rather than reading nothing — a renamed or deleted repository, or one the token lost
   * access to, would otherwise turn every dashboard of the instance quietly empty.
   */
  async scopedRepositories(): Promise<GitHubObject[]> {
    const repositories = await this.allRepositories();
    if (this.config.repositories.length === 0) {
      return repositories;
    }
    const scoped: GitHubObject[] = [];
    for (const entry of this.config.repositories) {
      const lowered = entry.toLowerCase();
      const match = repositories.find((repository) => text(repository.name).toLowerCase() === lowered);
      if (match === undefined) {
        throw new Error(
          `GitHub: the instance's repository "${entry}" is not among ${this.config.owner}'s repositories the ` +
            `${this.config.credential.kind === 'token' ? 'token' : 'app'} can see`,
        );
      }
      if (!scoped.includes(match)) scoped.push(match);
    }
    return scoped;
  }

  /**
   * The repositories whose contents (runs, commits, issues, alerts, …) a read goes through: the scope, narrowed to
   * the names a filter gives `repo`, and without archived repositories unless the instance asks for them. A
   * repository the instance names explicitly is read even when archived — naming it is asking for it.
   */
  async contentRepositories(shape: IQueryShape): Promise<GitHubObject[]> {
    const scoped = await this.scopedRepositories();
    const named = shape.values('repo');
    const wanted = named === undefined ? undefined : new Set(named.map((name) => name.toLowerCase()));
    const explicit = this.config.repositories.length > 0;
    return scoped.filter(
      (repository) =>
        (wanted === undefined || wanted.has(text(repository.name).toLowerCase())) &&
        (explicit || this.config.includeArchivedRepositories || repository.archived !== true),
    );
  }

  /**
   * The lower bound of an event-like read on `field`: the filter's own, else the instance's history window — moved
   * down to the start of its UTC day either way.
   */
  since(shape: IQueryShape, field: string): number {
    const bound = shape.lowerBound(field) ?? Date.now() - this.config.historyDays * DAY_MS;
    return startOfUtcDay(bound);
  }

  /**
   * Rows of every content repository, read one repository at a time (the client's gate bounds what is in flight).
   * `skip` names the answers that mean "nothing to read here" — a feature switched off for the repository, an empty
   * repository — which give no rows rather than failing the read; anything else fails it. Past
   * {@link MAX_RECORDS_PER_READ} the read fails, never truncates.
   */
  async perRepository(
    shape: IQueryShape,
    load: (repository: GitHubObject) => Promise<GitHubRecord[]>,
    skip: (error: GitHubApiError) => boolean = () => false,
  ): Promise<GitHubRecord[]> {
    const repositories = await this.contentRepositories(shape);
    const perRepository = await mapLimited(repositories, async (repository) => {
      try {
        return await load(repository);
      } catch (error) {
        if (error instanceof GitHubApiError && skip(error)) {
          return [];
        }
        throw error;
      }
    }, 8);
    const rows = perRepository.flat();
    if (rows.length > MAX_RECORDS_PER_READ) {
      throw new Error(
        `GitHub: the read produced more than ${MAX_RECORDS_PER_READ} rows — narrow it to repositories or a shorter period`,
      );
    }
    return rows;
  }
}

/** `/repos/{owner}/{name}` of a repository object, from its own `full_name`. */
export function repoPath(repository: GitHubObject): string {
  const [owner, name] = text(repository.full_name).split('/');
  return `/repos/${encodeURIComponent(owner)}/${encodeURIComponent(name)}`;
}
