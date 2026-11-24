import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { buildSync } from 'esbuild';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const rootDir = path.resolve(__dirname, '../..');
const outFile = path.join(rootDir, 'out', 'test-shared-ts.cjs');

fs.mkdirSync(path.dirname(outFile), { recursive: true });
buildSync({
  entryPoints: [path.join(rootDir, 'shared/ts/index.ts')],
  bundle: true,
  platform: 'node',
  target: 'node20',
  format: 'cjs',
  outfile: outFile,
});

const require = createRequire(import.meta.url);
const shared = require(outFile);

test('resolveRelativeDateRange resolves calendar day and whole-period half-open intervals in IANA zones', () => {
  const fixedNow = new Date('2026-03-18T14:35:20Z');

  const todayUtc = shared.resolveRelativeDateRange(
    { type: 'relativeDate', from: 'today', unit: 'day', amount: 0 },
    { now: fixedNow, timeZone: 'UTC' },
  );
  assert.equal(todayUtc.start, Date.UTC(2026, 2, 18, 0, 0, 0));
  assert.equal(todayUtc.endExclusive, Date.UTC(2026, 2, 19, 0, 0, 0));

  const thisMonthUtc = shared.resolveRelativeDateRange(
    {
      type: 'relativeDate',
      from: 'today',
      unit: 'month',
      amount: 0,
      snapTo: 'month',
      snapEdge: 'whole',
    },
    { now: fixedNow, timeZone: 'UTC' },
  );
  assert.equal(thisMonthUtc.start, Date.UTC(2026, 2, 1, 0, 0, 0));
  assert.equal(thisMonthUtc.endExclusive, Date.UTC(2026, 3, 1, 0, 0, 0));

  // At 2026-03-18T22:30:00Z it is already 2026-03-19 01:30 in Europe/Moscow (+03:00).
  const lateUtc = new Date('2026-03-18T22:30:00Z');
  const todayMoscow = shared.resolveRelativeDateRange(
    { type: 'relativeDate', from: 'today', unit: 'day', amount: 0 },
    { now: lateUtc, timeZone: 'Europe/Moscow' },
  );
  assert.equal(todayMoscow.start, Date.parse('2026-03-19T00:00:00+03:00'));
  assert.equal(todayMoscow.endExclusive, Date.parse('2026-03-20T00:00:00+03:00'));
});

test('executeInMemoryQuery matches non-midnight timestamps on today and snapEdge whole', () => {
  const fixedNow = new Date('2026-03-18T14:35:20Z');
  const schema = shared.buildEvaluationSchema(
    [
      { name: 'id', type: 'number' },
      { name: 'created_at', type: 'date' },
    ],
    1,
    fixedNow,
    'UTC',
  );

  const rows = [
    { id: 1, created_at: '2026-03-17T23:59:59Z' },
    { id: 2, created_at: '2026-03-18T15:10:00Z' },
    { id: 3, created_at: '2026-03-19T00:00:00Z' },
  ];

  const matchedToday = shared.executeInMemoryQuery(
    rows,
    {
      timeZone: 'UTC',
      filter: {
        type: 'condition',
        field: 'created_at',
        operator: '=',
        value: { type: 'relativeDate', from: 'today', unit: 'day', amount: 0 },
      },
    },
    schema,
  );
  assert.deepEqual(
    matchedToday.map((r) => r.id),
    [2],
  );

  const matchedWholeMonth = shared.executeInMemoryQuery(
    rows,
    {
      timeZone: 'UTC',
      filter: {
        type: 'condition',
        field: 'created_at',
        operator: '=',
        value: {
          type: 'relativeDate',
          from: 'today',
          unit: 'month',
          amount: 0,
          snapTo: 'month',
          snapEdge: 'whole',
        },
      },
    },
    schema,
  );
  assert.deepEqual(
    matchedWholeMonth.map((r) => r.id),
    [1, 2, 3],
  );
});

test('analyzeQuery extracts both lower and upper pushdown bounds for whole-period equality', () => {
  const fixedNow = new Date('2026-03-18T14:35:20Z');
  const shape = shared.analyzeQuery(
    {
      timeZone: 'UTC',
      filter: {
        type: 'condition',
        field: 'created_at',
        operator: '=',
        value: {
          type: 'relativeDate',
          from: 'today',
          unit: 'month',
          amount: 0,
          snapTo: 'month',
          snapEdge: 'whole',
        },
      },
    },
    1,
    fixedNow,
    'UTC',
  );

  assert.equal(shape.lowerBound('created_at'), Date.UTC(2026, 2, 1, 0, 0, 0));
  assert.equal(shape.upperBound('created_at'), Date.UTC(2026, 3, 1, 0, 0, 0) - 1);
});

test('normalizeDataQuery lifts groupBy, paging, timeZone, and weekStart', () => {
  const query = shared.normalizeDataQuery({
    expressions: [{ alias: 'total', expression: { type: 'aggregate', action: 'count' } }],
    groupBy: ['status'],
    limit: 25,
    offset: 50,
    orderBy: [{ field: 'status', direction: 'desc' }],
    timeZone: 'Europe/Moscow',
    weekStart: 1,
  });

  assert.deepEqual(query, {
    result: {
      expressions: [{ alias: 'total', expression: { type: 'aggregate', action: 'count' } }],
      groupBy: ['status'],
    },
    top: 25,
    skip: 50,
    order: [{ field: 'status', direction: 'desc' }],
    timeZone: 'Europe/Moscow',
    weekStart: 'Monday',
  });
});
