import type { GitHubApiError, GitHubObject } from '../github/github-client';
import type { GitHubReader } from '../github/reader';
import { MULTI_VALUE_SEPARATOR } from '../constants';
import type { GitHubRecord, IDataSourceFieldDescriptor, IFieldLookupDeclaration } from '../types';
import type { IQueryShape } from './query-shape';

/**
 * One datasource: its columns and how to read its rows from GitHub. Filtering, grouping, ordering and paging are the
 * same for every source (`github-data-sources.source.ts`); a source only narrows its read by what {@link IQueryShape}
 * allows, and returns a superset of the answer.
 */
export interface IGitHubSourceDefinition {
  id: string;
  name: string;
  description: string;
  fields: IDataSourceFieldDescriptor[];
  load(reader: GitHubReader, shape: IQueryShape): Promise<GitHubRecord[]>;
}

/**
 * Lookups resolve an id to a name in a filter or a grid. Each points at a source that reads its target cheaply: the
 * repository list is one cached call, and a workflow's name comes from the per-repository workflow list.
 */
export const LOOKUP = {
  repository: { datasource: 'github_repositories', valueField: 'repo_id', labelField: 'full_name' },
  workflow: { datasource: 'github_workflows', valueField: 'workflow_id', labelField: 'workflow_name' },
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

/** `repo`, the name every per-repository source carries first: the repository's name within the instance's owner. */
export const repoField = () => text('repo', 'Repository');

// Reading GitHub's JSON. Numeric ids are kept as text (`repo_id`, `run_id`, …): they are identifiers, not
// quantities, and a lookup matches them as text. Timestamps stay the ISO text GitHub sends (`2026-10-09T08:15:00Z`),
// which is what the evaluator compares as a date.

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

/** A nested object, or undefined. */
export function obj(value: unknown): GitHubObject | undefined {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as GitHubObject) : undefined;
}

/** The login of a nested user (`user`, `actor`, `author`, …); null when absent — a deleted account, or a bot-less commit. */
export function loginOf(value: unknown): string | null {
  return str(obj(value)?.login);
}

/** Formats a list as a comma-separated text column. */
export function joined(values: unknown, pick: (item: unknown) => string | null): string | null {
  if (!Array.isArray(values)) return null;
  const parts = values.map(pick).filter((part): part is string => part !== null);
  return parts.length > 0 ? parts.join(MULTI_VALUE_SEPARATOR) : null;
}

/** Whole seconds between two ISO timestamps; null when either is absent or the order is wrong. */
export function secondsBetween(start: unknown, end: unknown): number | null {
  const from = typeof start === 'string' ? Date.parse(start) : Number.NaN;
  const to = typeof end === 'string' ? Date.parse(end) : Number.NaN;
  if (Number.isNaN(from) || Number.isNaN(to) || to < from) return null;
  return Math.round((to - from) / 1_000);
}

/** Whether an ISO timestamp lies before an epoch-millisecond bound (an absent one never does). */
export function before(value: unknown, bound: number): boolean {
  const time = typeof value === 'string' ? Date.parse(value) : Number.NaN;
  return !Number.isNaN(time) && time < bound;
}

/** The first line of a commit message or a description. */
export function firstLine(value: unknown): string | null {
  const content = str(value);
  if (content === null) return null;
  const line = content.split(/\r?\n/, 1)[0].trim();
  return line.length > 0 ? line : null;
}

/**
 * The answers that mean a feature is not there for this repository, rather than that the credential may not read
 * it: GitHub says so in words, and the same status (403, 404) also carries "Resource not accessible by personal
 * access token" — a missing permission, which must fail the read and say so rather than look like no data.
 */
export function featureUnavailable(error: GitHubApiError, ...phrases: RegExp[]): boolean {
  if (error.status !== 403 && error.status !== 404 && error.status !== 409 && error.status !== 410 && error.status !== 422) {
    return false;
  }
  if (/resource not accessible/i.test(error.upstream)) {
    return false;
  }
  return phrases.some((phrase) => phrase.test(error.upstream));
}
