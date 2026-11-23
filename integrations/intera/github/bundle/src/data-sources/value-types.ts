/**
 * Text → typed value conversions shared by schema inference and query evaluation
 * so date, number, and boolean parsing stay consistent everywhere.
 */

const ISO_DATE_RE =
  /^\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?)?$/;

/** `01-JUL-2026 00:00:00` / `29-JUL-2026` — what Oracle-style exports emit. */
const DD_MON_YYYY_RE =
  /^(\d{1,2})-([A-Za-z]{3})-(\d{4})(?:[ T](\d{1,2}):(\d{2})(?::(\d{2}))?)?$/;

/**
 * `31.08.2026` / `31.08.2026 14:30` — the dotted form a Russian or European
 * hand types straight into a cell, and the one that turns up mixed into a
 * column of real dates (measured on a live sheet: 9 such cells among 76 proper
 * date cells in one column).
 *
 * Dots only. `08/31/2026` and `31/08/2026` are the same seven characters in two
 * countries, so accepting slashes would silently move every date whose day is
 * also a valid month — exactly the dates nobody would check.
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

/**
 * A cell that carries no value. Distinguished from the string "0" and from a
 * literal "null" text, which stays a string.
 */
export function isBlank(text: string): boolean {
  return text.trim().length === 0;
}

/**
 * Numbers are accepted in the plain form only — digits, one optional sign, one
 * optional decimal point, optional exponent. Thousands separators are NOT
 * stripped: `1,234` in a comma-delimited file is two cells, and guessing that
 * `1 234` is a number would make column typing depend on the locale of
 * whoever produced the file.
 *
 * A plain integer beyond `Number.MAX_SAFE_INTEGER` is REFUSED, so the column
 * stays text and keeps its digits. Identifier columns are what make this
 * matter: an ICCID like `8988169326005008200` becomes `8988169326005008000`
 * the moment it is read as a double, and it is then silently the wrong value
 * in every match key it takes part in. Floats are left alone — they are
 * approximate by construction, and a `47.5` price column must stay summable.
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

/** Epoch milliseconds, or null when the text is not one of the known formats. */
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
 * The parts as a local-time instant, or null when they do not describe a real
 * date.
 *
 * Local time matches how whoever wrote the value meant it — the same
 * assumption `new Date('2026-08-01T00:00:00')` makes for the ISO form without a
 * zone. The validation is the point of the helper: `new Date(2026, 12, 31)`
 * quietly rolls over into the next year, so `31.13.2026` would otherwise read
 * as a perfectly good January date, and `31.02.2026` as the 3rd of March.
 */
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

const TRUE_TOKENS = new Set(['true', 'yes', 'y']);
const FALSE_TOKENS = new Set(['false', 'no', 'n']);

/**
 * Booleans are only recognised from words. `0`/`1` are deliberately left to
 * the number branch — a column of flags is far more useful as a number that
 * can be summed than as a boolean that cannot.
 */
export function parseBoolean(text: string): boolean | null {
  const token = text.trim().toLowerCase();
  if (TRUE_TOKENS.has(token)) return true;
  if (FALSE_TOKENS.has(token)) return false;
  return null;
}
