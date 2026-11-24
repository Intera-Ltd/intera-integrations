/**
 * Classifies Excel `numFmtId` and `formatCode` definitions into date, dateTime,
 * or time-of-day categories (ECMA-376 Part 1, §18.8.30).
 */

export type TDateKind = 'date' | 'dateTime' | 'time';

/** Built-in Excel date/time format IDs (excluding elapsed duration 46 `[h]:mm:ss`). */
const BUILT_IN_DATE_KINDS = new Map<number, TDateKind>([
  [14, 'date'],
  [15, 'date'],
  [16, 'date'],
  [17, 'date'],
  [18, 'time'],
  [19, 'time'],
  [20, 'time'],
  [21, 'time'],
  [22, 'dateTime'],
  [45, 'time'],
  [47, 'time'],
  [27, 'date'],
  [28, 'date'],
  [29, 'date'],
  [30, 'date'],
  [31, 'date'],
  [32, 'time'],
  [33, 'time'],
  [34, 'time'],
  [35, 'time'],
  [36, 'date'],
  [50, 'date'],
  [51, 'date'],
  [52, 'date'],
  [53, 'date'],
  [54, 'date'],
  [55, 'date'],
  [56, 'date'],
  [57, 'date'],
  [58, 'date'],
]);

export function classifyNumberFormat(
  numFmtId: number,
  formatCode: string | undefined,
): TDateKind | null {
  if (formatCode !== undefined) {
    return classifyFormatCode(formatCode);
  }
  return BUILT_IN_DATE_KINDS.get(numFmtId) ?? null;
}

/**
 * Classifies an Excel number-format string. Elapsed-time formats (`[h]:mm`,
 * `[mm]:ss`) represent durations rather than wall-clock times and return `null`
 * so their numeric day fractions remain summable.
 */
export function classifyFormatCode(formatCode: string): TDateKind | null {
  const section = firstSection(formatCode);
  if (/\[(?:h+|m+|s+)\]/i.test(section)) {
    return null;
  }

  const tokens = section
    .replace(/"[^"]*"/g, '')
    .replace(/\\./g, '')
    .replace(/[_*]./g, '')
    .replace(/\[[^\]]*\]/g, '')
    .toLowerCase()
    .replace(/general/g, '')
    .replace(/am\/pm|a\/p/g, '');

  const hasYearOrDay = /[yd]/.test(tokens);
  const hasTime = /[hs]/.test(tokens);
  const hasM = tokens.includes('m');

  const isDate = hasYearOrDay || (hasM && !hasTime);
  if (isDate && hasTime) return 'dateTime';
  if (isDate) return 'date';
  if (hasTime) return 'time';
  return null;
}

function firstSection(formatCode: string): string {
  let inQuotes = false;
  let inBrackets = false;
  for (let index = 0; index < formatCode.length; index++) {
    const char = formatCode[index];
    if (char === '\\' && !inQuotes) {
      index++;
    } else if (char === '"') {
      inQuotes = !inQuotes;
    } else if (!inQuotes && char === '[') {
      inBrackets = true;
    } else if (!inQuotes && char === ']') {
      inBrackets = false;
    } else if (!inQuotes && !inBrackets && char === ';') {
      return formatCode.slice(0, index);
    }
  }
  return formatCode;
}
