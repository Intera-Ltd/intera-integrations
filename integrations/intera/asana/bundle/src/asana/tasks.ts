import {
  MAX_SUBTASK_DEPTH,
  MAX_SUBTASK_PARENTS,
  MAX_TASKS_BY_ID,
  MAX_TASKS_PER_READ,
} from '../constants';
import type { IQueryShape } from '../data-sources/query-shape';
import { AsanaApiError, type AsanaObject } from './asana-client';
import { mapLimited } from './map-limited';
import type { AsanaReader } from './reader';

const TASK_BASE_FIELDS = [
  'name',
  'resource_subtype',
  'workspace',
  'parent.name',
  'assignee.name',
  'created_by.name',
  'created_at',
  'modified_at',
  'start_on',
  'start_at',
  'due_on',
  'due_at',
  'completed',
  'completed_at',
  'completed_by.name',
  'num_subtasks',
  'num_likes',
  'actual_time_minutes',
  'permalink_url',
  'memberships.project.name',
  'memberships.section.name',
  'tags.name',
  'approval_status',
];

const CUSTOM_FIELD_FIELDS = [
  'custom_fields.name',
  'custom_fields.resource_subtype',
  'custom_fields.display_value',
  'custom_fields.text_value',
  'custom_fields.number_value',
  'custom_fields.enum_value.name',
  'custom_fields.multi_enum_values.name',
  'custom_fields.date_value',
  'custom_fields.people_value.name',
  'custom_fields.reference_value.name',
];

/** What the lightweight listing that finds subtask parents asks for. */
const PARENT_PROBE_FIELDS = 'num_subtasks,memberships.project.name,memberships.section.name';

/** What walking up from a subtask read by id asks for. */
const ANCESTOR_FIELDS = 'parent,memberships.project.name';

export interface ITaskReadOptions {
  /** `notes` is the one large text field; it is fetched only when the query can show it. */
  notes?: boolean;
  customFields?: boolean;
  dependencies?: boolean;
}

export interface ITaskMembership {
  projectGid: string;
  projectName: string | null;
  sectionGid: string | null;
  sectionName: string | null;
  /** Reached through a parent task rather than added to the project directly (Asana's subtasks). */
  inherited: boolean;
}

export interface IReadTask {
  task: AsanaObject;
  /** Direct memberships first, then the ones inherited from ancestors. */
  memberships: ITaskMembership[];
  /** The membership `asana_tasks` reports as the task's project: the first one inside the instance's scope. */
  primary: ITaskMembership | null;
}

function optFields(options: ITaskReadOptions): string {
  const fields = [...TASK_BASE_FIELDS];
  if (options.notes) fields.push('notes');
  if (options.customFields) fields.push(...CUSTOM_FIELD_FIELDS);
  if (options.dependencies) fields.push('dependencies');
  return fields.join(',');
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function directMemberships(task: AsanaObject): ITaskMembership[] {
  const memberships = Array.isArray(task.memberships) ? (task.memberships as AsanaObject[]) : [];
  const result: ITaskMembership[] = [];
  for (const membership of memberships) {
    const project = membership.project as AsanaObject | undefined;
    const section = membership.section as AsanaObject | undefined;
    const projectGid = text(project?.gid);
    if (projectGid === null) continue;
    result.push({
      projectGid,
      projectName: text(project?.name),
      sectionGid: text(section?.gid),
      sectionName: text(section?.name),
      inherited: false,
    });
  }
  return result;
}

/** A task's own memberships plus the projects its ancestors are in, each project once. */
function withInherited(own: ITaskMembership[], ancestors: ITaskMembership[]): ITaskMembership[] {
  const seen = new Set(own.map((membership) => membership.projectGid));
  const result = [...own];
  for (const membership of ancestors) {
    if (seen.has(membership.projectGid)) continue;
    seen.add(membership.projectGid);
    // A subtask sits in no section of its parent's project.
    result.push({ ...membership, sectionGid: null, sectionName: null, inherited: true });
  }
  return result;
}

function isoOrUndefined(epochMs: number | undefined): string | undefined {
  return epochMs === undefined ? undefined : new Date(epochMs).toISOString();
}

/**
 * The tasks a query can need, as a superset of its answer.
 *
 * - **Named tasks** (`task_gid` = / IN, up to {@link MAX_TASKS_BY_ID}) are read one by one — that is also how a
 *   lookup resolves a label — and kept only when inside the instance's scope.
 * - **Otherwise every content project is listed** (`GET /tasks?project=`), narrowed to the projects the filter names.
 *   `completed = false` becomes `completed_since=now`, a lower bound on `completed_at` becomes `completed_since`, and
 *   one on `modified_at` becomes `modified_since` — each returns a superset of the rows that satisfy it.
 * - **Subtasks** are not in a project's listing unless someone added them to the project, so they are read from their
 *   parents, level by level. A parent the narrowed listing left out can still have subtasks that match (a completed
 *   task with an open subtask; a parent whose `modified_at` a subtask's change does not move), so when the listing
 *   was narrowed the parents come from a second, lightweight listing of the whole project. A subtask belongs to its
 *   ancestors' projects as an *inherited* membership.
 *
 * A read past {@link MAX_TASKS_PER_READ} tasks fails rather than truncates.
 */
export async function readTasks(
  reader: AsanaReader,
  shape: IQueryShape,
  options: ITaskReadOptions,
): Promise<IReadTask[]> {
  const workspace = await reader.workspace();
  const contentProjects = await reader.contentProjects();
  const inScope = new Set(contentProjects.map((project) => String(project.gid)));
  const fields = optFields(options);

  const finish = (reads: IReadTask[]): IReadTask[] => {
    for (const read of reads) {
      read.primary = read.memberships.find((membership) => inScope.has(membership.projectGid)) ?? read.memberships[0] ?? null;
    }
    return reads;
  };

  const namedTasks = shape.values('task_gid');
  if (namedTasks !== undefined && namedTasks.length <= MAX_TASKS_BY_ID) {
    const reads = await readTasksById(reader, namedTasks, fields);
    return finish(
      reads.filter(
        (read) =>
          (read.task.workspace as AsanaObject | undefined)?.gid === workspace.gid &&
          read.memberships.some((membership) => inScope.has(membership.projectGid)),
      ),
    );
  }

  const namedProjects = shape.values('project_gid');
  const projects = namedProjects === undefined ? contentProjects : contentProjects.filter((p) => namedProjects.includes(String(p.gid)));

  const completedSince =
    shape.values('completed')?.length === 1 && shape.values('completed')?.[0] === 'false'
      ? 'now'
      : isoOrUndefined(shape.lowerBound('completed_at'));
  const modifiedSince = isoOrUndefined(shape.lowerBound('modified_at'));
  const narrowed = completedSince !== undefined || modifiedSince !== undefined;

  const reads = new Map<string, IReadTask>();
  const listings = await mapLimited(projects, (project) =>
    reader.client.list('/tasks', {
      project: String(project.gid),
      opt_fields: fields,
      completed_since: completedSince,
      modified_since: modifiedSince,
    }),
  );
  for (const task of listings.flat()) {
    const gid = String(task.gid);
    if (!reads.has(gid)) {
      reads.set(gid, { task, memberships: directMemberships(task), primary: null });
    }
    guardSize(reads.size);
  }

  if (reader.config.includeSubtasks) {
    // Parents to descend from, with the memberships their subtasks inherit.
    let parents: Array<{ gid: string; memberships: ITaskMembership[] }>;
    if (narrowed) {
      const probes = await mapLimited(projects, (project) =>
        reader.client.list('/tasks', { project: String(project.gid), opt_fields: PARENT_PROBE_FIELDS }),
      );
      const byGid = new Map<string, ITaskMembership[]>();
      for (const task of probes.flat()) {
        if (typeof task.num_subtasks === 'number' && task.num_subtasks > 0) {
          byGid.set(String(task.gid), directMemberships(task));
        }
      }
      parents = [...byGid].map(([gid, memberships]) => ({ gid, memberships }));
    } else {
      parents = [...reads.values()]
        .filter((read) => typeof read.task.num_subtasks === 'number' && read.task.num_subtasks > 0)
        .map((read) => ({ gid: String(read.task.gid), memberships: read.memberships }));
    }

    let fetchedParents = 0;
    for (let depth = 0; depth < MAX_SUBTASK_DEPTH && parents.length > 0; depth++) {
      fetchedParents += parents.length;
      if (fetchedParents > MAX_SUBTASK_PARENTS) {
        throw new AsanaApiError(
          400,
          `Asana: more than ${MAX_SUBTASK_PARENTS} tasks with subtasks to read. Filter by project_gid, name ` +
            'fewer projects in the instance, or turn subtasks off for it.',
        );
      }
      const children = await mapLimited(parents, async (parent) => ({
        parent,
        subtasks: await reader.client.list(`/tasks/${parent.gid}/subtasks`, { opt_fields: fields }),
      }));
      const next: typeof parents = [];
      for (const { parent, subtasks } of children) {
        for (const task of subtasks) {
          const gid = String(task.gid);
          const memberships = withInherited(directMemberships(task), parent.memberships);
          if (!reads.has(gid)) {
            reads.set(gid, { task, memberships, primary: null });
            guardSize(reads.size);
          }
          if (typeof task.num_subtasks === 'number' && task.num_subtasks > 0) {
            next.push({ gid, memberships });
          }
        }
      }
      parents = next;
    }
  }

  return finish([...reads.values()]);
}

function guardSize(size: number): void {
  if (size > MAX_TASKS_PER_READ) {
    throw new AsanaApiError(
      400,
      `Asana: the read covers more than ${MAX_TASKS_PER_READ} tasks. Filter by project_gid, completed = false or a ` +
        'lower bound on modified_at, or name fewer projects in the instance.',
    );
  }
}

/**
 * Tasks read one by one. A gid Asana does not know (deleted, or another workspace's) is simply absent, as a lookup
 * over a stale id should be. A subtask's inherited projects come from walking up its parents.
 */
async function readTasksById(reader: AsanaReader, gids: string[], fields: string): Promise<IReadTask[]> {
  const tasks = await mapLimited(gids, async (gid) => {
    try {
      return await reader.client.get(`/tasks/${encodeURIComponent(gid)}`, { opt_fields: fields });
    } catch (error) {
      if (error instanceof AsanaApiError && (error.status === 404 || error.status === 403)) {
        return null;
      }
      throw error;
    }
  });

  return mapLimited(
    tasks.filter((task): task is AsanaObject => task !== null),
    async (task) => {
      const ancestors: ITaskMembership[] = [];
      let parentGid = text((task.parent as AsanaObject | undefined)?.gid);
      for (let depth = 0; parentGid !== null && depth < MAX_SUBTASK_DEPTH; depth++) {
        const parent = await reader.client.get(`/tasks/${parentGid}`, { opt_fields: ANCESTOR_FIELDS });
        ancestors.push(...directMemberships(parent));
        parentGid = text((parent.parent as AsanaObject | undefined)?.gid);
      }
      return { task, memberships: withInherited(directMemberships(task), ancestors), primary: null };
    },
  );
}
