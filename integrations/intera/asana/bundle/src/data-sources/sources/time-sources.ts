import { UNBOUNDED_START_DATE } from '../../constants';
import type { AsanaObject } from '../../asana/asana-client';
import {
  LOOKUP,
  date,
  gidOf,
  joined,
  nameOf,
  num,
  number,
  str,
  text,
  type IAsanaSourceDefinition,
} from '../source-definition';

const TIME_ENTRY_OPT_FIELDS =
  'duration_minutes,entered_on,created_at,task.name,attributable_to.name,assignee.name,created_by.name,' +
  'approval_status,billable_status,description,categories.name';

/** `YYYY-MM-DD` of an epoch bound, in UTC — the day the bound falls on, which keeps the read a superset. */
function day(epochMs: number): string {
  return new Date(epochMs).toISOString().slice(0, 10);
}

export const timeTrackingSource: IAsanaSourceDefinition = {
  id: 'asana_time_tracking',
  name: 'Time tracking',
  description:
    'Time entries of the workspace (`GET /time_tracking_entries`; Asana\'s time tracking, a paid plan — billable ' +
    'and approval statuses, categories and descriptions need the Timesheets add-on). `duration_minutes` is the ' +
    'time spent on `entered_on`; `project_gid` is the project the time is attributed to. Bounds on `entered_on` go ' +
    'to Asana; without one every entry of the workspace is read. The instance\'s project list keeps the entries ' +
    'attributed to its projects.',
  fields: [
    text('entry_gid', 'Entry ID'),
    text('task_gid', 'Task ID', LOOKUP.task),
    text('task_name', 'Task'),
    text('project_gid', 'Project ID', LOOKUP.project),
    text('project_name', 'Project'),
    text('user_gid', 'User ID', LOOKUP.user),
    text('user_name', 'User'),
    number('duration_minutes', 'Duration (min)'),
    date('entered_on', 'Date'),
    date('created_at', 'Created'),
    text('billable_status', 'Billable'),
    text('approval_status', 'Approval status'),
    text('category_gid', 'Category ID'),
    text('category_name', 'Category'),
    text('description', 'Description'),
  ],
  async load(reader, shape) {
    const workspace = await reader.workspace();
    const lower = shape.lowerBound('entered_on');
    const upper = shape.upperBound('entered_on');
    const entries = await reader.client.list('/time_tracking_entries', {
      workspace: workspace.gid,
      entered_on_start_date: lower === undefined ? UNBOUNDED_START_DATE : day(lower),
      entered_on_end_date: upper === undefined ? undefined : day(upper),
      opt_fields: TIME_ENTRY_OPT_FIELDS,
    });

    const scope = reader.isScoped ? new Set((await reader.scopedProjects()).map((project) => String(project.gid))) : null;

    return entries
      .filter((entry) => scope === null || scope.has(gidOf(entry.attributable_to) ?? ''))
      .map((entry) => {
        const categories = Array.isArray(entry.categories) ? (entry.categories as AsanaObject[]) : [];
        // `assignee` is whose time it is; `created_by` who typed it in, which differs when a manager logs for someone.
        const person = entry.assignee ?? entry.created_by;
        return {
          entry_gid: str(entry.gid),
          task_gid: gidOf(entry.task),
          task_name: nameOf(entry.task),
          project_gid: gidOf(entry.attributable_to),
          project_name: nameOf(entry.attributable_to),
          user_gid: gidOf(person),
          user_name: nameOf(person),
          duration_minutes: num(entry.duration_minutes),
          entered_on: str(entry.entered_on),
          created_at: str(entry.created_at),
          billable_status: str(entry.billable_status),
          approval_status: str(entry.approval_status),
          category_gid: joined(categories, gidOf),
          category_name: joined(categories),
          description: str(entry.description),
        };
      });
  },
};
