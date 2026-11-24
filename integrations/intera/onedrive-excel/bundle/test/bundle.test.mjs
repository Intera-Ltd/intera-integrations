import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { afterEach, beforeEach, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import zlib from 'node:zlib';

const require = createRequire(import.meta.url);
const __dirname = path.dirname(fileURLToPath(import.meta.url));

const bundle = require('../dist/index.js');
const { fetchBoundedBytes } = require('../dist/graph/http.js');
const {
  clearTokenCache,
  getTokenCacheSize,
} = require('../dist/graph/token-cache.js');
const {
  clearWorkbookCache,
  getWorkbookCacheSize,
} = require('../dist/graph/drive-client.js');
const {
  FILE_CACHE_ENTRIES,
  MAX_FILE_BYTES,
  MAX_TOKEN_CACHE_ENTRIES,
} = require('../dist/constants.js');
const {
  analyzeQuery,
  normalizeFilterNode,
} = require('../dist/data-sources/query-shape.js');
const { decodeEntities } = require('../dist/xlsx/xml.js');

const TENANT_ID = '11111111-2222-3333-4444-555555555555';
const CLIENT_ID = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
const CLIENT_SECRET = 'super-secret-entra-client-value-9876';
const DRIVE_ID = 'b!driveExampleIdentifier1234';
const ITEM_ID = '01WORKBOOKITEMID5678';
const DOWNLOAD_URL = 'https://contoso.sharepoint.com/_layouts/15/download.aspx?tempauth=v1';

const TEST_YEAR = new Date().getFullYear();
const EXCEL_EPOCH_1900_MS = Date.UTC(1899, 11, 30);
const toExcelSerial = (year, month, day) =>
  Math.round((Date.UTC(year, month - 1, day) - EXCEL_EPOCH_1900_MS) / 86_400_000);

function buildZip(parts) {
  const localChunks = [];
  const centralChunks = [];
  let offset = 0;

  for (const [name, content] of Object.entries(parts)) {
    const nameBytes = Buffer.from(name, 'utf8');
    const rawBytes = Buffer.isBuffer(content) ? content : Buffer.from(content, 'utf8');
    const compressed = zlib.deflateRawSync(rawBytes);

    const local = Buffer.alloc(30 + nameBytes.length);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(0x0800, 6);
    local.writeUInt16LE(8, 8);
    local.writeUInt32LE(0, 14);
    local.writeUInt32LE(compressed.length, 18);
    local.writeUInt32LE(rawBytes.length, 22);
    local.writeUInt16LE(nameBytes.length, 26);
    local.writeUInt16LE(0, 28);
    nameBytes.copy(local, 30);

    const central = Buffer.alloc(46 + nameBytes.length);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt16LE(0x0800, 8);
    central.writeUInt16LE(8, 10);
    central.writeUInt32LE(0, 16);
    central.writeUInt32LE(compressed.length, 20);
    central.writeUInt32LE(rawBytes.length, 24);
    central.writeUInt16LE(nameBytes.length, 28);
    central.writeUInt16LE(0, 30);
    central.writeUInt16LE(0, 32);
    central.writeUInt32LE(offset, 42);
    nameBytes.copy(central, 46);

    localChunks.push(local, compressed);
    centralChunks.push(central);
    offset += local.length + compressed.length;
  }

  const centralDir = Buffer.concat(centralChunks);
  const eocd = Buffer.alloc(22);
  const count = Object.keys(parts).length;
  eocd.writeUInt32LE(0x06054b50, 0);
  eocd.writeUInt16LE(count, 8);
  eocd.writeUInt16LE(count, 10);
  eocd.writeUInt32LE(centralDir.length, 12);
  eocd.writeUInt32LE(offset, 16);

  return Buffer.concat([...localChunks, centralDir, eocd]);
}

function buildSampleXlsxBuffer() {
  const sJan10 = toExcelSerial(TEST_YEAR, 1, 10);
  const sJan15 = toExcelSerial(TEST_YEAR, 1, 15);
  const sJan20 = toExcelSerial(TEST_YEAR, 1, 20);
  const sJan25 = toExcelSerial(TEST_YEAR, 1, 25);

  const sharedStrings = [
    'Invoice ID',
    'Vendor',
    'Issued On',
    'Paid On',
    'Amount',
    'Approved',
    'Дата счета',
    'INV-1',
    'Northwind',
    `10.01.${TEST_YEAR}`,
    'INV-2',
    'Contoso',
    `20.01.${TEST_YEAR}`,
    'INV-3',
    `25.01.${TEST_YEAR}`,
    'HiddenKey',
    'H-1',
  ];

  const sstXml =
    `<?xml version="1.0" encoding="UTF-8"?>` +
    `<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">` +
    sharedStrings.map((s) => `<si><t>${s}</t></si>`).join('') +
    `</sst>`;

  // Style 0 = General, Style 1 = Built-in Date format 14 (yyyy-mm-dd)
  const stylesXml = `<?xml version="1.0" encoding="UTF-8"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <cellStyleXfs count="1"><xf numFmtId="0"/></cellStyleXfs>
  <cellXfs count="2">
    <xf numFmtId="0"/>
    <xf numFmtId="14" applyNumberFormat="1"/>
  </cellXfs>
</styleSheet>`;

  const hiddenSheetXml = `<?xml version="1.0" encoding="UTF-8"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <sheetData>
    <row r="1"><c r="A1" t="s"><v>15</v></c></row>
    <row r="2"><c r="A2" t="s"><v>16</v></c></row>
  </sheetData>
</worksheet>`;

  const invoicesSheetXml = `<?xml version="1.0" encoding="UTF-8"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <sheetData>
    <row r="1">
      <c r="A1" t="s"><v>0</v></c>
      <c r="B1" t="s"><v>1</v></c>
      <c r="C1" t="s"><v>2</v></c>
      <c r="D1" t="s"><v>3</v></c>
      <c r="E1" t="s"><v>4</v></c>
      <c r="F1" t="s"><v>5</v></c>
      <c r="G1" t="s"><v>6</v></c>
    </row>
    <row r="2">
      <c r="A2" t="s"><v>7</v></c>
      <c r="B2" t="s"><v>8</v></c>
      <c r="C2" s="1"><v>${sJan10}</v></c>
      <c r="D2" s="1"><v>${sJan15}</v></c>
      <c r="E2"><v>150</v></c>
      <c r="F2" t="b"><v>1</v></c>
      <c r="G2" t="s"><v>9</v></c>
    </row>
    <row r="3">
      <c r="A3" t="s"><v>10</v></c>
      <c r="B3" t="s"><v>11</v></c>
      <c r="C3" s="1"><v>${sJan20}</v></c>
      <c r="D3" s="1"><v>${sJan25}</v></c>
      <c r="E3"><v>350</v></c>
      <c r="F3" t="b"><v>0</v></c>
      <c r="G3" t="s"><v>12</v></c>
    </row>
    <row r="4">
      <c r="A4" t="s"><v>13</v></c>
      <c r="B4" t="s"><v>8</v></c>
      <c r="C4" s="1"><v>${sJan25}</v></c>
      <c r="D4" s="1"><v>${sJan20}</v></c>
      <c r="E4"><v>90</v></c>
      <c r="F4" t="b"><v>1</v></c>
      <c r="G4" t="s"><v>14</v></c>
    </row>
  </sheetData>
</worksheet>`;

  return buildZip({
    '_rels/.rels': `<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>`,
    'xl/workbook.xml': `<?xml version="1.0" encoding="UTF-8"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
    <sheet name="InternalLookup" sheetId="1" state="hidden" r:id="rId1"/>
    <sheet name="Invoices" sheetId="2" r:id="rId2"/>
  </sheets>
</workbook>`,
    'xl/_rels/workbook.xml.rels': `<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>
  <Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>`,
    'xl/sharedStrings.xml': sstXml,
    'xl/styles.xml': stylesXml,
    'xl/worksheets/sheet1.xml': hiddenSheetXml,
    'xl/worksheets/sheet2.xml': invoicesSheetXml,
  });
}

const SAMPLE_XLSX = buildSampleXlsxBuffer();

function makeJwtWithRoles(roles, suffix = '1') {
  const header = Buffer.from(JSON.stringify({ alg: 'RS256', typ: 'JWT' })).toString('base64url');
  const payload = Buffer.from(JSON.stringify({ roles, jti: suffix })).toString('base64url');
  return `${header}.${payload}.secret-jwt-sig-${suffix}`;
}

function makeConfig(overrides = {}) {
  return {
    tenant_id: TENANT_ID,
    client_id: CLIENT_ID,
    client_secret: CLIENT_SECRET,
    drive_id: DRIVE_ID,
    item_id: ITEM_ID,
    identity_column: 'invoice_id',
    ...overrides,
  };
}

const context = (config, extra = {}) => ({
  userId: 'u-1',
  roles: ['admin'],
  config,
  ...extra,
});

const rowsSource = () => bundle.dataSources.find((s) => s.id === 'excel-rows');

let calls;
let respond;
let tokenSeq = 0;
const realFetch = globalThis.fetch;

function defaultResponder(url, init) {
  const parsed = new URL(url);
  if (parsed.origin === 'https://login.microsoftonline.com') {
    const jwt = makeJwtWithRoles(['Files.SelectedOperations.Selected'], String(++tokenSeq));
    return new Response(
      JSON.stringify({ access_token: jwt, expires_in: 3600, token_type: 'Bearer' }),
      { status: 200, headers: { 'content-type': 'application/json' } },
    );
  }

  if (parsed.origin === 'https://graph.microsoft.com') {
    return new Response(
      JSON.stringify({
        name: 'FinanceBook.xlsx',
        size: SAMPLE_XLSX.length,
        cTag: '"cTag-v1"',
        eTag: '"eTag-v1"',
        file: { mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' },
        '@microsoft.graph.downloadUrl': DOWNLOAD_URL,
      }),
      { status: 200, headers: { 'content-type': 'application/json' } },
    );
  }

  // SharePoint download host — verify Authorization is never forwarded here!
  const authHeader = init?.headers?.authorization ?? init?.headers?.Authorization;
  assert.equal(authHeader, undefined, 'Authorization header must not be sent to storage download host');
  return new Response(SAMPLE_XLSX, {
    status: 200,
    headers: { 'content-length': String(SAMPLE_XLSX.length) },
  });
}

beforeEach(() => {
  calls = [];
  clearTokenCache();
  clearWorkbookCache();
  respond = defaultResponder;
  globalThis.fetch = async (url, init) => {
    calls.push({ url: String(url), init });
    return respond(String(url), init);
  };
});

afterEach(() => {
  globalThis.fetch = realFetch;
  clearTokenCache();
  clearWorkbookCache();
});

describe('onedrive-excel bundle contract', () => {
  it('exports manifest, excel-rows dataSource, provisionInstance, testConnection, and built icon asset', () => {
    assert.equal(bundle.manifest.id, 'intera-onedrive-excel-integration');
    assert.equal(bundle.manifest.name, 'Excel (OneDrive / SharePoint)');
    assert.equal(bundle.manifest.version, '3.3.0');
    assert.deepEqual(bundle.manifest.requires, { core: '>=0.5.25' });
    assert.deepEqual(bundle.manifest.compatibility, { integration_contract: 2 });
    assert.equal(bundle.manifest.iconFile, 'assets/icon.svg');
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.provisionInstance, 'function');
    assert.equal(typeof bundle.testConnection, 'function');
    assert.equal(bundle.dataSources.length, 1);
    assert.equal(rowsSource().id, 'excel-rows');

    const iconPath = path.join(__dirname, '..', 'dist', 'assets', 'icon.svg');
    assert.equal(fs.existsSync(iconPath), true, 'dist/assets/icon.svg must be copied on build');
  });

  it('validates Entra ID tenant, GUIDs, and Graph bare IDs before making network calls', async () => {
    await assert.rejects(rowsSource().getData({}, context(undefined)), /config is required/);
    await assert.rejects(rowsSource().getData({}, context(makeConfig({ tenant_id: 'common' }))), /multi-tenant/);
    await assert.rejects(rowsSource().getData({}, context(makeConfig({ tenant_id: 'consumers' }))), /multi-tenant/);
    await assert.rejects(rowsSource().getData({}, context(makeConfig({ client_id: 'not-a-guid' }))), /must be a GUID/);
    await assert.rejects(
      rowsSource().getData({}, context(makeConfig({ drive_id: 'https://contoso.sharepoint.com/Book.xlsx' }))),
      /not a URL/,
    );
    assert.equal(calls.length, 0);
  });

  it('supports testConnection, /content 302 redirect fallback, and provisionInstance dynamic column inference', async () => {
    const testResult = await bundle.testConnection({
      config: makeConfig(),
      instanceId: null,
      instanceSlug: null,
    });
    assert.equal(testResult.detail, 'Excel: "FinanceBook.xlsx", worksheet "Invoices"');

    // Verify /content 302 redirect fallback when @microsoft.graph.downloadUrl is omitted
    clearWorkbookCache();
    respond = (url, init) => {
      const parsed = new URL(url);
      if (parsed.origin === 'https://login.microsoftonline.com') {
        return defaultResponder(url, init);
      }
      if (parsed.origin === 'https://graph.microsoft.com' && parsed.pathname.endsWith('/content')) {
        return new Response(null, {
          status: 302,
          headers: { location: DOWNLOAD_URL },
        });
      }
      if (parsed.origin === 'https://graph.microsoft.com') {
        return new Response(
          JSON.stringify({
            name: 'FinanceBook.xlsx',
            size: SAMPLE_XLSX.length,
            cTag: '"cTag-redirect"',
            file: {},
          }),
          { status: 200 },
        );
      }
      return defaultResponder(url, init);
    };

    const provisioned = await bundle.provisionInstance({
      config: makeConfig(),
      instanceId: 'inst-excel-1',
      instanceSlug: 'finance-book',
    });

    assert.equal(provisioned.dataSources.length, 1);
    const ds = provisioned.dataSources[0];
    assert.equal(ds.id, 'excel-rows');
    assert.match(ds.description, /Invoices.*FinanceBook\.xlsx/);
    assert.deepEqual(
      ds.fields.map((f) => ({ name: f.name, type: f.type, identity: Boolean(f.identity) })),
      [
        { name: 'invoice_id', type: 'string', identity: true },
        { name: 'vendor', type: 'string', identity: false },
        { name: 'issued_on', type: 'date', identity: false },
        { name: 'paid_on', type: 'date', identity: false },
        { name: 'amount', type: 'number', identity: false },
        { name: 'approved', type: 'boolean', identity: false },
        { name: 'дата_счета', type: 'date', identity: false },
      ],
    );

    // Explicit hidden worksheet selection works when named
    const hiddenRows = await rowsSource().getData(
      {},
      context(makeConfig({ sheet_name: 'InternalLookup', identity_column: '' })),
    );
    assert.deepEqual(hiddenRows, [{ hiddenkey: 'H-1' }]);
  });

  it('evaluates Query Contract 1.5.0 FieldRefValue, dateDiff, relativeDate offset, expression filters, and aggregations', async () => {
    // Field-to-field comparison (paid_on > issued_on) + dateDiff expression
    const rows = await rowsSource().getData(
      {
        filter: {
          type: 'condition',
          field: 'paid_on',
          operator: '>',
          value: { type: 'field', name: 'issued_on' },
        },
        expressions: [
          { alias: 'invoice_id', expression: { type: 'field', field: 'invoice_id' } },
          {
            alias: 'days_to_pay',
            expression: {
              type: 'dateDiff',
              unit: 'day',
              start: { type: 'field', field: 'issued_on' },
              end: { type: 'field', field: 'paid_on' },
            },
          },
        ],
        orderBy: [{ field: 'amount', direction: 'desc' }],
      },
      context(makeConfig()),
    );

    assert.deepEqual(rows, [
      { invoice_id: 'INV-2', days_to_pay: 5 },
      { invoice_id: 'INV-1', days_to_pay: 5 },
    ]);

    // Expression filter normalization & query shape analysis
    const normalized = normalizeFilterNode({
      type: 'expression',
      left: { type: 'field', field: 'paid_on' },
      operator: '>',
      right: { type: 'field', field: 'issued_on' },
    });
    assert.deepEqual(normalized, {
      type: 'condition',
      field: 'paid_on',
      operator: '>',
      value: { type: 'field', name: 'issued_on' },
    });

    const shape = analyzeQuery({
      filter: {
        type: 'expression',
        left: { type: 'field', field: 'vendor' },
        operator: '=',
        right: { type: 'value', value: 'Northwind' },
      },
    });
    assert.deepEqual(shape.values('vendor'), ['Northwind']);

    // relativeDate with snapEdge + post-snap offset (Jan 1 + 14 days = Jan 15)
    const relativeFiltered = await rowsSource().getData(
      {
        filter: {
          type: 'group',
          operator: 'AND',
          items: [
            {
              type: 'condition',
              field: 'issued_on',
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
              field: 'issued_on',
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
    assert.deepEqual(
      relativeFiltered.map((r) => r.invoice_id),
      ['INV-2', 'INV-3'],
    );

    // Expression filter + grouped aggregations
    const aggregated = await rowsSource().getData(
      {
        filter: {
          type: 'expression',
          left: {
            type: 'dateDiff',
            unit: 'day',
            start: { type: 'field', field: 'issued_on' },
            end: { type: 'field', field: 'paid_on' },
          },
          operator: '>=',
          right: { type: 'value', value: 0 },
        },
        expressions: [
          { alias: 'vendor', expression: { type: 'field', field: 'vendor' } },
          { alias: 'total_amount', expression: { type: 'aggregate', action: 'sum', field: 'amount' } },
          { alias: 'invoice_count', expression: { type: 'aggregate', action: 'count' } },
        ],
        groupBy: ['vendor'],
      },
      context(makeConfig()),
    );
    assert.deepEqual(aggregated, [
      { vendor: 'Northwind', total_amount: 150, invoice_count: 1 },
      { vendor: 'Contoso', total_amount: 350, invoice_count: 1 },
    ]);
  });

  it('scopes token and workbook LRU caches by SHA-256 credential hash and enforces max entries', async () => {
    const configA = makeConfig({ client_secret: 'secret-instance-A-1234' });
    const configB = makeConfig({ client_secret: 'secret-instance-B-5678' });

    await rowsSource().getData({}, context(configA));
    await rowsSource().getData({}, context(configA));
    const downloadsAfterTwoA = calls.filter((c) => c.url === DOWNLOAD_URL).length;
    assert.equal(downloadsAfterTwoA, 1, 'identical credentials + cTag must reuse cached workbook');

    await rowsSource().getData({}, context(configB));
    const downloadsAfterB = calls.filter((c) => c.url === DOWNLOAD_URL).length;
    assert.equal(
      downloadsAfterB,
      2,
      'two instances with distinct credentials must never share cached workbooks',
    );
    assert.equal(getWorkbookCacheSize(), 2);

    // Fill workbook cache past FILE_CACHE_ENTRIES and verify LRU bound
    for (let i = 0; i < FILE_CACHE_ENTRIES + 4; i++) {
      await rowsSource().getData(
        {},
        context(makeConfig({ item_id: `01WORKBOOKITEM${1000 + i}` })),
      );
    }
    assert.equal(getWorkbookCacheSize(), FILE_CACHE_ENTRIES);

    // Fill token cache past MAX_TOKEN_CACHE_ENTRIES and verify LRU bound
    clearTokenCache();
    for (let i = 0; i < MAX_TOKEN_CACHE_ENTRIES + 6; i++) {
      await bundle.testConnection({
        config: makeConfig({ client_secret: `secret-lru-${i}-value` }),
        instanceId: null,
        instanceSlug: null,
      });
    }
    assert.equal(getTokenCacheSize(), MAX_TOKEN_CACHE_ENTRIES);
  });

  it('propagates AbortSignal cancellation immediately across token, Graph metadata, and workbook download calls', async () => {
    const controller1 = new AbortController();
    let seenLoginSignal = null;
    respond = (_url, init) => {
      seenLoginSignal = init?.signal ?? null;
      return new Promise(() => {});
    };

    const pending1 = rowsSource().getData(
      {},
      context(makeConfig(), { signal: controller1.signal }),
    );
    controller1.abort(new Error('cancelled during Microsoft sign-in'));
    await assert.rejects(pending1, /cancelled during Microsoft sign-in/);
    assert.equal(seenLoginSignal?.aborted, true);

    const controller2 = new AbortController();
    let seenDownloadSignal = null;
    respond = (url, init) => {
      if (url === DOWNLOAD_URL) {
        seenDownloadSignal = init?.signal ?? null;
        return new Promise(() => {});
      }
      return defaultResponder(url, init);
    };

    const pending2 = rowsSource().getData(
      {},
      context(makeConfig(), { signal: controller2.signal }),
    );
    await new Promise((resolve) => setTimeout(resolve, 10));
    controller2.abort(new Error('cancelled during workbook download'));
    await assert.rejects(pending2, /cancelled during workbook download/);
    assert.equal(seenDownloadSignal?.aborted, true);
  });

  it('redacts client_secret, access tokens, and raw upstream bodies from errors on 401/403/500 and malformed JSON', async () => {
    // Sign-in 500 with raw HTML body echoing client_secret
    respond = () =>
      new Response(
        `<html>Gateway error secret=${CLIENT_SECRET} raw-login-html-marker</html>`,
        { status: 500 },
      );

    await assert.rejects(
      async () => {
        await rowsSource().getData({}, context(makeConfig()));
      },
      (err) => {
        assert.match(err.message, /500/);
        assert.equal(err.message.includes(CLIENT_SECRET), false, 'must not leak client_secret');
        assert.equal(err.message.includes('raw-login-html-marker'), false, 'must not leak raw HTML');
        return true;
      },
    );

    // Graph 401/403/500 with echoed access token and client_secret
    for (const status of [401, 403, 500]) {
      clearTokenCache();
      let issuedJwt = '';
      respond = (url) => {
        if (url.includes('login.microsoftonline.com')) {
          issuedJwt = makeJwtWithRoles(['Files.SelectedOperations.Selected'], `err-${status}`);
          return new Response(
            JSON.stringify({ access_token: issuedJwt, expires_in: 3600 }),
            { status: 200 },
          );
        }
        if (status === 500) {
          return new Response(
            `<!DOCTYPE html><body>Crash token=${issuedJwt} secret=${CLIENT_SECRET} raw-graph-html</body>`,
            { status: 500 },
          );
        }
        return new Response(
          JSON.stringify({
            error: {
              code: 'accessDenied',
              message: `Denied for secret=${CLIENT_SECRET} bearer=${issuedJwt}`,
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
          assert.equal(err.message.includes(CLIENT_SECRET), false, `must not leak client_secret on ${status}`);
          assert.equal(err.message.includes(issuedJwt), false, `must not leak access token on ${status}`);
          assert.equal(err.message.includes('raw-graph-html'), false, 'must not leak raw HTML body');
          return true;
        },
      );
    }
  });

  it('rejects oversized workbooks, streaming downloads past byte cap, OLE containers, and out-of-range XML entities', async () => {
    assert.equal(decodeEntities('X&#x110000;Y&#55296;Z&#x41;'), 'X\uFFFDY\uFFFDZA');

    // Metadata size > MAX_FILE_BYTES rejected before downloading
    respond = (url, init) => {
      if (url.includes('graph.microsoft.com')) {
        return new Response(
          JSON.stringify({
            name: 'HugeDump.xlsx',
            size: MAX_FILE_BYTES + 1024,
            cTag: '"cTag-huge"',
            file: {},
            '@microsoft.graph.downloadUrl': DOWNLOAD_URL,
          }),
          { status: 200 },
        );
      }
      return defaultResponder(url, init);
    };

    await assert.rejects(rowsSource().getData({}, context(makeConfig())), /over the 64\.0 MB/);
    assert.equal(
      calls.filter((c) => c.url === DOWNLOAD_URL).length,
      0,
      'oversized workbook must not be downloaded',
    );

    // Streaming byte cap enforced even when Content-Length is absent
    respond = () => new Response(Buffer.alloc(4096, 0x41), { status: 200 });
    await assert.rejects(
      fetchBoundedBytes('https://contoso.sharepoint.com/download', { method: 'GET' }, {
        timeoutMs: 5000,
        maxBytes: 1024,
        timeoutMessage: 'timed out',
        unreachablePrefix: 'unreachable: ',
        sizeLabel: 'Workbook "HugeDump.xlsx"',
      }),
      /Workbook "HugeDump.xlsx" response exceeds the 1024-byte size limit/,
    );

    // Encrypted / legacy .xls OLE container rejected cleanly
    const oleHeader = Buffer.from([0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1, 0, 0, 0, 0]);
    respond = (url, init) => {
      if (url === DOWNLOAD_URL) {
        return new Response(oleHeader, { status: 200 });
      }
      return defaultResponder(url, init);
    };
    await assert.rejects(
      rowsSource().getData({}, context(makeConfig({ item_id: '01ENCRYPTEDBOOK99' }))),
      /legacy \.xls workbook or an encrypted \.xlsx/,
    );
  });
});
