import type {
  Expression,
  FilterCondition,
  FilterNode,
  IDataSourceParams,
  RowExpression,
} from '../types';
import { resolveRelativeDate } from './relative-date';
import { parseDate } from './value-types';

/**
 * Extracts superset pushdown bounds and referenced columns from a query.
 * Full filter evaluation still runs afterwards via `query-evaluator.ts`.
 */
export interface IQueryShape {
  values(field: string): string[] | undefined;
  lowerBound(field: string): number | undefined;
  upperBound(field: string): number | undefined;
  references(field: string): boolean;
}

function scalarText(value: unknown): string | undefined {
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  return undefined;
}

function boundValue(value: unknown): number | undefined {
  if (typeof value === 'object' && value !== null && (value as { type?: unknown }).type === 'relativeDate') {
    return resolveRelativeDate(value as Parameters<typeof resolveRelativeDate>[0]);
  }
  if (typeof value !== 'string') {
    return undefined;
  }
  return parseDate(value) ?? undefined;
}

function collectConjuncts(node: FilterNode | undefined, conditions: FilterCondition[]): void {
  if (node === undefined || node === null) {
    return;
  }
  if (node.type === 'condition') {
    conditions.push(node);
    return;
  }
  if (node.type === 'group' && node.operator === 'AND' && Array.isArray(node.items)) {
    for (const item of node.items) {
      collectConjuncts(item, conditions);
    }
  }
}

function collectFilterFields(node: FilterNode | undefined, fields: Set<string>): void {
  if (node === undefined || node === null) {
    return;
  }
  if (node.type === 'condition') {
    fields.add(node.field);
    if (
      typeof node.value === 'object' &&
      node.value !== null &&
      !Array.isArray(node.value) &&
      (node.value as { type?: unknown }).type === 'field' &&
      typeof (node.value as { name?: unknown }).name === 'string'
    ) {
      fields.add((node.value as { name: string }).name);
    }
    return;
  }
  if (node.type === 'expression') {
    collectExpressionFields(node.left, fields);
    collectExpressionFields(node.right, fields);
    return;
  }
  if (Array.isArray(node.items)) {
    for (const item of node.items) {
      collectFilterFields(item, fields);
    }
  }
}

function collectExpressionFields(expression: Expression | RowExpression | undefined, fields: Set<string>): void {
  if (expression === undefined || expression === null) {
    return;
  }
  switch (expression.type) {
    case 'field':
      fields.add(expression.field);
      return;
    case 'dateDiff':
      collectExpressionFields(expression.start, fields);
      collectExpressionFields(expression.end, fields);
      return;
    case 'aggregate':
      if (typeof expression.field === 'string') fields.add(expression.field);
      collectExpressionFields(expression.expression, fields);
      collectFilterFields(expression.filter, fields);
      return;
    case 'math':
      collectExpressionFields(expression.left, fields);
      collectExpressionFields(expression.right, fields);
      return;
    default:
      return;
  }
}

export function analyzeQuery(params: IDataSourceParams): IQueryShape {
  const filters: FilterNode[] = [];
  if (params.filter) filters.push(params.filter);
  if (Array.isArray(params.filters)) filters.push(...(params.filters as FilterNode[]));

  const conditions: FilterCondition[] = [];
  for (const filter of filters) {
    collectConjuncts(filter, conditions);
  }

  const values = new Map<string, Set<string>>();
  const lower = new Map<string, number>();
  const upper = new Map<string, number>();
  for (const condition of conditions) {
    switch (condition.operator) {
      case '=':
      case 'IN': {
        const raw = Array.isArray(condition.value) ? condition.value : [condition.value];
        const named = new Set(raw.map(scalarText).filter((text): text is string => text !== undefined));
        if (named.size !== raw.length) {
          break;
        }
        const previous = values.get(condition.field);
        values.set(
          condition.field,
          previous === undefined ? named : new Set([...previous].filter((value) => named.has(value))),
        );
        break;
      }
      case '>':
      case '>=': {
        const bound = boundValue(condition.value);
        if (bound !== undefined) lower.set(condition.field, Math.max(lower.get(condition.field) ?? bound, bound));
        break;
      }
      case '<':
      case '<=': {
        const bound = boundValue(condition.value);
        if (bound !== undefined) upper.set(condition.field, Math.min(upper.get(condition.field) ?? bound, bound));
        break;
      }
      default:
        break;
    }
  }

  const expressions = Array.isArray(params.expressions) ? params.expressions : [];
  let referenced: Set<string> | undefined;
  if (expressions.length > 0) {
    referenced = new Set<string>();
    for (const filter of filters) collectFilterFields(filter, referenced);
    for (const result of expressions) collectExpressionFields(result.expression, referenced);
    for (const term of params.orderBy ?? []) referenced.add(term.field);
    for (const key of Array.isArray(params.groupBy) ? params.groupBy : []) referenced.add(key);
  }

  return {
    values: (field) => {
      const set = values.get(field);
      return set === undefined ? undefined : [...set];
    },
    lowerBound: (field) => lower.get(field),
    upperBound: (field) => upper.get(field),
    references: (field) => referenced === undefined || referenced.has(field),
  };
}
