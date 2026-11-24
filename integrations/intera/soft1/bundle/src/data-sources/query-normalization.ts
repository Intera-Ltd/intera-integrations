import type { IDataQuery } from '../types';

// Maps Core DataSource parameters onto the service QueryRequest JSON contract.

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function parseJsonParam(value: unknown, fieldName: string): unknown {
  if (typeof value !== 'string') {
    return value;
  }

  try {
    return JSON.parse(value) as unknown;
  } catch {
    throw new Error(`DataSource param "${fieldName}" must be valid JSON`);
  }
}

function parseJsonArrayParam(value: unknown, fieldName: string): unknown[] {
  const parsed = parseJsonParam(value, fieldName);

  if (parsed === undefined) {
    return [];
  }

  return Array.isArray(parsed) ? parsed : [parsed];
}

// Core's IDataSourceParams sends `expressions` flat (top-level), not wrapped in `result` — `result` is
// only used by the legacy IKpiDataProvider.execute() query shape. Support both. When neither is set,
// `expressions` was explicitly omitted by the caller, meaning "return rows as-is" — omit `result`
// entirely so the API falls back to its own passthrough instead of us guessing a field list.
export function normalizeDataQuery(rawQuery: unknown): IDataQuery {
  if (!isRecord(rawQuery)) {
    throw new Error('DataSource query must be an object');
  }

  const result = parseJsonParam(rawQuery.result, 'result');
  const expressions = parseJsonParam(rawQuery.expressions, 'expressions');
  const filterItems = [
    ...parseJsonArrayParam(rawQuery.filter, 'filter'),
    ...parseJsonArrayParam(rawQuery.filters, 'filters'),
  ];

  // Explicit grouping keys (canonical contract, 2026-08-28). Core sends them flat on `params`,
  // exactly like `limit`/`offset`/`orderBy`, while the DSL carries them inside `result` — so they
  // have to be lifted in here, and they are only meaningful alongside expressions: on their own
  // there is nothing to group. Ten plugins were dropping them silently until 2026-09-03, which is
  // why an explicit groupBy did nothing even after the API learned to bind it.
  const groupBy = parseJsonParam(rawQuery.groupBy, 'groupBy');
  const groupByKeys =
    Array.isArray(groupBy) && groupBy.every((key) => typeof key === 'string')
      ? (groupBy as string[])
      : undefined;

  const baseResult = isRecord(result)
    ? (result as IDataQuery['result'])
    : Array.isArray(expressions)
      ? ({ expressions } as IDataQuery['result'])
      : undefined;

  const normalizedResult =
    baseResult && groupByKeys?.length
      ? ({ ...baseResult, groupBy: groupByKeys } as IDataQuery['result'])
      : baseResult;

  // `top` and `order` used to be parsed inline here; `normalizePaging` now owns both (it reads
  // the same `top`/`order` keys plus core's `limit`/`orderBy`), so there is one parser per key.
  return {
    ...(filterItems.length > 0 ? { filters: filterItems as NonNullable<IDataQuery['filters']> } : {}),
    ...(normalizedResult ? { result: normalizedResult } : {}),
    ...normalizePaging(rawQuery),
  };
}

/**
 * Maps core's paging/ordering params onto the DSL (host task grid-server-paging).
 *
 * `limit` → `top`, `offset` → `skip`, `orderBy` → `order`. `offset` is what makes the request a
 * PAGE: the API answers a query carrying `skip` with an `X-Total-Count` header, which the client
 * turns into `{ rows, total }`. Without it the response stays a bare array and the host pages
 * client-side, exactly as before.
 */
function normalizePaging(rawQuery: Record<string, unknown>): Partial<IDataQuery> {
  const paging: Partial<IDataQuery> = {};

  const top = parseNonNegativeInteger(rawQuery.limit ?? rawQuery.top);
  if (top !== undefined && top > 0) {
    paging.top = top;
  }

  const skip = parseNonNegativeInteger(rawQuery.offset ?? rawQuery.skip);
  if (skip !== undefined) {
    paging.skip = skip;
  }

  const order = normalizeOrder(rawQuery.orderBy ?? rawQuery.order);
  if (order.length > 0) {
    paging.order = order;
  }

  return paging;
}

function parseNonNegativeInteger(value: unknown): number | undefined {
  const parsed = typeof value === 'number' ? value : Number.parseInt(String(value ?? ''), 10);
  return Number.isInteger(parsed) && parsed >= 0 ? parsed : undefined;
}

/**
 * Core sends `[{ field, direction }]`; the DSL wants the same shape. Malformed entries are dropped
 * rather than thrown on — an unordered page is a visible degradation, a 500 on a stray param is not.
 */
function normalizeOrder(value: unknown): NonNullable<IDataQuery['order']> {
  const parsed = typeof value === 'string' ? parseJsonParam(value, 'orderBy') : value;
  if (!Array.isArray(parsed)) {
    return [];
  }

  const order: NonNullable<IDataQuery['order']> = [];
  for (const entry of parsed) {
    if (!isRecord(entry)) {
      continue;
    }
    const field = entry.field;
    if (typeof field !== 'string' || field.length === 0) {
      continue;
    }
    // Direction is optional in the DSL (the API defaults to ascending), so an entry without one
    // is kept rather than dropped — narrowing a caller's order list would silently change the
    // rows a page contains.
    const direction = entry.direction;
    order.push(direction === 'asc' || direction === 'desc' ? { field, direction } : { field });
  }

  return order;
}

