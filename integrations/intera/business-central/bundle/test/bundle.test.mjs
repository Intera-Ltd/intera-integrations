import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');
const SERVICE_URL = 'http://business-central-service.example.test:8080';
const SERVICE_TOKEN = 'svc-token-bc-12345';
const COMPANY_ID = '11111111-2222-3333-4444-555555555555';

let calls;
let respond;
const realFetch = globalThis.fetch;

beforeEach(() => {
  calls = [];
  respond = () =>
    new Response(
      JSON.stringify({
        ok: true,
        data: [{ id: COMPANY_ID, name: 'CRONUS', display_name: 'CRONUS International Ltd.' }],
        error: null,
      }),
      { status: 200 },
    );
  globalThis.fetch = async (url, init) => {
    calls.push({ url: String(url), init });
    return respond(String(url), init);
  };
});

afterEach(() => {
  globalThis.fetch = realFetch;
});

const validConfig = () => ({
  intera_service_url: `${SERVICE_URL}/`,
  intera_service_token: SERVICE_TOKEN,
});
const context = (config, extra = {}) => ({ userId: 'u1', roles: ['admin'], config, ...extra });
const companies = () => bundle.dataSources.find((s) => s.id === 'bc-companies');
const salesInvoices = () => bundle.dataSources.find((s) => s.id === 'bc-sales-invoices');

describe('business-central bundle contract', () => {
  it('exports manifest with empty configSchema, 6 dataSources, getDataSources, and testConnection', () => {
    assert.equal(bundle.manifest.id, 'intera-businesscentral-integration');
    assert.equal(bundle.manifest.version, '3.2.0');
    assert.equal(bundle.manifest.requires.core, '>=0.5.94');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.deepEqual(bundle.manifest.configSchema, []);
    assert.equal(bundle.dataSources.length, 6);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
  });

  it('validates intera_service_url and intera_service_token before making any request', async () => {
    await assert.rejects(companies().getData({}, context({})), /redeploy the integration/i);
    await assert.rejects(
      companies().getData({}, context({ intera_service_url: SERVICE_URL })),
      /redeploy the integration/i,
    );
    await assert.rejects(
      companies().getData({}, context({ intera_service_url: SERVICE_URL, intera_service_token: '   ' })),
      /redeploy the integration/i,
    );
    await assert.rejects(
      companies().getData({}, context({ intera_service_url: 'ftp://example.test', intera_service_token: SERVICE_TOKEN })),
      /http or https/i,
    );
    await assert.rejects(
      companies().getData({}, context({ intera_service_url: 'http://169.254.169.254/latest', intera_service_token: SERVICE_TOKEN })),
      /link-local/i,
    );
    assert.equal(calls.length, 0);
  });

  it('sends Authorization Bearer token and handles Query Contract 1.5.0 expression trees and X-Total-Count paging', async () => {
    respond = () =>
      new Response(
        JSON.stringify({ ok: true, data: [{ id: 'inv-1', number: 'S-1001', remaining_amount: 250 }], error: null }),
        { status: 200, headers: { 'X-Total-Count': '42' } },
      );

    const dateDiffExpr = {
      type: 'expression',
      left: {
        type: 'dateDiff',
        unit: 'day',
        start: { type: 'field', field: 'due_date' },
        end: { type: 'now', anchor: 'today' },
      },
      operator: '>',
      right: { type: 'value', value: 30 },
    };

    const filterTree = {
      type: 'group',
      operator: 'AND',
      items: [
        {
          type: 'expression',
          left: { type: 'field', field: 'company_id' },
          operator: '=',
          right: { type: 'value', value: COMPANY_ID },
        },
        dateDiffExpr,
      ],
    };

    const page = await salesInvoices().getData(
      { filter: filterTree, offset: 0, limit: 10 },
      context(validConfig()),
    );

    assert.deepEqual(page, { rows: [{ id: 'inv-1', number: 'S-1001', remaining_amount: 250 }], total: 42 });
    assert.equal(calls[0].url, `${SERVICE_URL}/api/BusinessCentral/${COMPANY_ID}/sales-invoices/query`);
    assert.equal(calls[0].init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);

    const body = JSON.parse(calls[0].init.body);
    assert.deepEqual(body.filters, [{ type: 'group', operator: 'AND', items: [dateDiffExpr] }]);
    assert.equal(body.skip, 0);
    assert.equal(body.top, 10);
  });

  it('rejects company_id inside OR/NOT groups including expression filters', async () => {
    await assert.rejects(
      salesInvoices().getData(
        {
          filter: {
            type: 'group',
            operator: 'OR',
            items: [
              {
                type: 'expression',
                left: { type: 'field', field: 'company_id' },
                operator: '=',
                right: { type: 'value', value: COMPANY_ID },
              },
            ],
          },
        },
        context(validConfig()),
      ),
      /combined with AND/i,
    );
    assert.equal(calls.length, 0);
  });

  it('never echoes raw HTML/non-JSON upstream bodies or secret tokens in error messages', async () => {
    respond = () => new Response(`<html><body>502 Bad Gateway token=${SERVICE_TOKEN}</body></html>`, { status: 502 });
    await assert.rejects(
      companies().getData({}, context(validConfig())),
      (err) => {
        assert.match(err.message, /status 502/);
        assert.equal(err.message.includes('<html>'), false);
        assert.equal(err.message.includes(SERVICE_TOKEN), false);
        return true;
      },
    );

    respond = () =>
      new Response(JSON.stringify({ ok: false, data: null, error: `Upstream failed (${SERVICE_TOKEN})` }), {
        status: 401,
      });
    await assert.rejects(
      companies().getData({}, context(validConfig())),
      (err) => {
        assert.equal(err.message.includes(SERVICE_TOKEN), false);
        assert.match(err.message, /\[REDACTED\]/);
        return true;
      },
    );
  });

  it('forwards context.signal cancellation and supports testConnection', async () => {
    const controller = new AbortController();
    await companies().getData({}, context(validConfig(), { signal: controller.signal }));
    assert.equal(calls[0].init.signal.aborted, false);
    controller.abort();
    assert.equal(calls[0].init.signal.aborted, true);

    const res = await bundle.testConnection({
      config: validConfig(),
      instanceId: null,
      instanceSlug: null,
    });
    assert.match(res.detail, /Business Central: 1 company \(CRONUS International Ltd\.\)/);
    assert.equal(calls[1].init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);
  });
});
