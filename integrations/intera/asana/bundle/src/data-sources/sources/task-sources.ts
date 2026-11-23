import { AsanaApiError, type AsanaObject } from '../../asana/asana-client';
import { mapLimited } from '../../asana/map-limited';
import { readTasks, type IReadTask } from '../../asana/tasks';
import { MAX_ACTIVITY_TASKS } from '../../constants';
import type { AsanaRecord } from '../../types';
import {
  LOOKUP,
  bool,
  date,
  flag,
  gidOf,
  joined,
  nameOf,
  num,
  number,
  str,
  text,
  type IAsanaSourceDefinition,
} from '../source-definition';

/** Dependencies outside the tasks read whose name and state are fetched one by one. */
const MAX_DEPENDENCY_LOOKUPS = 500;

function taskRow(read: IReadTask, workspaceGid: string): AsanaRecord {
  const { task, primary } = read;
  return {
    task_gid: str(task.gid),
    name: str(task.name),
    resource_subtype: str(task.resource_subtype),
    workspace_gid: gidOf(task.workspace) ?? workspaceGid,
    project_gid: primary?.projectGid ?? null,
    project_name: primary?.projectName ?? null,
    section_gid: primary?.sectionGid ?? null,
    section_name: primary?.sectionName ?? null,
    is_subtask: gidOf(task.parent) !== null,
    parent_gid: gidOf(task.parent),
    parent_name: nameOf(task.parent),
    assignee_gid: gidOf(task.assignee),
    assignee_name: nameOf(task.assignee),
    created_by_gid: gidOf(task.created_by),
    created_by_name: nameOf(task.created_by),
    created_at: str(task.created_at),
    modified_at: str(task.modified_at),
    start_on: str(task.start_on),
    start_at: str(task.start_at),
    due_on: str(task.due_on),
    due_at: str(task.due_at),
    completed: bool(task.completed),
    completed_at: str(task.completed_at),
    completed_by_gid: gidOf(task.completed_by),
    completed_by_name: nameOf(task.completed_by),
    approval_status: str(task.approval_status),
    tags: joined(task.tags),
    notes: str(task.notes),
    num_subtasks: num(task.num_subtasks),
    num_likes: num(task.num_likes),
    actual_time_minutes: num(task.actual_time_minutes),
    permalink_url: str(task.permalink_url),
  };
}

const TASK_SCOPE_NOTE =
  'Read per project (`GET /tasks?project=`) within the instance\'s project list; subtasks are read from their ' +
  'parents unless the instance turns them off. `task_gid` and `project_gid` (= / IN) narrow the read';

export const tasksSource: IAsanaSourceDefinition = {
  id: 'asana_tasks',
  name: 'Tasks',
  description:
    `Tasks and subtasks, one row per task. ${TASK_SCOPE_NOTE}, as do \`completed = false\` and lower bounds on ` +
    '`completed_at` and `modified_at`; past 20 000 tasks a read is refused. A task can be in several projects: ' +
    '`project_gid` / `section_gid` here are the first of them inside the instance\'s scope (for a subtask, its ' +
    'parent\'s project), and every membership is `asana_task_memberships` — count per project there.',
  fields: [
    text('task_gid', 'Task ID', LOOKUP.task),
    text('name', 'Task'),
    text('resource_subtype', 'Task type'),
    text('workspace_gid', 'Workspace ID', LOOKUP.workspace),
    text('project_gid', 'Project ID', LOOKUP.project),
    text('project_name', 'Project'),
    text('section_gid', 'Section ID', LOOKUP.section),
    text('section_name', 'Section'),
    flag('is_subtask', 'Subtask'),
    text('parent_gid', 'Parent task ID', LOOKUP.task),
    text('parent_name', 'Parent task'),
    text('assignee_gid', 'Assignee ID', LOOKUP.user),
    text('assignee_name', 'Assignee'),
    text('created_by_gid', 'Created by ID', LOOKUP.user),
    text('created_by_name', 'Created by'),
    date('created_at', 'Created'),
    date('modified_at', 'Modified'),
    date('start_on', 'Start date'),
    date('start_at', 'Start time'),
    date('due_on', 'Due date'),
    date('due_at', 'Due time'),
    flag('completed', 'Completed'),
    date('completed_at', 'Completed at'),
    text('completed_by_gid', 'Completed by ID', LOOKUP.user),
    text('completed_by_name', 'Completed by'),
    text('approval_status', 'Approval status'),
    text('tags', 'Tags'),
    text('notes', 'Description'),
    number('num_subtasks', 'Subtasks'),
    number('num_likes', 'Likes'),
    number('actual_time_minutes', 'Actual time (min)'),
    text('permalink_url', 'Link'),
  ],
  async load(reader, shape) {
    const workspace = await reader.workspace();
    const reads = await readTasks(reader, shape, { notes: shape.references('notes') });
    return reads.map((read) => taskRow(read, workspace.gid));
  },
};

export const taskMembershipsSource: IAsanaSourceDefinition = {
  id: 'asana_task_memberships',
  name: 'Task memberships',
  description:
    'One row per task and project it belongs to — an Asana task can be in several projects at once, each with its ' +
    `own section. A subtask belongs to its ancestors' projects with \`is_inherited\` = true and no section. ` +
    `${TASK_SCOPE_NOTE}.`,
  fields: [
    text('task_gid', 'Task ID', LOOKUP.task),
    text('task_name', 'Task'),
    text('project_gid', 'Project ID', LOOKUP.project),
    text('project_name', 'Project'),
    text('section_gid', 'Section ID', LOOKUP.section),
    text('section_name', 'Section'),
    flag('is_inherited', 'Inherited'),
  ],
  async load(reader, shape) {
    const reads = await readTasks(reader, shape, {});
    return reads.flatMap(({ task, memberships }) =>
      memberships.map((membership) => ({
        task_gid: str(task.gid),
        task_name: str(task.name),
        project_gid: membership.projectGid,
        project_name: membership.projectName,
        section_gid: membership.sectionGid,
        section_name: membership.sectionName,
        is_inherited: membership.inherited,
      })),
    );
  },
};

export const taskDependenciesSource: IAsanaSourceDefinition = {
  id: 'asana_task_dependencies',
  name: 'Task dependencies',
  description:
    'One row per task and a task it waits for (Asana\'s "blocked by"; a paid Asana plan). `dependency_*` ' +
    `describe the prerequisite. ${TASK_SCOPE_NOTE}.`,
  fields: [
    text('task_gid', 'Task ID', LOOKUP.task),
    text('task_name', 'Task'),
    text('dependency_task_gid', 'Prerequisite ID', LOOKUP.task),
    text('dependency_name', 'Prerequisite'),
    flag('dependency_completed', 'Prerequisite completed'),
    date('dependency_due_on', 'Prerequisite due date'),
  ],
  async load(reader, shape) {
    const reads = await readTasks(reader, shape, { dependencies: true });
    const known = new Map(reads.map(({ task }) => [String(task.gid), task]));

    const pairs = reads.flatMap(({ task }) =>
      (Array.isArray(task.dependencies) ? (task.dependencies as AsanaObject[]) : [])
        .map((dependency) => gidOf(dependency))
        .filter((gid): gid is string => gid !== null)
        .map((dependencyGid) => ({ task, dependencyGid })),
    );

    // A prerequisite in a project the read did not cover is read by itself, for its name and state.
    const missing = [...new Set(pairs.map((pair) => pair.dependencyGid).filter((gid) => !known.has(gid)))];
    if (missing.length > MAX_DEPENDENCY_LOOKUPS) {
      throw new AsanaApiError(
        400,
        `Asana: more than ${MAX_DEPENDENCY_LOOKUPS} prerequisites outside the tasks read. Filter by project_gid or task_gid.`,
      );
    }
    const fetched = await mapLimited(missing, async (gid) => {
      try {
        return await reader.client.get(`/tasks/${gid}`, { opt_fields: 'name,completed,due_on' });
      } catch (error) {
        if (error instanceof AsanaApiError && (error.status === 404 || error.status === 403)) {
          return null;
        }
        throw error;
      }
    });
    for (const task of fetched) {
      if (task !== null) known.set(String(task.gid), task);
    }

    return pairs.map(({ task, dependencyGid }) => {
      const dependency = known.get(dependencyGid);
      return {
        task_gid: str(task.gid),
        task_name: str(task.name),
        dependency_task_gid: dependencyGid,
        dependency_name: str(dependency?.name),
        dependency_completed: bool(dependency?.completed),
        dependency_due_on: str(dependency?.due_on),
      };
    });
  },
};

function customFieldRow(read: IReadTask, field: AsanaObject): AsanaRecord {
  const { task, primary } = read;
  const multi = Array.isArray(field.multi_enum_values) ? (field.multi_enum_values as AsanaObject[]) : [];
  const single = field.enum_value as AsanaObject | null | undefined;
  const dateValue = field.date_value as AsanaObject | null | undefined;
  return {
    task_gid: str(task.gid),
    task_name: str(task.name),
    project_gid: primary?.projectGid ?? null,
    assignee_gid: gidOf(task.assignee),
    completed: bool(task.completed),
    custom_field_gid: str(field.gid),
    custom_field_name: str(field.name),
    field_type: str(field.resource_subtype),
    display_value: str(field.display_value),
    text_value: str(field.text_value),
    number_value: num(field.number_value),
    enum_gid: single ? gidOf(single) : joined(multi, gidOf),
    enum_name: single ? nameOf(single) : joined(multi),
    date_value: str(dateValue?.date_time) ?? str(dateValue?.date),
    people_value: joined(field.people_value, gidOf),
    reference_value: joined(field.reference_value, gidOf),
  };
}

export const taskCustomFieldValuesSource: IAsanaSourceDefinition = {
  id: 'asana_task_custom_field_values',
  name: 'Task custom field values',
  description:
    'One row per task and custom field attached to it (a paid Asana plan), empty values included. Read the value ' +
    'from the column of its `field_type`: `number_value`, `text_value`, `enum_name` (multi-enum comma-joined), ' +
    '`date_value`, `people_value` / `reference_value` (gids, comma-joined); `display_value` is Asana\'s text for any ' +
    'of them. `project_gid`, `assignee_gid` and `completed` repeat the task\'s, so a sum per person or project ' +
    `needs no second source. ${TASK_SCOPE_NOTE}.`,
  fields: [
    text('task_gid', 'Task ID', LOOKUP.task),
    text('task_name', 'Task'),
    text('project_gid', 'Project ID', LOOKUP.project),
    text('assignee_gid', 'Assignee ID', LOOKUP.user),
    flag('completed', 'Task completed'),
    text('custom_field_gid', 'Custom field ID', LOOKUP.customField),
    text('custom_field_name', 'Custom field'),
    text('field_type', 'Field type'),
    text('display_value', 'Display value'),
    text('text_value', 'Text value'),
    number('number_value', 'Number value'),
    text('enum_gid', 'Option ID'),
    text('enum_name', 'Option'),
    date('date_value', 'Date value'),
    text('people_value', 'People'),
    text('reference_value', 'References'),
  ],
  async load(reader, shape) {
    const reads = await readTasks(reader, shape, { customFields: true });
    return reads.flatMap((read) =>
      (Array.isArray(read.task.custom_fields) ? (read.task.custom_fields as AsanaObject[]) : []).map((field) =>
        customFieldRow(read, field),
      ),
    );
  },
};

export const taskActivitySource: IAsanaSourceDefinition = {
  id: 'asana_task_activity',
  name: 'Task activity',
  description:
    'Stories of each task (`GET /tasks/{gid}/stories`): comments (`type` = `comment`) and system activity — ' +
    'assignments, moves, completions, field changes. One call per task, at most 500 tasks a read: filter by ' +
    '`task_gid` or `project_gid`. Comments can be sensitive.',
  fields: [
    text('story_gid', 'Story ID'),
    text('task_gid', 'Task ID', LOOKUP.task),
    text('task_name', 'Task'),
    date('created_at', 'Created'),
    text('created_by_gid', 'Created by ID', LOOKUP.user),
    text('created_by_name', 'Created by'),
    text('type', 'Kind'),
    text('resource_subtype', 'Activity type'),
    text('text', 'Text'),
    flag('is_pinned', 'Pinned'),
  ],
  async load(reader, shape) {
    const reads = await readTasks(reader, shape, {});
    if (reads.length > MAX_ACTIVITY_TASKS) {
      throw new AsanaApiError(
        400,
        `Asana: activity of ${reads.length} tasks asked for, at most ${MAX_ACTIVITY_TASKS} a read. ` +
          'Filter by task_gid or project_gid.',
      );
    }
    const lists = await mapLimited(reads, async ({ task }) => ({
      task,
      stories: await reader.client.list(`/tasks/${task.gid}/stories`, {
        opt_fields: 'created_at,created_by.name,type,resource_subtype,text,is_pinned',
      }),
    }));
    return lists.flatMap(({ task, stories }) =>
      stories.map((story) => ({
        story_gid: str(story.gid),
        task_gid: str(task.gid),
        task_name: str(task.name),
        created_at: str(story.created_at),
        created_by_gid: gidOf(story.created_by),
        created_by_name: nameOf(story.created_by),
        type: str(story.type),
        resource_subtype: str(story.resource_subtype),
        text: str(story.text),
        is_pinned: bool(story.is_pinned),
      })),
    );
  },
};
