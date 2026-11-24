import { SCHEMA_SAMPLE_ROWS } from '../constants';
import type { IDataSourceFieldDescriptor } from '../types';
import {
  columnDateKind,
  hasCellValue,
  inferType,
  type IDateVote,
  type TDateKind,
} from './sheet-values';

export interface ISheetSchema {
  names: string[];
  fields: IDataSourceFieldDescriptor[];
  dateKinds: Array<TDateKind | null>;
}

export interface IBuildSchemaInput {
  /** Data rows only — `headerRow` carries the header when enabled. */
  rows: unknown[][];
  headerRow: unknown[] | null;
  /** Date kind of each sampled cell's style number format, aligned with `rows`. */
  formats: Array<Array<TDateKind | null>>;
  identityColumn: string | null;
}

export function buildSchema(input: IBuildSchemaInput): ISheetSchema {
  const { rows, headerRow, formats, identityColumn } = input;

  let columnCount = headerRow?.length ?? 0;
  for (const row of rows) {
    if (row.length > columnCount) {
      columnCount = row.length;
    }
  }
  if (columnCount === 0) {
    throw new Error('Sheet has no columns');
  }

  const names = deduplicate(
    Array.from({ length: columnCount }, (_, index) =>
      normalizeColumnName(cellText(headerRow?.[index]), index),
    ),
  );
  if (identityColumn !== null && !names.includes(identityColumn)) {
    throw new Error(
      `identity_column "${identityColumn}" is not a column of this sheet. Available: ${names.join(', ')}`,
    );
  }

  const sample = rows.slice(0, SCHEMA_SAMPLE_ROWS);
  const dateKinds: Array<TDateKind | null> = [];
  const fields: IDataSourceFieldDescriptor[] = [];

  for (let index = 0; index < columnCount; index++) {
    const votes: IDateVote[] = [];
    for (let row = 0; row < sample.length && row < formats.length; row++) {
      const value = sample[row][index];
      if (!hasCellValue(value)) {
        continue;
      }
      votes.push({ value, formatKind: formats[row][index] ?? null });
    }

    const dateKind = columnDateKind(votes);
    dateKinds.push(dateKind);

    const field: IDataSourceFieldDescriptor = {
      name: names[index],
      type: inferType(
        sample.map((row) => row[index]),
        dateKind,
      ),
    };

    const label = cellText(headerRow?.[index]).trim();
    if (label.length > 0 && label !== names[index]) {
      field.label = label;
    }
    if (identityColumn !== null && identityColumn === names[index]) {
      field.identity = true;
    }
    fields.push(field);
  }

  return { names, fields, dateKinds };
}

/**
 * Normalizes a header cell to lowercase Unicode letters and digits separated by
 * single underscores, falling back to `column_<n>` when empty.
 */
export function normalizeColumnName(header: string, index: number): string {
  const normalized = header
    .trim()
    .toLowerCase()
    .replace(/[^\p{L}\p{N}]+/gu, '_')
    .replace(/^_+|_+$/g, '');
  return normalized.length > 0 ? normalized : `column_${index + 1}`;
}

function deduplicate(names: string[]): string[] {
  const seen = new Map<string, number>();
  return names.map((name) => {
    const previous = seen.get(name);
    if (previous === undefined) {
      seen.set(name, 1);
      return name;
    }
    let next = previous + 1;
    while (seen.has(`${name}_${next}`)) {
      next++;
    }
    seen.set(name, next);
    seen.set(`${name}_${next}`, 1);
    return `${name}_${next}`;
  });
}

function cellText(value: unknown): string {
  if (value === null || value === undefined) {
    return '';
  }
  return String(value);
}
