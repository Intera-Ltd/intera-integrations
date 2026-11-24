import type {
  Expression,
  FilterCondition,
  FilterExpressionCondition,
  FilterNode,
  IDataSourceParams,
  RelativeDateValue,
  RowExpression,
} from './contract-types';
import {
  resolveRelativeDateRange,
  type IRelativeDateOptions,
} from './relative-date';
import { parseDate } from './value-types';

/**
 * Extracts superset pushdown bounds, referenced columns, and normalized filter
 * nodes (including Query Contract 1.5+ expression filters and half-open
 * `relativeDate` spans) from `IDataSourceParams`.
 */
export interface IQueryShape {
  filters: FilterNode[];
  values(field: string): string[] | undefined;
  lowerBound(field: string): number | undefined;
  upperBound(field: string): number | undefined;
  references(field: string): boolean;
}

export function analyzeQuery(
  params: IDataSourceParams,
  weekStart: number | string | undefined = params.weekStart,
  now?: Date,
  timeZone: string | undefined = params.timeZone,
): IQueryShape {
  const options: IRelativeDateOptions = {
    ...(now ? { now } : {}),
    ...(timeZone ? { timeZone } : {}),
    ...(weekStart !== undefined ? { weekStart } : {}),
  };

  const filters = normalizeFilterList(params);

  const conditions: FilterCondition[] = [];
  for (const filter of filters) {
    collectConjuncts(filter, conditions);
  }

  const values = new Map<string, Set<string>>();
  const lower = new Map<string, number>();
  const upper = new Map<string, number>();

  for (const condition of conditions) {
    if (isRelativeDate(condition.value)) {
      const range = resolveRelativeDateRange(condition.value, options);
      const inclusiveEnd =
        range.endExclusive !== null ? range.endExclusive - 1 : range.start;

      switch (condition.operator) {
        case '=':
          setLower(lower, condition.field, range.start);
          setUpper(upper, condition.field, inclusiveEnd);
          break;
        case '>=':
          setLower(lower, condition.field, range.start);
          break;
        case '>':
          setLower(
            lower,
            condition.field,
            range.endExclusive !== null ? range.endExclusive : range.start,
          );
          break;
        case '<':
          setUpper(upper, condition.field, range.start);
          break;
        case '<=':
          setUpper(upper, condition.field, inclusiveEnd);
          break;
        default:
          break;
      }
      continue;
    }

    switch (condition.operator) {
      case '=':
      case 'IN': {
        const raw = Array.isArray(condition.value)
          ? condition.value
          : [condition.value];
        const named = new Set(
          raw
            .map(scalarText)
            .filter((text): text is string => text !== undefined),
        );
        if (named.size !== raw.length) {
          break;
        }
        const previous = values.get(condition.field);
        values.set(
          condition.field,
          previous === undefined
            ? named
            : new Set([...previous].filter((value) => named.has(value))),
        );
        break;
      }
      case '>':
      case '>=': {
        const bound = boundLiteralDate(condition.value, options.timeZone);
        if (bound !== undefined) {
          setLower(lower, condition.field, bound);
        }
        break;
      }
      case '<':
      case '<=': {
        const bound = boundLiteralDate(condition.value, options.timeZone);
        if (bound !== undefined) {
          setUpper(upper, condition.field, bound);
        }
        break;
      }
      default:
        break;
    }
  }

  const expressions = Array.isArray(params.expressions)
    ? params.expressions
    : [];
  let referenced: Set<string> | undefined;
  if (expressions.length > 0) {
    referenced = new Set<string>();
    for (const filter of filters) {
      collectFilterFields(filter, referenced);
    }
    for (const result of expressions) {
      collectExpressionFields(result.expression, referenced);
    }
    for (const term of params.orderBy ?? []) {
      referenced.add(term.field);
    }
    for (const key of Array.isArray(params.groupBy) ? params.groupBy : []) {
      referenced.add(key);
    }
  }

  return {
    filters,
    values: (field) => {
      const set = values.get(field);
      return set === undefined ? undefined : [...set];
    },
    lowerBound: (field) => lower.get(field),
    upperBound: (field) => upper.get(field),
    references: (field) => referenced === undefined || referenced.has(field),
  };
}

/**
 * Normalizes a filter tree so expression filters rewriting simple field
 * comparisons (`field op value`, `field op field`, `field IS_NULL`) and nested
 * groups carry canonical Query Contract 1.5+ nodes.
 */
export function normalizeFilterNode(node: FilterNode): FilterNode {
  if (node.type === 'group') {
    return {
      type: 'group',
      operator: node.operator,
      items: Array.isArray(node.items)
        ? node.items.map(normalizeFilterNode)
        : [],
    };
  }
  if (node.type === 'expression') {
    return normalizeExpressionCondition(node);
  }
  return node;
}

function normalizeFilterList(params: IDataSourceParams): FilterNode[] {
  const filters: FilterNode[] = [];
  if (params.filter) {
    filters.push(normalizeFilterNode(params.filter));
  }
  if (Array.isArray(params.filters)) {
    for (const filter of params.filters) {
      filters.push(normalizeFilterNode(filter));
    }
  }
  return filters;
}

function normalizeExpressionCondition(
  condition: FilterExpressionCondition,
): FilterNode {
  const left = normalizeRowExpression(condition.left);
  if (
    condition.operator === 'IS_NULL' ||
    condition.operator === 'IS_NOT_NULL'
  ) {
    if (left.type === 'field') {
      return {
        type: 'condition',
        field: left.field,
        operator: condition.operator,
      };
    }
    return { type: 'expression', left, operator: condition.operator };
  }

  const right = condition.right
    ? normalizeRowExpression(condition.right)
    : undefined;
  if (left.type === 'field' && right !== undefined) {
    if (right.type === 'value') {
      return {
        type: 'condition',
        field: left.field,
        operator: condition.operator,
        value: right.value,
      };
    }
    if (right.type === 'field') {
      return {
        type: 'condition',
        field: left.field,
        operator: condition.operator,
        value: { type: 'field', name: right.field },
      };
    }
  }

  return {
    type: 'expression',
    left,
    operator: condition.operator,
    ...(right !== undefined ? { right } : {}),
  };
}

function normalizeRowExpression(expression: RowExpression): RowExpression {
  if (expression.type === 'field') {
    const raw = expression as { type: 'field'; field?: string; name?: string };
    const field =
      typeof raw.field === 'string'
        ? raw.field
        : typeof raw.name === 'string'
          ? raw.name
          : '';
    return { type: 'field', field };
  }
  if (expression.type === 'dateDiff') {
    return {
      type: 'dateDiff',
      unit: expression.unit,
      start: normalizeRowExpression(expression.start),
      end: normalizeRowExpression(expression.end),
    };
  }
  if (expression.type === 'math') {
    return {
      type: 'math',
      operator: expression.operator,
      left: normalizeRowExpression(expression.left),
      right: normalizeRowExpression(expression.right),
    };
  }
  return expression;
}

function setLower(map: Map<string, number>, field: string, bound: number): void {
  const prev = map.get(field);
  map.set(field, prev === undefined ? bound : Math.max(prev, bound));
}

function setUpper(map: Map<string, number>, field: string, bound: number): void {
  const prev = map.get(field);
  map.set(field, prev === undefined ? bound : Math.min(prev, bound));
}

function isRelativeDate(value: unknown): value is RelativeDateValue {
  return (
    typeof value === 'object' &&
    value !== null &&
    !Array.isArray(value) &&
    (value as { type?: unknown }).type === 'relativeDate'
  );
}

function boundLiteralDate(
  value: unknown,
  timeZone?: string,
): number | undefined {
  if (typeof value !== 'string') {
    return undefined;
  }
  return parseDate(value, timeZone) ?? undefined;
}

function scalarText(value: unknown): string | undefined {
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean') {
    return String(value);
  }
  return undefined;
}

function collectConjuncts(
  node: FilterNode | undefined,
  conditions: FilterCondition[],
): void {
  if (node === undefined || node === null) {
    return;
  }
  if (node.type === 'condition') {
    conditions.push(node);
    return;
  }
  if (
    node.type === 'group' &&
    node.operator === 'AND' &&
    Array.isArray(node.items)
  ) {
    for (const item of node.items) {
      collectConjuncts(item, conditions);
    }
  }
}

function collectFilterFields(
  node: FilterNode | undefined,
  fields: Set<string>,
): void {
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

function collectExpressionFields(
  expression: Expression | RowExpression | undefined,
  fields: Set<string>,
): void {
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
