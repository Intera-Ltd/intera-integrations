import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { afterEach, beforeEach, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const __dirname = path.dirname(fileURLToPath(import.meta.url));

const distBundle = require('../dist/index.js');
const {
  bundle,
  fetchBoundedText,
  clearTokenCache,
  getTokenCacheSize,
  MAX_TOKEN_CACHE_ENTRIES,
  analyzeQuery,
  normalizeFilterNode,
} = require('./dist/harness.cjs');

const KEY_PAIR_A = crypto.generateKeyPairSync('rsa', {
  modulusLength: 2048,
  privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
  publicKeyEncoding: { type: 'spki', format: 'pem' },
});

const KEY_PAIR_B = crypto.generateKeyPairSync('rsa', {
  modulusLength: 2048,
  privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
  publicKeyEncoding: { type: 'spki', format: 'pem' },
});

const SPREADSHEET_ID = '1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgvE2upms';
const SPREADSHEET_URL = `https://docs.google.com/spreadsheets/d/${SPREADSHEET_ID}/edit#gid=100`;

const TEST_YEAR = new Date().getFullYear();
const SHEETS_EPOCH_MS = Date.UTC(1899, 11, 30);
const toSheetsSerial = (year, month, day) =>
  Math.round((Date.UTC(year, month - 1, day) - SHEETS_EPOCH_MS) / 86_400_000);

const ORDERS_VALUES = [
  ['Order ID', 'Customer', 'Ordered On', 'Shipped On', 'Amount', 'Paid', 'Дата заказа'],
  ['ORD-1', 'Acme Corp', toSheetsSerial(TEST_YEAR, 1, 10), toSheetsSerial(TEST_YEAR, 1, 15), 120, true, `10.01.${TEST_YEAR}`],
  ['ORD-2', 'Beta Logistics', toSheetsSerial(TEST_YEAR, 1, 20), toSheetsSerial(TEST_YEAR, 1, 25), 280, false, `20.01.${TEST_YEAR}`],
  ['ORD-3', 'Acme Corp', toSheetsSerial(TEST_YEAR, 1, 25), toSheetsSerial(TEST_YEAR, 1, 20), 60, true, `25.01.${TEST_YEAR}`],
];

const ORDERS_FORMATS = {
  startRow: 1,
  startColumn: 0,
  rowData: [
    {
      values: [
        {},
        {},
        { effectiveFormat: { numberFormat: { type: 'DATE' } } },
        { effectiveFormat: { numberFormat: { type: 'DATE' } } },
        { effectiveFormat: { numberFormat: { type: 'CURRENCY' } } },
        {},
        {},
      ],
    },
    {
      values: [
        {},
        {},
        { effectiveFormat: { numberFormat: { type: 'DATE' } } },
        { effectiveFormat: { numberFormat: { type: 'DATE' } } },
        { effectiveFormat: { numberFormat: { type: 'CURRENCY' } } },
        {},
        {},
      ],
    },
    {
      values: [
        {},
        {},
        { effectiveFormat: { numberFormat: { type: 'DATE' } } },
        { effectiveFormat: { numberFormat: { type: 'DATE' } } },
        { effectiveFormat: { numberFormat: { type: 'CURRENCY' } } },
        {},
        {},
      ],
    },
  ],
};

const INVENTORY_VALUES = [
  ['SKU', 'Stock', 'Warehouse'],
  ['SKU-10', 40, 'North'],
  ['SKU-20', 12, 'South'],
];

const INVENTORY_FORMATS = {
  startRow: 1,
  startColumn: 0,
  rowData: [
    { values: [{}, { effectiveFormat: { numberFormat: { type: 'NUMBER' } } }, {}] },
    { values: [{}, { effectiveFormat: { numberFormat: { type: 'NUMBER' } } }, {}] },
  ],
};

function makeServiceAccountJson(overrides = {}) {
  return JSON.stringify({
    type: 'service_account',
    client_email: 'intera-reader@test-project.iam.gserviceaccount.com',
    private_key: KEY_PAIR_A.privateKey,
    token_uri: 'https://oauth2.googleapis.com/token',
    ...overrides,
  });
}

function makeConfig(overrides = {}) {
  return {
    spreadsheet: SPREADSHEET_URL,
    service_account_json: makeServiceAccountJson(),
    identity_column: 'order_id',
    ...overrides,
  };
}

const context = (config, extra = {}) => ({
  userId: 'u-1',
  roles: ['admin'],
  config,
  ...extra,
});

const rowsSource = () => bundle.dataSources.find((s) => s.id === 'gsheet-rows');

let calls;
let respond;
let tokenCounter = 0;
const realFetch = globalThis.fetch;

function defaultResponder(url, init) {
  const parsed = new URL(url);
  if (parsed.origin === 'https://oauth2.googleapis.com' && parsed.pathname === '/token') {
    const token = `ya29.secret-google-access-token-${++tokenCounter}`;
    return new Response(
      JSON.stringify({ access_token: token, expires_in: 3600, token_type: 'Bearer' }),
      { status: 200, headers: { 'content-type': 'application/json' } },
    );
  }

  if (parsed.pathname.endsWith('/values:batchGet')) {
    return new Response(
      JSON.stringify({
        valueRanges: [
          { values: ORDERS_VALUES },
          { values: INVENTORY_VALUES },
          { values: [['Header Only']] },
        ],
      }),
      { status: 200, headers: { 'content-type': 'application/json' } },
    );
  }

  if (parsed.pathname.includes('/values/')) {
    const encodedRange = parsed.pathname.split('/values/')[1];
    const decodedRange = decodeURIComponent(encodedRange);
    const values = decodedRange.startsWith("'Inventory'!") ? INVENTORY_VALUES : ORDERS_VALUES;
    return new Response(JSON.stringify({ values }), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    });
  }

  if (parsed.searchParams.get('includeGridData') === 'true') {
    return new Response(
      JSON.stringify({
        sheets: [
          { properties: { sheetId: 100 }, data: [ORDERS_FORMATS] },
          { properties: { sheetId: 202 }, data: [INVENTORY_FORMATS] },
        ],
      }),
      { status: 200, headers: { 'content-type': 'application/json' } },
    );
  }

  // Metadata endpoint
  return new Response(
    JSON.stringify({
      properties: { title: 'Operations Ledger' },
      sheets: [
        {
          properties: {
            title: 'Orders',
            sheetId: 100,
            sheetType: 'GRID',
            gridProperties: { rowCount: 200, columnCount: 7 },
          },
        },
        {
          properties: {
            title: 'Inventory',
            sheetId: 202,
            sheetType: 'GRID',
            gridProperties: { rowCount: 100, columnCount: 3 },
          },
        },
        {
          properties: {
            title: 'EmptyHeaderTab',
            sheetId: 304,
            sheetType: 'GRID',
            gridProperties: { rowCount: 10, columnCount: 2 },
          },
        },
        {
          properties: {
            title: 'Revenue Chart',
            sheetId: 406,
            sheetType: 'OBJECT',
            gridProperties: { rowCount: 0, columnCount: 0 },
          },
        },
      ],
    }),
    { status: 200, headers: { 'content-type': 'application/json' } },
  );
}

beforeEach(() => {
  calls = [];
  clearTokenCache();
  respond = defaultResponder;
  globalThis.fetch = async (url, init) => {
    calls.push({ url: String(url), init });
    return respond(String(url), init);
  };
});

afterEach(() => {
  globalThis.fetch = realFetch;
  clearTokenCache();
});

describe('google-sheets bundle contract', () => {
  it('exports manifest, gsheet-rows dataSource, provisionInstance, testConnection, and built icon asset', () => {
    assert.deepEqual(distBundle.manifest, bundle.manifest);
    assert.equal(typeof distBundle.provisionInstance, 'function');
    assert.equal(typeof distBundle.testConnection, 'function');
    assert.equal(bundle.manifest.id, 'intera-gsheets-integration');
    assert.equal(bundle.manifest.name, 'Google Sheets');
    assert.equal(bundle.manifest.version, '1.3.1');
    assert.deepEqual(bundle.manifest.requires, { core: '>=0.5.25' });
    assert.deepEqual(bundle.manifest.compatibility, { integration_contract: 2 });
    assert.equal(bundle.manifest.iconFile, 'assets/icon.svg');
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.provisionInstance, 'function');
    assert.equal(typeof bundle.testConnection, 'function');
    assert.equal(bundle.dataSources.length, 1);
    assert.equal(rowsSource().id, 'gsheet-rows');

    const iconPath = path.join(__dirname, '..', 'dist', 'assets', 'icon.svg');
    assert.equal(fs.existsSync(iconPath), true, 'dist/assets/icon.svg must be copied on build');
  });

  it('validates config fields and token_uri HTTPS/loopback rules before network calls', async () => {
    await assert.rejects(rowsSource().getData({}, context(undefined)), /config is required/);
    await assert.rejects(rowsSource().getData({}, context({})), /"spreadsheet" is required/);
    await assert.rejects(
      rowsSource().getData({}, context({ spreadsheet: 'short', service_account_json: makeServiceAccountJson() })),
      /spreadsheet id or the URL/,
    );
    await assert.rejects(
      rowsSource().getData(
        {},
        context(makeConfig({ service_account_json: makeServiceAccountJson({ token_uri: 'http://oauth2.googleapis.com/token' }) })),
      ),
      /must use https/,
    );
    await assert.rejects(
      rowsSource().getData(
        {},
        context(makeConfig({ service_account_json: makeServiceAccountJson({ token_uri: 'https://user:pass@oauth2.googleapis.com/token' }) })),
      ),
      /userinfo/,
    );
    await assert.rejects(
      rowsSource().getData(
        {},
        context(makeConfig({ service_account_json: makeServiceAccountJson({ token_uri: 'https://oauth2.googleapis.com/token#frag' }) })),
      ),
      /fragment/,
    );
    assert.equal(calls.length, 0);
  });

  it('supports testConnection and multi-tab provisionInstance with dynamic column inference', async () => {
    const testResult = await bundle.testConnection({
      config: makeConfig(),
      instanceId: null,
      instanceSlug: null,
    });
    assert.equal(testResult.detail, 'Google Sheets: "Operations Ledger", tab "Orders"');

    const provisioned = await bundle.provisionInstance({
      config: makeConfig(),
      instanceId: 'inst-1',
      instanceSlug: 'ops-ledger',
    });

    assert.equal(provisioned.dataSources.length, 2);

    const primary = provisioned.dataSources[0];
    assert.equal(primary.id, 'gsheet-rows');
    assert.equal(primary.name, 'Orders');
    assert.equal(primary.template, undefined);
    assert.deepEqual(
      primary.fields.map((f) => ({ name: f.name, type: f.type, identity: Boolean(f.identity) })),
      [
        { name: 'order_id', type: 'string', identity: true },
        { name: 'customer', type: 'string', identity: false },
        { name: 'ordered_on', type: 'date', identity: false },
        { name: 'shipped_on', type: 'date', identity: false },
        { name: 'amount', type: 'number', identity: false },
        { name: 'paid', type: 'boolean', identity: false },
        { name: 'дата_заказа', type: 'date', identity: false },
      ],
    );

    const secondary = provisioned.dataSources[1];
    assert.equal(secondary.id, 'gsheet-tab-202');
    assert.equal(secondary.name, 'Inventory');
    assert.equal(secondary.template, 'gsheet-rows');
    assert.deepEqual(
      secondary.fields.map((f) => ({ name: f.name, type: f.type, identity: Boolean(f.identity) })),
      [
        { name: 'sku', type: 'string', identity: false },
        { name: 'stock', type: 'number', identity: false },
        { name: 'warehouse', type: 'string', identity: false },
      ],
    );
  });

  it('evaluates Query Contract 1.5.0 FieldRefValue, dateDiff, expression filters, and aggregations across tabs', async () => {
    // Primary tab query: shipped_on > ordered_on (FieldRefValue) + dateDiff expression
    const rows = await rowsSource().getData(
      {
        filter: {
          type: 'condition',
          field: 'shipped_on',
          operator: '>',
          value: { type: 'field', name: 'ordered_on' },
        },
        expressions: [
          { alias: 'order_id', expression: { type: 'field', field: 'order_id' } },
          {
            alias: 'lead_days',
            expression: {
              type: 'dateDiff',
              unit: 'day',
              start: { type: 'field', field: 'ordered_on' },
              end: { type: 'field', field: 'shipped_on' },
            },
          },
        ],
        orderBy: [{ field: 'amount', direction: 'desc' }],
      },
      context(makeConfig()),
    );

    assert.deepEqual(rows, [
      { order_id: 'ORD-2', lead_days: 5 },
      { order_id: 'ORD-1', lead_days: 5 },
    ]);

    // Expression filter normalization & evaluation + grouped aggregation
    const normalized = normalizeFilterNode({
      type: 'expression',
      left: { type: 'field', field: 'paid' },
      operator: '=',
      right: { type: 'value', value: true },
    });
    assert.deepEqual(normalized, {
      type: 'condition',
      field: 'paid',
      operator: '=',
      value: true,
    });

    const shape = analyzeQuery({
      filter: {
        type: 'expression',
        left: { type: 'field', field: 'customer' },
        operator: '=',
        right: { type: 'value', value: 'Acme Corp' },
      },
    });
    assert.deepEqual(shape.values('customer'), ['Acme Corp']);

    const aggregated = await rowsSource().getData(
      {
        filter: {
          type: 'expression',
          left: {
            type: 'dateDiff',
            unit: 'day',
            start: { type: 'field', field: 'ordered_on' },
            end: { type: 'field', field: 'shipped_on' },
          },
          operator: '>=',
          right: { type: 'value', value: 0 },
        },
        expressions: [
          { alias: 'customer', expression: { type: 'field', field: 'customer' } },
          { alias: 'total_amount', expression: { type: 'aggregate', action: 'sum', field: 'amount' } },
          { alias: 'order_count', expression: { type: 'aggregate', action: 'count' } },
        ],
        groupBy: ['customer'],
      },
      context(makeConfig()),
    );

    assert.deepEqual(aggregated, [
      { customer: 'Acme Corp', total_amount: 120, order_count: 1 },
      { customer: 'Beta Logistics', total_amount: 280, order_count: 1 },
    ]);

    // Secondary tab via dataSourceId + paging
    const secondaryPage = await rowsSource().getData(
      {
        orderBy: [{ field: 'stock', direction: 'asc' }],
        offset: 0,
        limit: 1,
      },
      context(makeConfig(), { dataSourceId: 'gsheet-tab-202' }),
    );
    assert.deepEqual(secondaryPage, {
      rows: [{ sku: 'SKU-20', stock: 12, warehouse: 'South' }],
      total: 2,
    });
  });

  it('evaluates relativeDate with snapEdge and post-snap offset', async () => {
    const rows = await rowsSource().getData(
      {
        filter: {
          type: 'group',
          operator: 'AND',
          items: [
            {
              type: 'condition',
              field: 'ordered_on',
              operator: '>=',
              value: {
                type: 'relativeDate',
                from: 'today',
                unit: 'year',
                amount: 0,
                snapTo: 'year',
                snapEdge: 'start',
                offset: { unit: 'day', amount: 14 },
              },
            },
            {
              type: 'condition',
              field: 'ordered_on',
              operator: '<=',
              value: {
                type: 'relativeDate',
                from: 'today',
                unit: 'year',
                amount: 0,
                snapTo: 'year',
                snapEdge: 'end',
              },
            },
          ],
        },
      },
      context(makeConfig()),
    );

    // Jan 1 + 14 days = Jan 15 -> matches ORD-2 (Jan 20) and ORD-3 (Jan 25), excludes ORD-1 (Jan 10)
    assert.equal(rows.length, 2);
    assert.deepEqual(
      rows.map((r) => r.order_id),
      ['ORD-2', 'ORD-3'],
    );
  });

  it('isolates cached tokens by credential fingerprint and enforces LRU max entries', async () => {
    const configA = makeConfig({
      service_account_json: makeServiceAccountJson({
        client_email: 'acct-a@test-project.iam.gserviceaccount.com',
        private_key: KEY_PAIR_A.privateKey,
      }),
    });
    const configB = makeConfig({
      service_account_json: makeServiceAccountJson({
        client_email: 'acct-b@test-project.iam.gserviceaccount.com',
        private_key: KEY_PAIR_B.privateKey,
      }),
    });

    await bundle.testConnection({ config: configA, instanceId: null, instanceSlug: null });
    await bundle.testConnection({ config: configA, instanceId: null, instanceSlug: null });
    const tokenCallsAfterTwoA = calls.filter((c) => c.url.endsWith('/token')).length;
    assert.equal(tokenCallsAfterTwoA, 1, 'second call with identical credentials must reuse cached token');

    await bundle.testConnection({ config: configB, instanceId: null, instanceSlug: null });
    const tokenCallsAfterB = calls.filter((c) => c.url.endsWith('/token')).length;
    assert.equal(tokenCallsAfterB, 2, 'distinct credentials must not share cached tokens');

    // Fill past MAX_TOKEN_CACHE_ENTRIES and verify LRU bound
    clearTokenCache();
    for (let i = 0; i < MAX_TOKEN_CACHE_ENTRIES + 8; i++) {
      const cfg = makeConfig({
        service_account_json: makeServiceAccountJson({
          client_email: `acct-${i}@test-project.iam.gserviceaccount.com`,
        }),
      });
      await bundle.testConnection({ config: cfg, instanceId: null, instanceSlug: null });
    }
    assert.equal(getTokenCacheSize(), MAX_TOKEN_CACHE_ENTRIES);
  });

  it('propagates AbortSignal cancellation immediately to in-flight token and Sheets API requests', async () => {
    const controller1 = new AbortController();
    let seenTokenSignal = null;
    respond = (_url, init) => {
      seenTokenSignal = init?.signal ?? null;
      return new Promise(() => {});
    };

    const pending1 = rowsSource().getData(
      {},
      context(makeConfig(), { signal: controller1.signal }),
    );
    controller1.abort(new Error('caller cancelled token fetch'));

    await assert.rejects(pending1, /caller cancelled token fetch/);
    assert.ok(seenTokenSignal, 'token fetch must receive a combined AbortSignal');
    assert.equal(seenTokenSignal.aborted, true, 'in-flight token signal must abort immediately');

    const controller2 = new AbortController();
    let seenSheetsSignal = null;
    respond = (url, init) => {
      if (url.endsWith('/token')) {
        return defaultResponder(url, init);
      }
      seenSheetsSignal = init?.signal ?? null;
      return new Promise(() => {});
    };

    const pending2 = rowsSource().getData(
      {},
      context(makeConfig(), { signal: controller2.signal }),
    );
    await new Promise((resolve) => setImmediate(resolve));
    controller2.abort(new Error('caller cancelled sheet read'));

    await assert.rejects(pending2, /caller cancelled sheet read/);
    assert.ok(seenSheetsSignal, 'Sheets API fetch must receive a combined AbortSignal');
    assert.equal(seenSheetsSignal.aborted, true, 'in-flight Sheets API signal must abort immediately');
  });

  it('redacts private keys, access tokens, and raw upstream bodies from errors on 401/403/500 and malformed JSON', async () => {
    const pemLine = KEY_PAIR_A.privateKey
      .split(/\r?\n/)
      .find((line) => line.length > 20 && !line.startsWith('-----'));

    // Malformed service_account_json containing private key
    const brokenJson = `{"client_email":"x","private_key":${JSON.stringify(KEY_PAIR_A.privateKey)}`;
    await assert.rejects(
      async () => {
        await rowsSource().getData({}, context(makeConfig({ service_account_json: brokenJson })));
      },
      (err) => {
        assert.match(err.message, /not valid JSON/);
        assert.equal(err.message.includes(pemLine), false);
        assert.equal(err.message.includes('PRIVATE KEY'), false);
        return true;
      },
    );

    // 401/403/500 with echoed token, PEM line, and raw HTML error body
    for (const status of [401, 403, 500]) {
      clearTokenCache();
      let issuedToken = '';
      respond = (url) => {
        if (url.endsWith('/token')) {
          issuedToken = `ya29.super-secret-token-${status}`;
          return new Response(
            JSON.stringify({ access_token: issuedToken, expires_in: 3600 }),
            { status: 200 },
          );
        }
        if (status === 500) {
          return new Response(
            `<html>Proxy failure leak=${issuedToken} key=${pemLine} raw-html-body-marker</html>`,
            { status: 500 },
          );
        }
        return new Response(
          JSON.stringify({
            error: {
              message: `Permission denied for token ${issuedToken} and key ${pemLine}`,
            },
          }),
          { status },
        );
      };

      await assert.rejects(
        async () => {
          await rowsSource().getData({}, context(makeConfig()));
        },
        (err) => {
          assert.match(err.message, new RegExp(String(status)));
          assert.equal(err.message.includes(issuedToken), false, `must not leak token on ${status}`);
          assert.equal(err.message.includes(pemLine), false, `must not leak private key on ${status}`);
          assert.equal(err.message.includes('raw-html-body-marker'), false, 'must not leak raw HTML error body');
          return true;
        },
      );
    }
  });

  it('rejects oversized responses both by Content-Length and while streaming chunks', async () => {
    respond = () =>
      new Response('{"values":[]}', {
        status: 200,
        headers: { 'content-length': '999999999' },
      });

    await assert.rejects(
      rowsSource().getData({}, context(makeConfig())),
      /exceeds the .* size limit/,
    );

    // Streaming body without Content-Length exceeding maxBytes
    respond = () =>
      new Response('x'.repeat(4096), {
        status: 200,
      });

    await assert.rejects(
      fetchBoundedText(
        'https://sheets.googleapis.com/v4/spreadsheets/test',
        { method: 'GET' },
        {
          timeoutMs: 5000,
          maxBytes: 1024,
          timeoutMessage: 'timed out',
          unreachablePrefix: 'unreachable: ',
          sizeLabel: 'Google Sheets API',
        },
      ),
      /Google Sheets API response exceeds the 1024-byte size limit/,
    );
  });
});
