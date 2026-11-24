/**
 * Text-to-typed-value parsers shared across schema inference and in-memory
 * query evaluation so column typing and filter comparisons never diverge.
 */

const ISO_DATE_ONLY_RE = /^(\d{4})-(\d{2})-(\d{2})$/;
const ISO_NAIVE_DATETIME_RE =
  /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,9}))?)?$/;
const ISO_ZONED_DATETIME_RE =
  /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})$/i;

/** `01-JUL-2026 00:00:00` / `29-JUL-2026`. */
const DD_MON_YYYY_RE =
  /^(\d{1,2})-([A-Za-z]{3})-(\d{4})(?:[ T](\d{1,2}):(\d{2})(?::(\d{2}))?)?$/;

/**
 * `31.08.2026` / `31.08.2026 14:30` — unambiguous dotted day-month-year dates.
 * Slash-separated dates (`08/31/2026` vs `31/08/2026`) are intentionally not
 * guessed because month/day order is locale-ambiguous.
 */
const DD_MM_YYYY_RE =
  /^(\d{1,2})\.(\d{1,2})\.(\d{4})(?:[ T](\d{1,2}):(\d{2})(?::(\d{2}))?)?$/;

const MONTHS: Readonly<Record<string, number>> = {
  jan: 1,
  feb: 2,
  mar: 3,
  apr: 4,
  may: 5,
  jun: 6,
  jul: 7,
  aug: 8,
  sep: 9,
  oct: 10,
  nov: 11,
  dec: 12,
};

const TRUE_TOKENS = new Set(['true', 'yes', 'y']);
const FALSE_TOKENS = new Set(['false', 'no', 'n']);

const zoneFormatters = new Map<string, Intl.DateTimeFormat>();

/** True when a cell string holds only whitespace. */
export function isBlank(text: string): boolean {
  return text.trim().length === 0;
}

/**
 * Parses a plain numeric string without locale-dependent thousand separators.
 * Plain integers exceeding `Number.MAX_SAFE_INTEGER` return `null` so long
 * numeric identifiers (such as 19-digit ICCIDs) remain lossless strings.
 */
export function parseNumber(text: string): number | null {
  const trimmed = text.trim();
  if (trimmed.length === 0) {
    return null;
  }
  if (!/^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$/.test(trimmed)) {
    return null;
  }
  const value = Number(trimmed);
  if (!Number.isFinite(value)) {
    return null;
  }
  const isPlainInteger = !/[.eE]/.test(trimmed);
  if (isPlainInteger && !Number.isSafeInteger(value)) {
    return null;
  }
  return value;
}

/**
 * Parses a date/datetime string into epoch milliseconds.
 *
 * When `timeZone` is omitted, zone-less strings (`2026-01-10` uses UTC midnight
 * when bare ISO date or local wall-clock when `timeZone` is supplied; naive
 * `YYYY-MM-DDTHH:mm:ss`, `DD.MM.YYYY`, and `DD-MON-YYYY` use `timeZone` or
 * local time) preserve backward compatibility with existing spreadsheet/API
 * tests while supporting explicit IANA zones.
 */
export function parseDate(text: string, timeZone?: string): number | null {
  const trimmed = text.trim();
  if (trimmed.length === 0) {
    return null;
  }

  const normalizedZone = normalizeZoneName(timeZone);

  if (ISO_ZONED_DATETIME_RE.test(trimmed)) {
    const parsed = Date.parse(trimmed.replace(' ', 'T'));
    return Number.isNaN(parsed) ? null : parsed;
  }

  const isoDateOnly = ISO_DATE_ONLY_RE.exec(trimmed);
  if (isoDateOnly) {
    return validatedZoneInstant(
      Number(isoDateOnly[1]),
      Number(isoDateOnly[2]),
      Number(isoDateOnly[3]),
      0,
      0,
      0,
      0,
      normalizedZone,
    );
  }

  const isoNaive = ISO_NAIVE_DATETIME_RE.exec(trimmed);
  if (isoNaive) {
    const msRaw = isoNaive[7] ? isoNaive[7].slice(0, 3).padEnd(3, '0') : '0';
    return validatedZoneInstant(
      Number(isoNaive[1]),
      Number(isoNaive[2]),
      Number(isoNaive[3]),
      Number(isoNaive[4]),
      Number(isoNaive[5]),
      Number(isoNaive[6] ?? 0),
      Number(msRaw),
      normalizedZone,
    );
  }

  const ddMonYyyy = DD_MON_YYYY_RE.exec(trimmed);
  if (ddMonYyyy) {
    const month = MONTHS[ddMonYyyy[2].toLowerCase()];
    if (month === undefined) {
      return null;
    }
    return validatedZoneInstant(
      Number(ddMonYyyy[3]),
      month,
      Number(ddMonYyyy[1]),
      Number(ddMonYyyy[4] ?? 0),
      Number(ddMonYyyy[5] ?? 0),
      Number(ddMonYyyy[6] ?? 0),
      0,
      normalizedZone,
    );
  }

  const ddMmYyyy = DD_MM_YYYY_RE.exec(trimmed);
  if (ddMmYyyy) {
    return validatedZoneInstant(
      Number(ddMmYyyy[3]),
      Number(ddMmYyyy[2]),
      Number(ddMmYyyy[1]),
      Number(ddMmYyyy[4] ?? 0),
      Number(ddMmYyyy[5] ?? 0),
      Number(ddMmYyyy[6] ?? 0),
      0,
      normalizedZone,
    );
  }

  return null;
}

/**
 * Parses word booleans (`true`/`false`/`yes`/`no`/`y`/`n`). Numeric `0`/`1`
 * stay in the number branch so flag columns remain summable.
 */
export function parseBoolean(text: string): boolean | null {
  const token = text.trim().toLowerCase();
  if (TRUE_TOKENS.has(token)) return true;
  if (FALSE_TOKENS.has(token)) return false;
  return null;
}

export interface IWallClock {
  year: number;
  month: number; // 1..12
  day: number; // 1..31
  hour: number; // 0..23
  minute: number; // 0..59
  second: number; // 0..59
  millisecond: number; // 0..999
}

export function normalizeZoneName(timeZone?: string): string | undefined {
  if (timeZone === undefined) {
    return undefined;
  }
  const trimmed = timeZone.trim();
  if (trimmed.length === 0) {
    return undefined;
  }
  getZoneFormatter(trimmed);
  return trimmed;
}

export function toWallClock(epochMs: number, timeZone?: string): IWallClock {
  if (!timeZone) {
    const d = new Date(epochMs);
    return {
      year: d.getFullYear(),
      month: d.getMonth() + 1,
      day: d.getDate(),
      hour: d.getHours(),
      minute: d.getMinutes(),
      second: d.getSeconds(),
      millisecond: d.getMilliseconds(),
    };
  }

  const parts = getZoneFormatter(timeZone).formatToParts(new Date(epochMs));
  let year = 1970;
  let month = 1;
  let day = 1;
  let hour = 0;
  let minute = 0;
  let second = 0;

  for (const part of parts) {
    switch (part.type) {
      case 'year':
        year = Number(part.value);
        break;
      case 'month':
        month = Number(part.value);
        break;
      case 'day':
        day = Number(part.value);
        break;
      case 'hour': {
        const parsed = Number(part.value);
        hour = parsed === 24 ? 0 : parsed;
        break;
      }
      case 'minute':
        minute = Number(part.value);
        break;
      case 'second':
        second = Number(part.value);
        break;
      default:
        break;
    }
  }

  const millisecond = ((Math.trunc(epochMs) % 1000) + 1000) % 1000;
  return { year, month, day, hour, minute, second, millisecond };
}

export function fromWallClock(clock: IWallClock, timeZone?: string): number {
  if (!timeZone) {
    const d = new Date(0);
    d.setFullYear(clock.year, clock.month - 1, clock.day);
    d.setHours(clock.hour, clock.minute, clock.second, clock.millisecond);
    return d.getTime();
  }

  const utcGuess = wallClockAsUtcMs(clock);
  const offset1 = wallClockAsUtcMs(toWallClock(utcGuess, timeZone)) - utcGuess;
  const candidate1 = utcGuess - offset1;
  const offset2 =
    wallClockAsUtcMs(toWallClock(candidate1, timeZone)) - candidate1;
  return utcGuess - offset2;
}

export function daysInMonth(year: number, month: number): number {
  const d = new Date(0);
  d.setUTCFullYear(year, month, 0);
  d.setUTCHours(0, 0, 0, 0);
  return d.getUTCDate();
}

export function wallClockAsUtcMs(clock: IWallClock): number {
  const d = new Date(0);
  d.setUTCFullYear(clock.year, clock.month - 1, clock.day);
  d.setUTCHours(clock.hour, clock.minute, clock.second, clock.millisecond);
  return d.getTime();
}

function validatedZoneInstant(
  year: number,
  month: number,
  day: number,
  hour: number,
  minute: number,
  second: number,
  millisecond: number,
  timeZone?: string,
): number | null {
  if (!isValidCalendarDate(year, month, day)) {
    return null;
  }
  if (
    hour < 0 ||
    hour > 23 ||
    minute < 0 ||
    minute > 59 ||
    second < 0 ||
    second > 59 ||
    millisecond < 0 ||
    millisecond > 999
  ) {
    return null;
  }
  return fromWallClock(
    { year, month, day, hour, minute, second, millisecond },
    timeZone,
  );
}

function isValidCalendarDate(year: number, month: number, day: number): boolean {
  return month >= 1 && month <= 12 && day >= 1 && day <= daysInMonth(year, month);
}

function getZoneFormatter(timeZone: string): Intl.DateTimeFormat {
  const cached = zoneFormatters.get(timeZone);
  if (cached) {
    return cached;
  }
  let formatter: Intl.DateTimeFormat;
  try {
    formatter = new Intl.DateTimeFormat('en-US', {
      timeZone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hourCycle: 'h23',
    });
  } catch {
    throw new Error(`Unknown time zone "${timeZone}"`);
  }
  zoneFormatters.set(timeZone, formatter);
  return formatter;
}
