import type { AsanaReader } from '../asana/reader';
import { MULTI_VALUE_SEPARATOR } from '../constants';
import type { AsanaRecord, IDataSourceFieldDescriptor, IFieldLookupDeclaration } from '../types';
import type { IQueryShape } from '@intera-integrations/shared';

/**
 * One datasource: its columns and how to read its rows from Asana. Filtering, grouping, ordering and paging are the
 * same for every source (`asana-data-sources.source.ts`); a source only narrows its read by what {@link IQueryShape}
 * allows, and returns a superset of the answer.
 */
export interface IAsanaSourceDefinition {
  id: string;
  name: string;
  description: string;
  fields: IDataSourceFieldDescriptor[];
  load(reader: AsanaReader, shape: IQueryShape): Promise<AsanaRecord[]>;
}

/**
 * Lookups resolve a gid to a name in a filter or a grid. Every one points at a source that reads its target cheaply
 * by gid (`=` / `IN` is pushed down), so a label never costs a whole fan-out.
 */
export const LOOKUP = {
  workspace: { datasource: 'asana_workspaces', valueField: 'workspace_gid', labelField: 'name' },
  team: { datasource: 'asana_teams', valueField: 'team_gid', labelField: 'name' },
  project: { datasource: 'asana_projects', valueField: 'project_gid', labelField: 'name' },
  section: { datasource: 'asana_sections', valueField: 'section_gid', labelField: 'name' },
  task: { datasource: 'asana_tasks', valueField: 'task_gid', labelField: 'name' },
  user: { datasource: 'asana_users', valueField: 'user_gid', labelField: 'name' },
  customField: { datasource: 'asana_custom_fields', valueField: 'custom_field_gid', labelField: 'name' },
  portfolio: { datasource: 'asana_portfolios', valueField: 'portfolio_gid', labelField: 'name' },
} satisfies Record<string, IFieldLookupDeclaration>;

function field(
  name: string,
  type: IDataSourceFieldDescriptor['type'],
  label: string,
  lookup?: IFieldLookupDeclaration,
): IDataSourceFieldDescriptor {
  return lookup === undefined ? { name, type, label } : { name, type, label, lookup };
}

export const text = (name: string, label: string, lookup?: IFieldLookupDeclaration) =>
  field(name, 'string', label, lookup);
export const number = (name: string, label: string) => field(name, 'number', label);
export const flag = (name: string, label: string) => field(name, 'boolean', label);
export const date = (name: string, label: string) => field(name, 'date', label);

// Reading Asana's JSON. Every id is text (Asana's gids are numeric strings longer than a double holds exactly), every
// timestamp stays the ISO text Asana sends (`2026-10-09T08:15:00.000Z`, `2026-10-09` for a date), which is what the
// evaluator compares as a date.

export function str(value: unknown): string | null {
  if (typeof value === 'string') return value.length > 0 ? value : null;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  return null;
}

export function num(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

export function bool(value: unknown): boolean | null {
  return typeof value === 'boolean' ? value : null;
}

/** The gid of a nested object (`assignee`, `team`, …), null when the object is absent. */
export function gidOf(value: unknown): string | null {
  return str((value as { gid?: unknown } | null | undefined)?.gid);
}

/** The name of a nested object; status updates and a few others call it `title`. */
export function nameOf(value: unknown): string | null {
  const object = value as { name?: unknown; title?: unknown } | null | undefined;
  return str(object?.name) ?? str(object?.title);
}

/** Formats a list of nested objects as a comma-separated text column. */
export function joined(values: unknown, pick: (item: unknown) => string | null = nameOf): string | null {
  if (!Array.isArray(values)) return null;
  const parts = values.map(pick).filter((part): part is string => part !== null);
  return parts.length > 0 ? parts.join(MULTI_VALUE_SEPARATOR) : null;
}
