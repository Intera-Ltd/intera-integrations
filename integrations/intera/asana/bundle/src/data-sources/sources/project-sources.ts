import { AsanaApiError, type AsanaObject } from '../../asana/asana-client';
import { mapLimited } from '../../asana/map-limited';
import type { AsanaReader } from '../../asana/reader';
import type { AsanaRecord } from '../../types';
import type { IQueryShape } from '../query-shape';
import {
  LOOKUP,
  bool,
  date,
  flag,
  gidOf,
  nameOf,
  str,
  text,
  type IAsanaSourceDefinition,
} from '../source-definition';
import { readGoals, readPortfolios } from './portfolio-sources';

const MAX_BY_ID = 100;

export const projectsSource: IAsanaSourceDefinition = {
  id: 'asana_projects',
  name: 'Projects',
  description:
    'Projects of the workspace (`GET /projects?workspace=`), archived ones included, within the instance\'s project ' +
    'list. `current_status` is the type of the latest status update (`on_track`, `at_risk`, `off_track`, ' +
    '`on_hold`, `complete`, `dropped`) and the `current_status_*` columns are that update; every update is ' +
    '`asana_status_updates`.',
  fields: [
    text('project_gid', 'Project ID', LOOKUP.project),
    text('name', 'Project'),
    text('workspace_gid', 'Workspace ID', LOOKUP.workspace),
    text('team_gid', 'Team ID', LOOKUP.team),
    text('team_name', 'Team'),
    text('owner_gid', 'Owner ID', LOOKUP.user),
    text('owner_name', 'Owner'),
    text('notes', 'Description'),
    date('start_on', 'Start date'),
    date('due_on', 'Due date'),
    date('created_at', 'Created'),
    date('modified_at', 'Modified'),
    flag('archived', 'Archived'),
    flag('completed', 'Completed'),
    date('completed_at', 'Completed at'),
    text('privacy_setting', 'Privacy'),
    text('default_view', 'Default view'),
    text('color', 'Color'),
    text('current_status', 'Current status'),
    text('current_status_title', 'Status title'),
    text('current_status_text', 'Status text'),
    date('current_status_at', 'Status updated'),
    text('permalink_url', 'Link'),
  ],
  async load(reader, shape) {
    const workspace = await reader.workspace();
    const named = shape.values('project_gid');
    const projects = (await reader.scopedProjects()).filter(
      (project) => named === undefined || named.includes(String(project.gid)),
    );
    return projects.map((project) => {
      const status = project.current_status_update as AsanaObject | null | undefined;
      return {
        project_gid: str(project.gid),
        name: str(project.name),
        workspace_gid: gidOf(project.workspace) ?? workspace.gid,
        team_gid: gidOf(project.team),
        team_name: nameOf(project.team),
        owner_gid: gidOf(project.owner),
        owner_name: nameOf(project.owner),
        notes: str(project.notes),
        start_on: str(project.start_on),
        due_on: str(project.due_on),
        created_at: str(project.created_at),
        modified_at: str(project.modified_at),
        archived: bool(project.archived),
        completed: bool(project.completed),
        completed_at: str(project.completed_at),
        privacy_setting: str(project.privacy_setting),
        default_view: str(project.default_view),
        color: str(project.color),
        current_status: str(status?.status_type),
        current_status_title: str(status?.title),
        current_status_text: str(status?.text),
        current_status_at: str(status?.created_at),
        permalink_url: str(project.permalink_url),
      };
    });
  },
};

const SECTION_OPT_FIELDS = 'name,created_at,project.name';

function sectionRow(section: AsanaObject, project: AsanaObject | undefined): AsanaRecord {
  return {
    section_gid: str(section.gid),
    project_gid: gidOf(section.project) ?? str(project?.gid),
    project_name: nameOf(section.project) ?? str(project?.name),
    name: str(section.name),
    created_at: str(section.created_at),
  };
}

export const sectionsSource: IAsanaSourceDefinition = {
  id: 'asana_sections',
  name: 'Sections',
  description:
    'Sections of each project (`GET /projects/{gid}/sections`) — the columns of a board, often the workflow ' +
    'stages. One call per project; filter by `project_gid` to read fewer.',
  fields: [
    text('section_gid', 'Section ID', LOOKUP.section),
    text('project_gid', 'Project ID', LOOKUP.project),
    text('project_name', 'Project'),
    text('name', 'Section'),
    date('created_at', 'Created'),
  ],
  async load(reader, shape) {
    const named = shape.values('section_gid');
    if (named !== undefined && named.length <= MAX_BY_ID) {
      // A label lookup: read the sections themselves, not every project's list.
      const inScope = new Set((await reader.scopedProjects()).map((project) => String(project.gid)));
      const sections = await mapLimited(named, (gid) => getOrNull(reader, `/sections/${encodeURIComponent(gid)}`, SECTION_OPT_FIELDS));
      return sections
        .filter((section): section is AsanaObject => section !== null && inScope.has(gidOf(section.project) ?? ''))
        .map((section) => sectionRow(section, undefined));
    }
    const projects = await reader.contentProjects(shape.values('project_gid'));
    const lists = await mapLimited(projects, async (project) => ({
      project,
      sections: await reader.client.list(`/projects/${project.gid}/sections`, { opt_fields: 'name,created_at' }),
    }));
    return lists.flatMap(({ project, sections }) => sections.map((section) => sectionRow(section, project)));
  },
};

async function getOrNull(reader: AsanaReader, path: string, optFields: string): Promise<AsanaObject | null> {
  try {
    return await reader.client.get(path, { opt_fields: optFields });
  } catch (error) {
    if (error instanceof AsanaApiError && (error.status === 404 || error.status === 403)) {
      return null;
    }
    throw error;
  }
}

const STATUS_OPT_FIELDS = 'resource_subtype,title,text,status_type,author.name,created_by.name,created_at,parent.name';

interface IStatusParent {
  gid: string;
  type: 'project' | 'portfolio' | 'goal';
  name: string | null;
}

/**
 * Portfolios and goals are paid-plan features; on a plan without them their status updates are simply not there,
 * rather than failing the project ones.
 */
async function optionalParents(load: () => Promise<IStatusParent[]>): Promise<IStatusParent[]> {
  try {
    return await load();
  } catch (error) {
    if (error instanceof AsanaApiError && (error.status === 402 || error.status === 403)) {
      return [];
    }
    throw error;
  }
}

async function statusParents(reader: AsanaReader, shape: IQueryShape): Promise<IStatusParent[]> {
  const types = shape.values('parent_type');
  const wants = (type: IStatusParent['type']) => types === undefined || types.includes(type);
  const named = shape.values('parent_gid');
  const parents: IStatusParent[] = [];

  if (wants('project')) {
    const projects = await reader.contentProjects(named);
    parents.push(...projects.map((project) => ({ gid: String(project.gid), type: 'project' as const, name: str(project.name) })));
  }
  // The instance's project list narrows projects only; portfolios and goals are read whole, as their own sources are.
  if (wants('portfolio')) {
    parents.push(
      ...(await optionalParents(async () =>
        (await readPortfolios(reader)).map((portfolio) => ({
          gid: String(portfolio.gid),
          type: 'portfolio' as const,
          name: str(portfolio.name),
        })),
      )),
    );
  }
  if (wants('goal')) {
    parents.push(
      ...(await optionalParents(async () =>
        (await readGoals(reader)).map((goal) => ({ gid: String(goal.gid), type: 'goal' as const, name: str(goal.name) })),
      )),
    );
  }
  return named === undefined ? parents : parents.filter((parent) => named.includes(parent.gid));
}

export const statusUpdatesSource: IAsanaSourceDefinition = {
  id: 'asana_status_updates',
  name: 'Status updates',
  description:
    'Status updates of projects, portfolios and goals (`GET /status_updates?parent=`), one call per parent. ' +
    '`status_type` is `on_track`, `at_risk`, `off_track`, `on_hold`, `complete` or `dropped` for a project or ' +
    'portfolio, and `on_track`, `at_risk`, `off_track`, `achieved`, `partial`, `missed` or `dropped` for a goal. ' +
    'A lower bound on `created_at` and `parent_type` / `parent_gid` narrow the read; the instance\'s project list ' +
    'narrows the projects, while portfolios and goals are read whole.',
  fields: [
    text('status_gid', 'Status update ID'),
    text('parent_gid', 'Parent ID'),
    text('parent_type', 'Parent type'),
    text('parent_name', 'Parent'),
    text('resource_subtype', 'Update type'),
    text('title', 'Title'),
    text('text', 'Text'),
    text('status_type', 'Status'),
    text('author_gid', 'Author ID', LOOKUP.user),
    text('author_name', 'Author'),
    date('created_at', 'Published'),
  ],
  async load(reader, shape) {
    const parents = await statusParents(reader, shape);
    const lowerBound = shape.lowerBound('created_at');
    const createdSince = lowerBound === undefined ? undefined : new Date(lowerBound).toISOString();
    const lists = await mapLimited(parents, async (parent) => ({
      parent,
      updates: await reader.client.list('/status_updates', {
        parent: parent.gid,
        created_since: createdSince,
        opt_fields: STATUS_OPT_FIELDS,
      }),
    }));
    return lists.flatMap(({ parent, updates }) =>
      updates.map((update) => {
        // `author` is the person the update speaks for; `created_by` whoever posted it. They differ only when someone
        // posts on another's behalf, and the author is the one a dashboard means.
        const author = (update.author as AsanaObject | null | undefined) ?? (update.created_by as AsanaObject | null | undefined);
        return {
          status_gid: str(update.gid),
          parent_gid: parent.gid,
          parent_type: parent.type,
          parent_name: nameOf(update.parent) ?? parent.name,
          resource_subtype: str(update.resource_subtype),
          title: str(update.title),
          text: str(update.text),
          status_type: str(update.status_type),
          author_gid: gidOf(author),
          author_name: nameOf(author),
          created_at: str(update.created_at),
        };
      }),
    );
  },
};
