import type { RelativeDateValue } from '../types';

/**
 * Resolves a `relativeDate` filter value (Query Contract 1.5.0, including
 * `snapEdge: 'start' | 'end'` and post-snap `offset`) to epoch milliseconds in
 * local time, matching how {@link parseDate} reads zone-less cells.
 */
export function resolveRelativeDate(
  value: RelativeDateValue,
  weekStart = 1,
  now: Date = new Date(),
): number {
  const base = new Date(now.getTime());
  if ((value.from ?? 'now') === 'today') {
    base.setHours(0, 0, 0, 0);
  }

  const amount = Number(value.amount);
  if (Number.isFinite(amount) && amount !== 0) {
    addUnits(base, value.unit, amount);
  } else {
    validateUnit(value.unit);
  }

  let toEnd = false;
  if (value.snapTo !== undefined) {
    const edge = value.snapEdge ?? 'start';
    if (edge !== 'start' && edge !== 'end') {
      throw new Error(`Unsupported relativeDate snapEdge "${String(edge)}"`);
    }
    toEnd = edge === 'end';
    snapToUnit(base, value.snapTo, weekStart);
    if (toEnd) {
      addUnits(base, value.snapTo, 1);
    }
  } else if (value.snapEdge !== undefined) {
    throw new Error('relativeDate snapEdge requires snapTo');
  }

  if (value.offset !== undefined) {
    validateUnit(value.offset.unit);
    const offsetAmount = Number(value.offset.amount);
    if (Number.isFinite(offsetAmount) && offsetAmount !== 0) {
      addUnits(base, value.offset.unit, offsetAmount);
    }
  }

  return toEnd ? base.getTime() - 1 : base.getTime();
}

export function resolveNowAnchor(
  anchor: 'now' | 'today' = 'now',
  now: Date = new Date(),
): number {
  if (anchor === 'today') {
    const midnight = new Date(now.getTime());
    midnight.setHours(0, 0, 0, 0);
    return midnight.getTime();
  }
  return now.getTime();
}

export function computeDateDiff(
  unit: string,
  startMs: number,
  endMs: number,
  weekStart = 1,
): number {
  const start = new Date(startMs);
  const end = new Date(endMs);
  switch (unit) {
    case 'year':
      return end.getFullYear() - start.getFullYear();
    case 'quarter':
      return (
        end.getFullYear() * 4 +
        Math.floor(end.getMonth() / 3) -
        (start.getFullYear() * 4 + Math.floor(start.getMonth() / 3))
      );
    case 'month':
      return (
        (end.getFullYear() - start.getFullYear()) * 12 +
        (end.getMonth() - start.getMonth())
      );
    case 'week': {
      const s = new Date(startMs);
      const e = new Date(endMs);
      snapToUnit(s, 'week', weekStart);
      snapToUnit(e, 'week', weekStart);
      return Math.round((e.getTime() - s.getTime()) / (7 * 86_400_000));
    }
    case 'day': {
      const sUtc = Date.UTC(
        start.getFullYear(),
        start.getMonth(),
        start.getDate(),
      );
      const eUtc = Date.UTC(end.getFullYear(), end.getMonth(), end.getDate());
      return Math.round((eUtc - sUtc) / 86_400_000);
    }
    case 'hour':
      return Math.trunc(endMs / 3_600_000) - Math.trunc(startMs / 3_600_000);
    case 'minute':
      return Math.trunc(endMs / 60_000) - Math.trunc(startMs / 60_000);
    case 'second':
      return Math.trunc(endMs / 1_000) - Math.trunc(startMs / 1_000);
    default:
      throw new Error(`Unsupported dateDiff unit "${unit}"`);
  }
}

function validateUnit(unit: string): void {
  switch (unit) {
    case 'year':
    case 'quarter':
    case 'month':
    case 'week':
    case 'day':
    case 'hour':
    case 'minute':
    case 'second':
      return;
    default:
      throw new Error(`Unsupported relativeDate unit "${unit}"`);
  }
}

function addUnits(date: Date, unit: string, amount: number): void {
  switch (unit) {
    case 'year':
      shiftMonthsClamped(date, amount * 12);
      return;
    case 'quarter':
      shiftMonthsClamped(date, amount * 3);
      return;
    case 'month':
      shiftMonthsClamped(date, amount);
      return;
    case 'week':
      date.setDate(date.getDate() + amount * 7);
      return;
    case 'day':
      date.setDate(date.getDate() + amount);
      return;
    case 'hour':
      date.setHours(date.getHours() + amount);
      return;
    case 'minute':
      date.setMinutes(date.getMinutes() + amount);
      return;
    case 'second':
      date.setSeconds(date.getSeconds() + amount);
      return;
    default:
      throw new Error(`Unsupported relativeDate unit "${unit}"`);
  }
}

function shiftMonthsClamped(date: Date, months: number): void {
  const day = date.getDate();
  date.setDate(1);
  date.setMonth(date.getMonth() + months);
  const maxDay = new Date(date.getFullYear(), date.getMonth() + 1, 0).getDate();
  date.setDate(Math.min(day, maxDay));
}

function snapToUnit(date: Date, unit: string, weekStart: number): void {
  switch (unit) {
    case 'year':
      date.setMonth(0, 1);
      date.setHours(0, 0, 0, 0);
      return;
    case 'quarter':
      date.setMonth(Math.floor(date.getMonth() / 3) * 3, 1);
      date.setHours(0, 0, 0, 0);
      return;
    case 'week': {
      const diff = (7 + (date.getDay() - weekStart)) % 7;
      date.setDate(date.getDate() - diff);
      date.setHours(0, 0, 0, 0);
      return;
    }
    case 'month':
      date.setDate(1);
      date.setHours(0, 0, 0, 0);
      return;
    case 'day':
      date.setHours(0, 0, 0, 0);
      return;
    case 'hour':
      date.setMinutes(0, 0, 0);
      return;
    case 'minute':
      date.setSeconds(0, 0);
      return;
    case 'second':
      date.setMilliseconds(0);
      return;
    default:
      throw new Error(`Unsupported relativeDate snapTo "${unit}"`);
  }
}
