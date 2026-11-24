import type { DataSourceFieldType } from '../types';
import type { TDateKind } from '../xlsx/number-format';
import { isBlank, parseBoolean, parseDate, parseNumber } from '@intera-integrations/shared';

export type { TDateKind } from '../xlsx/number-format';

/**
 * Cell value normalization and column type inference for Excel `.xlsx` cells,
 * supporting both the 1900 (including the Lotus 1-2-3 leap-year offset) and
 * 1904 date systems.
 */

export interface IDateVote {
  value: unknown;
  /** Date kind of the cell's number format, or `null` when it has none. */
  formatKind: TDateKind | null;
}

const EPOCH_1900_MS = Date.UTC(1899, 11, 30);
const EPOCH_1900_EARLY_MS = Date.UTC(1899, 11, 31);
const LOTUS_BUG_SERIAL = 61;
const EPOCH_1904_MS = Date.UTC(1904, 0, 1);
const HAS_TIME_RE = /[T ]\d{1,2}:\d{2}/;

/**
 * Determines a column's date kind from sampled non-empty cells. Number cells
 * vote using their style format; text cells vote only if `parseDate` recognizes
 * their text (an unused date format applied to non-date text never votes).
 */
export function columnDateKind(cells: IDateVote[]): TDateKind | null {
  let sawDate = false;
  let sawDateTime = false;
  let sawTime = false;

  for (const cell of cells) {
    const kind =
      typeof cell.value === 'number'
        ? cell.formatKind
        : textDateKind(cell.value);
    if (kind === null) {
      return null;
    }
    if (kind === 'date') sawDate = true;
    if (kind === 'dateTime') sawDateTime = true;
    if (kind === 'time') sawTime = true;
  }

  if (sawTime) {
    return sawDate || sawDateTime ? null : 'time';
  }
  if (sawDateTime) return 'dateTime';
  return sawDate ? 'date' : null;
}

/**
 * Infers a column's field type from its sampled values and resolved date kind.
 * A column is `number`, `date`, or `boolean` only when every non-blank sampled
 * cell conforms; empty columns default to `string`.
 */
export function inferType(
  values: unknown[],
  dateKind: TDateKind | null,
): DataSourceFieldType {
  if (!values.some(hasCellValue)) {
    return 'string';
  }

  if (dateKind === 'date' || dateKind === 'dateTime') {
    return 'date';
  }
  if (dateKind === 'time') {
    return 'string';
  }

  let sawValue = false;
  let couldBeNumber = true;
  let couldBeDate = true;
  let couldBeBoolean = true;

  for (const value of values) {
    if (value === null || value === undefined) {
      continue;
    }
    if (typeof value === 'number') {
      sawValue = true;
      couldBeDate = false;
      couldBeBoolean = false;
      continue;
    }
    if (typeof value === 'boolean') {
      sawValue = true;
      couldBeNumber = false;
      couldBeDate = false;
      continue;
    }

    const text = String(value);
    if (isBlank(text)) {
      continue;
    }
    sawValue = true;
    if (couldBeNumber && parseNumber(text) === null) couldBeNumber = false;
    if (couldBeDate && parseDate(text) === null) couldBeDate = false;
    if (couldBeBoolean && parseBoolean(text) === null) couldBeBoolean = false;
    if (!couldBeNumber && !couldBeDate && !couldBeBoolean) {
      return 'string';
    }
  }

  if (!sawValue) return 'string';
  if (couldBeNumber) return 'number';
  if (couldBeDate) return 'date';
  if (couldBeBoolean) return 'boolean';
  return 'string';
}

/** Converts a raw worksheet cell value according to the column's inferred schema. */
export function convertCell(
  raw: unknown,
  type: DataSourceFieldType,
  dateKind: TDateKind | null,
  cellDateKind: TDateKind | null,
  date1904: boolean,
): unknown {
  if (raw === null || raw === undefined || raw === '') {
    return null;
  }

  if (typeof raw === 'number') {
    if (dateKind !== null) {
      return serialToText(raw, dateKind, date1904);
    }
    if (type === 'string') {
      return cellDateKind !== null
        ? serialToText(raw, cellDateKind, date1904)
        : String(raw);
    }
    return raw;
  }

  if (typeof raw === 'boolean') {
    return type === 'string' ? String(raw) : raw;
  }

  const text = String(raw).trim();
  if (text.length === 0) {
    return null;
  }
  if (type === 'number') {
    return parseNumber(text) ?? text;
  }
  if (type === 'boolean') {
    return parseBoolean(text) ?? text;
  }
  if (type === 'date' && dateKind !== null) {
    const epoch = parseDate(text);
    if (epoch !== null) {
      return localEpochToText(epoch, dateKind);
    }
  }
  return text;
}

/** Converts an Excel serial number into canonical ISO date/time text. */
export function serialToText(
  serial: number,
  kind: TDateKind,
  date1904: boolean,
): string {
  const totalSeconds = Math.round(serial * 86400);
  const epoch = date1904
    ? EPOCH_1904_MS
    : serial < LOTUS_BUG_SERIAL
      ? EPOCH_1900_EARLY_MS
      : EPOCH_1900_MS;
  const at = new Date(epoch + totalSeconds * 1000);

  const hours = pad(at.getUTCHours());
  const minutes = pad(at.getUTCMinutes());
  const seconds = pad(at.getUTCSeconds());
  if (kind === 'time') {
    return `${hours}:${minutes}:${seconds}`;
  }

  const day = `${at.getUTCFullYear()}-${pad(at.getUTCMonth() + 1)}-${pad(at.getUTCDate())}`;
  return kind === 'date' ? day : `${day}T${hours}:${minutes}:${seconds}`;
}

/** True when a cell holds a non-whitespace value. */
export function hasCellValue(value: unknown): boolean {
  if (value === null || value === undefined) {
    return false;
  }
  return typeof value === 'string' ? value.trim().length > 0 : true;
}

function textDateKind(value: unknown): TDateKind | null {
  if (typeof value !== 'string') {
    return null;
  }
  const text = value.trim();
  if (text.length === 0 || parseDate(text) === null) {
    return null;
  }
  return HAS_TIME_RE.test(text) ? 'dateTime' : 'date';
}

function localEpochToText(epoch: number, kind: TDateKind): string {
  const at = new Date(epoch);
  const hours = pad(at.getHours());
  const minutes = pad(at.getMinutes());
  const seconds = pad(at.getSeconds());
  if (kind === 'time') {
    return `${hours}:${minutes}:${seconds}`;
  }
  const day = `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}`;
  return kind === 'date' ? day : `${day}T${hours}:${minutes}:${seconds}`;
}

function pad(value: number): string {
  return value < 10 ? `0${value}` : String(value);
}
