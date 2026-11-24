/**
 * Shared cell-text parsers used by both schema type inference and query
 * evaluation so column typing and filter comparisons never diverge.
 */

const ISO_DATE_RE =
  /^\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?)?$/;

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

const MONTHS: Record<string, number> = {
  jan: 0,
  feb: 1,
  mar: 2,
  apr: 3,
  may: 4,
  jun: 5,
  jul: 6,
  aug: 7,
  sep: 8,
  oct: 9,
  nov: 10,
  dec: 11,
};

const TRUE_TOKENS = new Set(['true', 'yes', 'y']);
const FALSE_TOKENS = new Set(['false', 'no', 'n']);

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

/** Epoch milliseconds, or `null` when the text is not a recognized date format. */
export function parseDate(text: string): number | null {
  const trimmed = text.trim();
  if (trimmed.length === 0) {
    return null;
  }

  const ddMonYyyy = DD_MON_YYYY_RE.exec(trimmed);
  if (ddMonYyyy) {
    const month = MONTHS[ddMonYyyy[2].toLowerCase()];
    if (month === undefined) {
      return null;
    }
    return localInstant(
      Number(ddMonYyyy[3]),
      month,
      Number(ddMonYyyy[1]),
      Number(ddMonYyyy[4] ?? 0),
      Number(ddMonYyyy[5] ?? 0),
      Number(ddMonYyyy[6] ?? 0),
    );
  }

  const ddMmYyyy = DD_MM_YYYY_RE.exec(trimmed);
  if (ddMmYyyy) {
    return localInstant(
      Number(ddMmYyyy[3]),
      Number(ddMmYyyy[2]) - 1,
      Number(ddMmYyyy[1]),
      Number(ddMmYyyy[4] ?? 0),
      Number(ddMmYyyy[5] ?? 0),
      Number(ddMmYyyy[6] ?? 0),
    );
  }

  if (ISO_DATE_RE.test(trimmed)) {
    const parsed = Date.parse(trimmed);
    return Number.isNaN(parsed) ? null : parsed;
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

function localInstant(
  year: number,
  month: number,
  day: number,
  hours: number,
  minutes: number,
  seconds: number,
): number | null {
  if (month < 0 || month > 11 || day < 1 || day > 31) return null;
  if (hours > 23 || minutes > 59 || seconds > 59) return null;

  const date = new Date(year, month, day, hours, minutes, seconds);
  if (
    date.getFullYear() !== year ||
    date.getMonth() !== month ||
    date.getDate() !== day
  ) {
    return null;
  }
  const time = date.getTime();
  return Number.isNaN(time) ? null : time;
}
