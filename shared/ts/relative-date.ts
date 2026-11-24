import type {
  RelativeDateSnapEdge,
  RelativeDateUnit,
  RelativeDateValue,
} from './contract-types';
import {
  daysInMonth,
  fromWallClock,
  normalizeZoneName,
  toWallClock,
  wallClockAsUtcMs,
  type IWallClock,
} from './value-types';

export interface IRelativeDateOptions {
  now?: Date;
  timeZone?: string;
  weekStart?: number | string;
}

/**
 * Half-open window `[start, endExclusive)` when a `relativeDate` names a
 * calendar day (`from: 'today'` without sub-day units) or a whole period
 * (`snapEdge: 'whole'`), or a point instant (`endExclusive === null`) when
 * anchored on `now` or sub-day units.
 */
export interface IRelativeDateRange {
  start: number;
  endExclusive: number | null;
}

const WEEKDAY_NAMES: Readonly<Record<string, number>> = {
  sunday: 0,
  monday: 1,
  tuesday: 2,
  wednesday: 3,
  thursday: 4,
  friday: 5,
  saturday: 6,
};

/**
 * Resolves a `relativeDate` filter value to a single epoch-millisecond point
 * (backward-compatible with callers expecting one timestamp).
 */
export function resolveRelativeDate(
  value: RelativeDateValue,
  optionsOrWeekStart: IRelativeDateOptions | number = 1,
  now?: Date,
): number {
  const { start, endExclusive, edge } = computeRelativeDateCore(
    value,
    normalizeOptions(optionsOrWeekStart, now),
  );
  if (edge === 'end' && endExclusive !== null) {
    return endExclusive - 1;
  }
  return start;
}

/**
 * Resolves a `relativeDate` filter value into either a half-open calendar
 * range `[start, endExclusive)` (`snapEdge: 'whole'` or `from: 'today'`
 * without sub-day units) or a point instant (`endExclusive === null`).
 */
export function resolveRelativeDateRange(
  value: RelativeDateValue,
  optionsOrWeekStart: IRelativeDateOptions | number = 1,
  now?: Date,
): IRelativeDateRange {
  const { start, endExclusive } = computeRelativeDateCore(
    value,
    normalizeOptions(optionsOrWeekStart, now),
  );
  return { start, endExclusive };
}

export function resolveNowAnchor(
  anchor: 'now' | 'today' = 'now',
  now: Date = new Date(),
  timeZone?: string,
): number {
  const zone = normalizeZoneName(timeZone);
  if (anchor === 'today') {
    const clock = toWallClock(now.getTime(), zone);
    return fromWallClock(
      {
        year: clock.year,
        month: clock.month,
        day: clock.day,
        hour: 0,
        minute: 0,
        second: 0,
        millisecond: 0,
      },
      zone,
    );
  }
  if (anchor !== 'now') {
    throw new Error(`Unsupported now anchor "${String(anchor)}"`);
  }
  return now.getTime();
}

export function computeDateDiff(
  unit: string,
  startMs: number,
  endMs: number,
  optionsOrWeekStart: IRelativeDateOptions | number = 1,
): number {
  const options = normalizeOptions(optionsOrWeekStart);
  const zone = normalizeZoneName(options.timeZone);
  const weekStart = parseWeekStart(options.weekStart);
  const canonicalUnit = normalizeUnit(unit, 'dateDiff unit');
  const start = toWallClock(startMs, zone);
  const end = toWallClock(endMs, zone);

  switch (canonicalUnit) {
    case 'year':
      return end.year - start.year;
    case 'quarter':
      return (
        end.year * 4 +
        Math.floor((end.month - 1) / 3) -
        (start.year * 4 + Math.floor((start.month - 1) / 3))
      );
    case 'month':
      return end.year * 12 + end.month - (start.year * 12 + start.month);
    case 'week': {
      const sWeek = startOfWeekClock(start, weekStart);
      const eWeek = startOfWeekClock(end, weekStart);
      return Math.round(
        (wallClockUtcDayMs(eWeek) - wallClockUtcDayMs(sWeek)) / (7 * 86_400_000),
      );
    }
    case 'day':
      return Math.round(
        (wallClockUtcDayMs(end) - wallClockUtcDayMs(start)) / 86_400_000,
      );
    case 'hour':
      return Math.trunc(
        (Date.UTC(end.year, end.month - 1, end.day, end.hour) -
          Date.UTC(start.year, start.month - 1, start.day, start.hour)) /
          3_600_000,
      );
    case 'minute':
      return Math.trunc(
        (Date.UTC(end.year, end.month - 1, end.day, end.hour, end.minute) -
          Date.UTC(
            start.year,
            start.month - 1,
            start.day,
            start.hour,
            start.minute,
          )) /
          60_000,
      );
    case 'second':
      return Math.trunc(
        (Date.UTC(
          end.year,
          end.month - 1,
          end.day,
          end.hour,
          end.minute,
          end.second,
        ) -
          Date.UTC(
            start.year,
            start.month - 1,
            start.day,
            start.hour,
            start.minute,
            start.second,
          )) /
          1_000,
      );
  }
}

export function parseWeekStart(weekStart?: number | string): number {
  if (weekStart === undefined) {
    return 1;
  }
  if (typeof weekStart === 'number') {
    if (Number.isInteger(weekStart) && weekStart >= 0 && weekStart <= 6) {
      return weekStart;
    }
    throw new Error(`Unknown first day of week "${String(weekStart)}"`);
  }
  const normalized = weekStart.trim().toLowerCase();
  if (normalized.length === 0) {
    return 1;
  }
  const mapped = WEEKDAY_NAMES[normalized];
  if (mapped !== undefined) {
    return mapped;
  }
  throw new Error(`Unknown first day of week "${weekStart}"`);
}

function normalizeOptions(
  optionsOrWeekStart: IRelativeDateOptions | number | undefined,
  now?: Date,
): IRelativeDateOptions {
  if (typeof optionsOrWeekStart === 'number') {
    return { weekStart: optionsOrWeekStart, ...(now ? { now } : {}) };
  }
  if (optionsOrWeekStart === undefined) {
    return now ? { now } : {};
  }
  return now && !optionsOrWeekStart.now
    ? { ...optionsOrWeekStart, now }
    : optionsOrWeekStart;
}

function computeRelativeDateCore(
  value: RelativeDateValue,
  options: IRelativeDateOptions,
): {
  start: number;
  endExclusive: number | null;
  edge: RelativeDateSnapEdge;
} {
  const from = value.from ?? 'now';
  if (from !== 'now' && from !== 'today') {
    throw new Error(`Unsupported relativeDate from "${String(from)}"`);
  }

  const zone = normalizeZoneName(options.timeZone);
  const weekStart = parseWeekStart(options.weekStart);
  const nowInstant = (options.now ?? new Date()).getTime();
  const nowClock = toWallClock(nowInstant, zone);
  const isFromToday = from === 'today';

  const baseClock: IWallClock = isFromToday
    ? {
        year: nowClock.year,
        month: nowClock.month,
        day: nowClock.day,
        hour: 0,
        minute: 0,
        second: 0,
        millisecond: 0,
      }
    : nowClock;

  const canonicalUnit = normalizeUnit(value.unit, 'unit');
  const amount = Number(value.amount);
  const shifted =
    Number.isFinite(amount) && amount !== 0
      ? addWallClockUnit(baseClock, canonicalUnit, Math.trunc(amount))
      : baseClock;

  let canonicalSnapUnit: RelativeDateUnit | null = null;
  let edge: RelativeDateSnapEdge = 'start';
  let snapped = shifted;

  if (value.snapTo !== undefined && value.snapTo.trim().length > 0) {
    canonicalSnapUnit = normalizeUnit(value.snapTo, 'snapTo');
    edge = parseSnapEdge(value.snapEdge);
    snapped = snapWallClockStart(shifted, canonicalSnapUnit, weekStart);
    if (edge === 'end') {
      snapped = addWallClockUnit(snapped, canonicalSnapUnit, 1);
    }
  } else if (value.snapEdge !== undefined) {
    throw new Error('relativeDate snapEdge requires snapTo');
  }

  let canonicalOffsetUnit: RelativeDateUnit | null = null;
  let offsetAmount = 0;
  if (value.offset !== undefined) {
    canonicalOffsetUnit = normalizeUnit(value.offset.unit, 'offset.unit');
    const parsedOffset = Number(value.offset.amount);
    if (Number.isFinite(parsedOffset) && parsedOffset !== 0) {
      offsetAmount = Math.trunc(parsedOffset);
      snapped = addWallClockUnit(snapped, canonicalOffsetUnit, offsetAmount);
    }
  }

  if (edge === 'whole' && canonicalSnapUnit !== null) {
    const unoffsetStart = snapWallClockStart(shifted, canonicalSnapUnit, weekStart);
    let endClock = addWallClockUnit(unoffsetStart, canonicalSnapUnit, 1);
    if (offsetAmount !== 0 && canonicalOffsetUnit !== null) {
      endClock = addWallClockUnit(endClock, canonicalOffsetUnit, offsetAmount);
    }
    return {
      start: fromWallClock(snapped, zone),
      endExclusive: fromWallClock(endClock, zone),
      edge,
    };
  }

  const hasSubDay =
    isSubDayUnit(canonicalUnit) ||
    (canonicalSnapUnit !== null && isSubDayUnit(canonicalSnapUnit)) ||
    (canonicalOffsetUnit !== null &&
      offsetAmount !== 0 &&
      isSubDayUnit(canonicalOffsetUnit));

  if (isFromToday && !hasSubDay) {
    if (edge === 'end') {
      const dayStart = addWallClockUnit(snapped, 'day', -1);
      return {
        start: fromWallClock(dayStart, zone),
        endExclusive: fromWallClock(snapped, zone),
        edge,
      };
    }
    const nextDay = addWallClockUnit(snapped, 'day', 1);
    return {
      start: fromWallClock(snapped, zone),
      endExclusive: fromWallClock(nextDay, zone),
      edge,
    };
  }

  if (edge === 'end') {
    return {
      start: fromWallClock(snapped, zone) - 1,
      endExclusive: null,
      edge: 'start',
    };
  }

  return {
    start: fromWallClock(snapped, zone),
    endExclusive: null,
    edge,
  };
}

function normalizeUnit(unit: string, label: string): RelativeDateUnit {
  switch (String(unit ?? '').trim().toLowerCase()) {
    case 'year':
    case 'yy':
    case 'yyyy':
      return 'year';
    case 'quarter':
    case 'qq':
    case 'q':
      return 'quarter';
    case 'month':
    case 'mm':
    case 'm':
      return 'month';
    case 'week':
    case 'wk':
    case 'ww':
      return 'week';
    case 'day':
    case 'dd':
    case 'd':
      return 'day';
    case 'hour':
    case 'hh':
      return 'hour';
    case 'minute':
    case 'mi':
    case 'n':
      return 'minute';
    case 'second':
    case 'ss':
    case 's':
      return 'second';
    default:
      throw new Error(`Unsupported relativeDate ${label} "${String(unit)}"`);
  }
}

function parseSnapEdge(snapEdge?: string): RelativeDateSnapEdge {
  if (snapEdge === undefined || snapEdge.trim() === '' || snapEdge === 'start') {
    return 'start';
  }
  if (snapEdge === 'end' || snapEdge === 'whole') {
    return snapEdge;
  }
  throw new Error(
    `Unsupported relativeDate snapEdge "${String(snapEdge)}". Expected "start", "end", or "whole"`,
  );
}

function isSubDayUnit(unit: RelativeDateUnit): boolean {
  return unit === 'hour' || unit === 'minute' || unit === 'second';
}

function addWallClockUnit(
  clock: IWallClock,
  unit: RelativeDateUnit,
  amount: number,
): IWallClock {
  switch (unit) {
    case 'year':
      return shiftMonthsClamped(clock, amount * 12);
    case 'quarter':
      return shiftMonthsClamped(clock, amount * 3);
    case 'month':
      return shiftMonthsClamped(clock, amount);
    case 'week':
      return shiftUtcMillis(clock, amount * 7 * 86_400_000);
    case 'day':
      return shiftUtcMillis(clock, amount * 86_400_000);
    case 'hour':
      return shiftUtcMillis(clock, amount * 3_600_000);
    case 'minute':
      return shiftUtcMillis(clock, amount * 60_000);
    case 'second':
      return shiftUtcMillis(clock, amount * 1_000);
  }
}

function shiftMonthsClamped(clock: IWallClock, totalMonths: number): IWallClock {
  const zeroBasedMonth = clock.year * 12 + (clock.month - 1) + totalMonths;
  const targetYear = Math.floor(zeroBasedMonth / 12);
  const targetMonth = (((zeroBasedMonth % 12) + 12) % 12) + 1;
  const maxDay = daysInMonth(targetYear, targetMonth);
  return {
    ...clock,
    year: targetYear,
    month: targetMonth,
    day: Math.min(clock.day, maxDay),
  };
}

function shiftUtcMillis(clock: IWallClock, deltaMs: number): IWallClock {
  const d = new Date(wallClockAsUtcMs(clock) + deltaMs);
  return {
    year: d.getUTCFullYear(),
    month: d.getUTCMonth() + 1,
    day: d.getUTCDate(),
    hour: d.getUTCHours(),
    minute: d.getUTCMinutes(),
    second: d.getUTCSeconds(),
    millisecond: d.getUTCMilliseconds(),
  };
}

function snapWallClockStart(
  clock: IWallClock,
  unit: RelativeDateUnit,
  weekStart: number,
): IWallClock {
  switch (unit) {
    case 'year':
      return {
        year: clock.year,
        month: 1,
        day: 1,
        hour: 0,
        minute: 0,
        second: 0,
        millisecond: 0,
      };
    case 'quarter':
      return {
        year: clock.year,
        month: Math.floor((clock.month - 1) / 3) * 3 + 1,
        day: 1,
        hour: 0,
        minute: 0,
        second: 0,
        millisecond: 0,
      };
    case 'month':
      return {
        year: clock.year,
        month: clock.month,
        day: 1,
        hour: 0,
        minute: 0,
        second: 0,
        millisecond: 0,
      };
    case 'week':
      return startOfWeekClock(clock, weekStart);
    case 'day':
      return {
        year: clock.year,
        month: clock.month,
        day: clock.day,
        hour: 0,
        minute: 0,
        second: 0,
        millisecond: 0,
      };
    case 'hour':
      return { ...clock, minute: 0, second: 0, millisecond: 0 };
    case 'minute':
      return { ...clock, second: 0, millisecond: 0 };
    case 'second':
      return { ...clock, millisecond: 0 };
  }
}

function startOfWeekClock(clock: IWallClock, weekStart: number): IWallClock {
  const dayMs = wallClockUtcDayMs(clock);
  const weekday = new Date(dayMs).getUTCDay();
  const diff = (7 + (weekday - weekStart)) % 7;
  const d = new Date(dayMs - diff * 86_400_000);
  return {
    year: d.getUTCFullYear(),
    month: d.getUTCMonth() + 1,
    day: d.getUTCDate(),
    hour: 0,
    minute: 0,
    second: 0,
    millisecond: 0,
  };
}

function wallClockUtcDayMs(clock: IWallClock): number {
  const d = new Date(0);
  d.setUTCFullYear(clock.year, clock.month - 1, clock.day);
  d.setUTCHours(0, 0, 0, 0);
  return d.getTime();
}
