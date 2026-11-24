import type { IAsanaConfig } from '../config';
import { REFERENCE_CACHE_TTL_MS } from '../constants';
import { AsanaClient, type AsanaObject } from './asana-client';

export const WORKSPACE_OPT_FIELDS = 'name,is_organization,email_domains';

/**
 * Every project field `asana_projects` shows. Kept in one list because the project listing is also what resolves the
 * instance's project scope and feeds every per-project fan-out: one cached call serves them all.
 */
export const PROJECT_OPT_FIELDS = [
  'name',
  'workspace',
  'team.name',
  'owner.name',
  'notes',
  'start_on',
  'due_on',
  'created_at',
  'modified_at',
  'archived',
  'completed',
  'completed_at',
  'privacy_setting',
  'default_view',
  'color',
  'current_status_update.title',
  'current_status_update.status_type',
  'current_status_update.text',
  'current_status_update.created_at',
  'permalink_url',
].join(',');

export interface IWorkspace {
  gid: string;
  name: string;
  isOrganization: boolean;
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/**
 * One instance's view of Asana: its token, its workspace and its project scope.
 *
 * The scope is the instance's `projects` list (gids or exact names). What it names is the ceiling of every read —
 * the `asana_projects` list itself, and each per-project read under it — so a filter can only narrow it further.
 */
export class AsanaReader {
  readonly client: AsanaClient;

  constructor(readonly config: IAsanaConfig) {
    this.client = new AsanaClient(config.accessToken, config.apiUrl);
  }

  workspaces(): Promise<AsanaObject[]> {
    return this.client.list('/workspaces', { opt_fields: WORKSPACE_OPT_FIELDS }, REFERENCE_CACHE_TTL_MS);
  }

  /**
   * The configured workspace (gid or name, case-insensitive), else the token's only one. Several and none named is
   * an error listing them, which is how a first setup learns the value.
   */
  async workspace(): Promise<IWorkspace> {
    const all = await this.workspaces();
    const wanted = this.config.workspace;
    const matching =
      wanted === undefined
        ? all
        : all.filter((workspace) => workspace.gid === wanted || text(workspace.name).toLowerCase() === wanted.toLowerCase());

    if (matching.length === 1) {
      const [workspace] = matching;
      return { gid: text(workspace.gid), name: text(workspace.name), isOrganization: workspace.is_organization === true };
    }

    const listed = all.map((workspace) => `${text(workspace.name)} (${text(workspace.gid)})`).join(', ');
    if (all.length === 0) {
      throw new Error('Asana: the token belongs to no workspace');
    }
    if (wanted === undefined) {
      throw new Error(`Asana: the token belongs to several workspaces — name one in the instance: ${listed}`);
    }
    throw new Error(`Asana: no workspace "${wanted}" among the token's workspaces: ${listed}`);
  }

  /** Every project of the workspace the token can see, archived included. */
  async allProjects(): Promise<AsanaObject[]> {
    const workspace = await this.workspace();
    return this.client.list('/projects', { workspace: workspace.gid, opt_fields: PROJECT_OPT_FIELDS });
  }

  /**
   * The projects this instance reads: the configured list, or every project. A configured entry that matches no
   * project fails the read rather than reading nothing — a renamed or deleted project would otherwise turn every
   * dashboard of the instance quietly empty.
   */
  async scopedProjects(): Promise<AsanaObject[]> {
    const projects = await this.allProjects();
    if (this.config.projects.length === 0) {
      return projects;
    }
    const scoped = new Map<string, AsanaObject>();
    for (const entry of this.config.projects) {
      const lowered = entry.toLowerCase();
      const matches = projects.filter(
        (project) => project.gid === entry || text(project.name).toLowerCase() === lowered,
      );
      if (matches.length === 0) {
        throw new Error(
          `Asana: the instance's project "${entry}" is not in the workspace, or the token's user cannot see it`,
        );
      }
      for (const project of matches) {
        scoped.set(text(project.gid), project);
      }
    }
    return [...scoped.values()];
  }

  /**
   * The projects whose contents (tasks, sections, status updates) a read goes through: the scope, narrowed to the
   * gids a filter names, and without archived projects unless the instance asks for them. A project the instance
   * names explicitly is read even when archived — naming it is asking for it.
   */
  async contentProjects(projectGids?: string[]): Promise<AsanaObject[]> {
    const scoped = await this.scopedProjects();
    const named = projectGids === undefined ? undefined : new Set(projectGids);
    const explicit = this.config.projects.length > 0;
    return scoped.filter(
      (project) =>
        (named === undefined || named.has(text(project.gid))) &&
        (explicit || this.config.includeArchivedProjects || project.archived !== true),
    );
  }

  /** Whether the instance restricts reads to a list of projects. */
  get isScoped(): boolean {
    return this.config.projects.length > 0;
  }
}
