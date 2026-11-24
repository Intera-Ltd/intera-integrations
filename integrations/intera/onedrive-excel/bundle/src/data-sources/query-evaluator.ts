/**
 * In-memory evaluation of the INTERA query AST (Contract 1.5.0) — filter
 * (`condition`, `expression`, `group`), field-to-field comparisons, `now`,
 * `dateDiff`, order, paging, and result expressions.
 */
import type {
  AggregateExpression,
  DataSourceFieldType,
  Expression,
  FieldRefValue,
  FilterCondition,
  FilterExpressionCondition,
  FilterNode,
  IDataSourceFieldDescriptor,
  IDataSourceOrder,
  MathOperator,
  RelativeDateValue,
  ResultExpression,
  RowExpression,
  SheetRecord,
} from '../types';
import { parseDate } from '../sheets/value-types';
import { normalizeFilterNode } from './query-shape';
import {
  computeDateDiff,
  resolveNowAnchor,
  resolveRelativeDate,
} from './relative-date';

export interface IEvaluationSchema {
  fieldTypes: Map<string, DataSourceFieldType>;
  weekStart: number;
  now?: Date;
}

export function buildEvaluationSchema(
  fields: IDataSourceFieldDescriptor[],
  weekStart = 1,
  now?: Date,
): IEvaluationSchema {
  return {
    fieldTypes: new Map(fields.map((field) => [field.name, field.type])),
    weekStart,
    ...(now ? { now } : {}),
  };
}

export function matchesFilter(
  record: SheetRecord,
  filter: FilterNode | undefined,
  schema: IEvaluationSchema,
): boolean {
  if (!filter) {
    return true;
  }
  const normalized = normalizeFilterNode(filter);
  if (normalized.type === 'condition') {
    return matchesCondition(record, normalized, schema);
  }
  if (normalized.type === 'expression') {
    return matchesExpressionCondition(record, normalized, schema);
  }
  const groupOperator: string = normalized.operator;
  switch (groupOperator) {
    case 'AND':
      return normalized.items.every((item) =>
        matchesFilter(record, item, schema),
      );
    case 'OR':
      return normalized.items.some((item) =>
        matchesFilter(record, item, schema),
      );
    case 'NOT':
      return !normalized.items.some((item) =>
        matchesFilter(record, item, schema),
      );
    default:
      throw new Error(`Unsupported filter operator "${groupOperator}"`);
  }
}

export function applyOrder<T extends SheetRecord>(
  records: T[],
  orderBy: IDataSourceOrder[] | undefined,
  schema: IEvaluationSchema,
): T[] {
  if (!orderBy || orderBy.length === 0) {
    return records;
  }
  return [...records].sort((left, right) => {
    for (const term of orderBy) {
      const type = schema.fieldTypes.get(term.field);
      const [a, b] = coerceForCompare(
        left[term.field],
        right[term.field],
        type,
        schema,
      );
      if (a === null && b === null) continue;
      if (a === null) return 1;
      if (b === null) return -1;
      const result = compare(a, b) ?? 0;
      if (result !== 0) {
        return term.direction === 'desc' ? -result : result;
      }
    }
    return 0;
  });
}

export function evaluateExpressions(
  records: SheetRecord[],
  expressions: ResultExpression[],
  schema: IEvaluationSchema,
  orderBy?: IDataSourceOrder[],
  groupBy?: string[],
): SheetRecord[] {
  const withClock = schema.now ? schema : { ...schema, now: new Date() };
  const grouped = expressions.some((entry) =>
    containsAggregate(entry.expression),
  );

  if (!grouped) {
    return applyOrder(records, orderBy, withClock).map((record) =>
      shapeRow({ key: record, rows: [record] }, expressions, withClock),
    );
  }

  if (orderBy && orderBy.length > 0) {
    throw new Error('Order is not supported for aggregate result queries yet.');
  }

  const keyFields: string[] = [];
  if (groupBy && groupBy.length > 0) {
    for (const field of groupBy) {
      addGroupKey(keyFields, field);
    }
  } else {
    for (const entry of expressions) {
      collectGroupKeys(entry.expression, keyFields);
    }
  }

  return groupRecords(records, keyFields).map((group) =>
    shapeRow(group, expressions, withClock),
  );
}

export function evaluateRowExpression(
  expression: RowExpression,
  record: SheetRecord,
  schema: IEvaluationSchema,
): unknown {
  switch (expression.type) {
    case 'value':
      return expression.value;
    case 'field':
      return record[expression.field] ?? null;
    case 'now':
      return resolveNowAnchor(expression.anchor ?? 'now', schema.now);
    case 'dateDiff': {
      const startMs = toEpoch(
        evaluateRowExpression(expression.start, record, schema),
      );
      const endMs = toEpoch(
        evaluateRowExpression(expression.end, record, schema),
      );
      if (startMs === null || endMs === null) {
        return null;
      }
      return computeDateDiff(expression.unit, startMs, endMs, schema.weekStart);
    }
    case 'math':
      return applyMath(
        expression.operator,
        toNumber(evaluateRowExpression(expression.left, record, schema)),
        toNumber(evaluateRowExpression(expression.right, record, schema)),
      );
    default:
      throw new Error(
        `Unsupported expression type "${String(
          (expression as { type: unknown }).type,
        )}" inside a row expression`,
      );
  }
}

interface IRowGroup {
  key: SheetRecord;
  rows: SheetRecord[];
}

function matchesCondition(
  record: SheetRecord,
  condition: FilterCondition,
  schema: IEvaluationSchema,
): boolean {
  const cellValue = record[condition.field];
  const type = schema.fieldTypes.get(condition.field);

  switch (condition.operator) {
    case 'IS_NULL':
      return isEmpty(cellValue);
    case 'IS_NOT_NULL':
      return !isEmpty(cellValue);
    case 'LIKE':
      return matchesLike(cellValue, condition.value);
    case 'NOT_LIKE':
      return !matchesLike(cellValue, condition.value);
    case 'IN':
    case 'NOT_IN': {
      const list = Array.isArray(condition.value) ? condition.value : [];
      const found = list.some((candidate) => {
        const [a, b] = coerceForCompare(cellValue, candidate, type, schema);
        return a !== null && b !== null && compare(a, b) === 0;
      });
      return condition.operator === 'IN' ? found : !found;
    }
    default: {
      const rawRight = isFieldRef(condition.value)
        ? record[condition.value.name]
        : condition.value;
      const effectiveType =
        type ??
        (isFieldRef(condition.value)
          ? schema.fieldTypes.get(condition.value.name)
          : undefined);
      const [a, b] = coerceForCompare(
        cellValue,
        rawRight,
        effectiveType,
        schema,
      );
      return compareWithOperator(compare(a, b), condition.operator);
    }
  }
}

function matchesExpressionCondition(
  record: SheetRecord,
  condition: FilterExpressionCondition,
  schema: IEvaluationSchema,
): boolean {
  const leftVal = evaluateRowExpression(condition.left, record, schema);
  if (condition.operator === 'IS_NULL') {
    return isEmpty(leftVal);
  }
  if (condition.operator === 'IS_NOT_NULL') {
    return !isEmpty(leftVal);
  }
  if (!condition.right) {
    return false;
  }
  const rightVal = evaluateRowExpression(condition.right, record, schema);
  if (isEmpty(leftVal) || isEmpty(rightVal)) {
    return false;
  }
  const leftType = inferRowExpressionType(condition.left, schema);
  const rightType = inferRowExpressionType(condition.right, schema);
  const effectiveType =
    leftType === 'date' || rightType === 'date'
      ? 'date'
      : (leftType ?? rightType);
  const [a, b] = coerceForCompare(leftVal, rightVal, effectiveType, schema);
  return compareWithOperator(compare(a, b), condition.operator);
}

function inferRowExpressionType(
  expression: RowExpression,
  schema: IEvaluationSchema,
): DataSourceFieldType | undefined {
  switch (expression.type) {
    case 'field':
      return schema.fieldTypes.get(expression.field);
    case 'now':
      return 'date';
    case 'dateDiff':
    case 'math':
      return 'number';
    default:
      return undefined;
  }
}

function shapeRow(
  group: IRowGroup,
  expressions: ResultExpression[],
  schema: IEvaluationSchema,
): SheetRecord {
  const result: SheetRecord = {};
  for (const entry of expressions) {
    result[entry.alias] = evaluateExpression(entry.expression, group, schema);
  }
  return result;
}

function evaluateExpression(
  expression: Expression,
  group: IRowGroup,
  schema: IEvaluationSchema,
): unknown {
  switch (expression.type) {
    case 'value':
      return expression.value;

    case 'field':
      return group.key[expression.field] ?? null;

    case 'now':
      return resolveNowAnchor(expression.anchor ?? 'now', schema.now);

    case 'dateDiff': {
      const startMs = toEpoch(
        evaluateExpression(expression.start, group, schema),
      );
      const endMs = toEpoch(evaluateExpression(expression.end, group, schema));
      if (startMs === null || endMs === null) {
        return null;
      }
      return computeDateDiff(expression.unit, startMs, endMs, schema.weekStart);
    }

    case 'aggregate':
      return evaluateAggregate(expression, group, schema);

    case 'math':
      return applyMath(
        expression.operator,
        toNumber(evaluateExpression(expression.left, group, schema)),
        toNumber(evaluateExpression(expression.right, group, schema)),
      );

    default:
      throw new Error(
        `Unsupported expression type "${String(
          (expression as { type: unknown }).type,
        )}"`,
      );
  }
}

function evaluateAggregate(
  expression: AggregateExpression,
  group: IRowGroup,
  schema: IEvaluationSchema,
): unknown {
  const scope = expression.filter
    ? group.rows.filter((record) =>
        matchesFilter(record, expression.filter, schema),
      )
    : group.rows;

  const operand = aggregateOperand(expression);
  if (operand === undefined) {
    if (expression.action === 'count') {
      return scope.length;
    }
    if (expression.action === 'exists') {
      return scope.length > 0;
    }
    throw new Error(
      `Aggregate "${expression.action}" requires a field or an expression`,
    );
  }

  const operands = scope.map((record) =>
    evaluateRowExpression(operand, record, schema),
  );

  switch (expression.action) {
    case 'count':
      return operands.filter((item) => !isEmpty(item)).length;
    case 'exists':
      return operands.some((item) => !isEmpty(item));
    case 'countDistinct': {
      const seen = new Set<unknown>();
      for (const item of operands) {
        if (!isEmpty(item)) {
          seen.add(item);
        }
      }
      return seen.size;
    }
    default:
      break;
  }

  const values = numericValues(operands);
  if (values.length === 0) {
    return expression.action === 'sum' ? 0 : null;
  }
  switch (expression.action) {
    case 'sum':
      return values.reduce((acc, value) => acc + value, 0);
    case 'avg':
      return values.reduce((acc, value) => acc + value, 0) / values.length;
    case 'min':
      return Math.min(...values);
    case 'max':
      return Math.max(...values);
    default:
      throw new Error(
        `Unsupported aggregate action "${String(expression.action)}"`,
      );
  }
}

function aggregateOperand(
  expression: AggregateExpression,
): RowExpression | undefined {
  const field =
    typeof expression.field === 'string' && expression.field.trim().length > 0
      ? expression.field
      : undefined;
  if (expression.expression !== undefined) {
    if (field !== undefined) {
      throw new Error(
        `Aggregate "${expression.action}" takes a field or an expression, not both`,
      );
    }
    return expression.expression;
  }
  return field !== undefined ? { type: 'field', field } : undefined;
}

function containsAggregate(expression: Expression): boolean {
  switch (expression.type) {
    case 'aggregate':
      return true;
    case 'math':
      return (
        containsAggregate(expression.left) ||
        containsAggregate(expression.right)
      );
    default:
      return false;
  }
}

function collectGroupKeys(expression: Expression, keys: string[]): void {
  switch (expression.type) {
    case 'field':
      addGroupKey(keys, expression.field);
      return;
    case 'aggregate':
    case 'now':
      return;
    case 'dateDiff':
      collectGroupKeys(expression.start, keys);
      collectGroupKeys(expression.end, keys);
      return;
    case 'math':
      collectGroupKeys(expression.left, keys);
      collectGroupKeys(expression.right, keys);
      return;
    default:
      return;
  }
}

function addGroupKey(keys: string[], field: string): void {
  if (!keys.includes(field)) {
    keys.push(field);
  }
}

function groupRecords(records: SheetRecord[], keyFields: string[]): IRowGroup[] {
  if (keyFields.length === 0) {
    return [{ key: {}, rows: records }];
  }

  const groups = new Map<string, IRowGroup>();
  for (const record of records) {
    const key: SheetRecord = {};
    const parts: string[] = [];
    for (const field of keyFields) {
      const value = record[field] ?? null;
      key[field] = value;
      parts.push(groupKeyPart(value));
    }
    const keyString = parts.join(' ');
    const group = groups.get(keyString);
    if (group === undefined) {
      groups.set(keyString, { key, rows: [record] });
    } else {
      group.rows.push(record);
    }
  }

  return [...groups.values()];
}

function groupKeyPart(value: unknown): string {
  return value === null || value === undefined
    ? ' null'
    : `${typeof value}:${String(value)}`;
}

function coerceForCompare(
  cellValue: unknown,
  compareValue: unknown,
  type: DataSourceFieldType | undefined,
  schema: IEvaluationSchema,
): [unknown, unknown] {
  if (isRelativeDate(compareValue)) {
    return [
      toEpoch(cellValue),
      resolveRelativeDate(compareValue, schema.weekStart, schema.now),
    ];
  }
  if (type === 'date') {
    return [toEpoch(cellValue), toEpoch(compareValue)];
  }
  if (type === 'number') {
    return [toNumber(cellValue), toNumber(compareValue)];
  }
  if (typeof cellValue === 'number' || typeof compareValue === 'number') {
    return [toNumber(cellValue), toNumber(compareValue)];
  }
  return [toText(cellValue), toText(compareValue)];
}

function isRelativeDate(value: unknown): value is RelativeDateValue {
  return (
    typeof value === 'object' &&
    value !== null &&
    (value as { type?: unknown }).type === 'relativeDate'
  );
}

function isFieldRef(value: unknown): value is FieldRefValue {
  return (
    typeof value === 'object' &&
    value !== null &&
    (value as { type?: unknown }).type === 'field' &&
    typeof (value as { name?: unknown }).name === 'string'
  );
}

function toEpoch(value: unknown): number | null {
  if (value === null || value === undefined) return null;
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (value instanceof Date) return value.getTime();
  return parseDate(String(value));
}

function toNumber(value: unknown): number | null {
  if (value === null || value === undefined || value === '') return null;
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value === 'boolean') return value ? 1 : 0;
  const parsed = Number(String(value).trim());
  return Number.isFinite(parsed) ? parsed : null;
}

function toText(value: unknown): string | null {
  if (value === null || value === undefined) return null;
  return String(value);
}

function isEmpty(value: unknown): boolean {
  return (
    value === null ||
    value === undefined ||
    (typeof value === 'string' && value.trim().length === 0)
  );
}

function matchesLike(cellValue: unknown, pattern: unknown): boolean {
  const text = toText(cellValue);
  const raw = toText(pattern);
  if (text === null || raw === null) {
    return false;
  }
  const haystack = text.toLowerCase();
  const needle = raw.toLowerCase();

  if (!needle.includes('%') && !needle.includes('_')) {
    return haystack === needle;
  }

  const escaped = needle.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const expression = `^${escaped.replace(/%/g, '.*').replace(/_/g, '.')}$`;
  return new RegExp(expression).test(haystack);
}

function compare(left: unknown, right: unknown): number | null {
  if (left === null || right === null) {
    return null;
  }
  if (typeof left === 'number' && typeof right === 'number') {
    return left - right;
  }
  const leftText = String(left);
  const rightText = String(right);
  return leftText < rightText ? -1 : leftText > rightText ? 1 : 0;
}

function compareWithOperator(
  result: number | null,
  operator: string,
): boolean {
  if (result === null) {
    return false;
  }
  switch (operator) {
    case '=':
      return result === 0;
    case '!=':
      return result !== 0;
    case '<':
      return result < 0;
    case '<=':
      return result <= 0;
    case '>':
      return result > 0;
    case '>=':
      return result >= 0;
    default:
      throw new Error(`Unsupported filter operator "${operator}"`);
  }
}

function numericValues(operands: readonly unknown[]): number[] {
  const values: number[] = [];
  for (const operand of operands) {
    const value = toNumber(operand);
    if (value !== null) {
      values.push(value);
    }
  }
  return values;
}

function applyMath(
  operator: MathOperator,
  left: number | null,
  right: number | null,
): number | null {
  if (left === null || right === null) {
    return null;
  }
  switch (operator) {
    case '+':
      return left + right;
    case '-':
      return left - right;
    case '*':
      return left * right;
    case '/':
      return right === 0 ? null : left / right;
    default:
      throw new Error(`Unsupported math operator "${String(operator)}"`);
  }
}
