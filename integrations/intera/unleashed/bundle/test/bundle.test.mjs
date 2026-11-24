import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');
const SERVICE_URL = 'http://unleashed-service.example.test:8080';
const SERVICE_TOKEN = 'svc-token-unleashed-67890';

let calls;
let respond;
const realFetch = globalThis.fetch;

beforeEach(() => {
  calls = [];
  respond = () =>
    new Response(
      JSON.stringify({
        ok: true,
        data: [{ warehouse_id: 'wh-1', warehouse_code: 'MAIN', warehouse_name: 'Main Depot' }],
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
const warehouses = () => bundle.dataSources.find((s) => s.id === 'unleashed_warehouses');

describe('unleashed bundle contract', () => {
  it('exports manifest with empty configSchema, 9 dataSources, getDataSources, and testConnection', () => {
    assert.equal(bundle.manifest.id, 'intera-unleashed-integration');
    assert.equal(bundle.manifest.version, '3.2.0');
    assert.equal(bundle.manifest.requires.core, '>=0.5.94');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.deepEqual(bundle.manifest.configSchema, []);
    assert.equal(bundle.dataSources.length, 9);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
  });

  it('validates intera_service_url and intera_service_token before making any request', async () => {
    await assert.rejects(warehouses().getData({}, context({})), /redeploy the integration/i);
    await assert.rejects(
      warehouses().getData({}, context({ intera_service_url: SERVICE_URL })),
      /redeploy the integration/i,
    );
    await assert.rejects(
      warehouses().getData({}, context({ intera_service_url: 'javascript:alert(1)', intera_service_token: SERVICE_TOKEN })),
      /http or https/i,
    );
    await assert.rejects(
      warehouses().getData({}, context({ intera_service_url: 'http://169.254.169.254/latest', intera_service_token: SERVICE_TOKEN })),
      /link-local/i,
    );
    assert.equal(calls.length, 0);
  });

  it('sends Authorization Bearer token and returns paged { rows, total } when X-Total-Count is set', async () => {
    respond = () =>
      new Response(
        JSON.stringify({ ok: true, data: [{ warehouse_code: 'MAIN' }], error: null }),
        { status: 200, headers: { 'X-Total-Count': '14' } },
      );

    const filter = {
      type: 'expression',
      left: {
        type: 'dateDiff',
        unit: 'day',
        start: { type: 'field', field: 'last_modified_on' },
        end: { type: 'now', anchor: 'today' },
      },
      operator: '<=',
      right: { type: 'value', value: 7 },
    };

    const page = await warehouses().getData({ filter, offset: 0, limit: 5 }, context(validConfig()));
    assert.deepEqual(page, { rows: [{ warehouse_code: 'MAIN' }], total: 14 });
    assert.equal(calls[0].url, `${SERVICE_URL}/api/warehouse/query`);
    assert.equal(calls[0].init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);

    const body = JSON.parse(calls[0].init.body);
    assert.deepEqual(body.filters, [filter]);
    assert.equal(body.skip, 0);
    assert.equal(body.top, 5);
  });

  it('never echoes raw HTML/non-JSON upstream bodies or secret tokens in error messages', async () => {
    respond = () => new Response(`<html>502 Bad Gateway ${SERVICE_TOKEN}</html>`, { status: 502 });
    await assert.rejects(
      warehouses().getData({}, context(validConfig())),
      (err) => {
        assert.match(err.message, /status 502/);
        assert.equal(err.message.includes('<html>'), false);
        assert.equal(err.message.includes(SERVICE_TOKEN), false);
        return true;
      },
    );
  });

  it('forwards context.signal cancellation and supports testConnection', async () => {
    const controller = new AbortController();
    await warehouses().getData({}, context(validConfig(), { signal: controller.signal }));
    assert.equal(calls[0].init.signal.aborted, false);
    controller.abort();
    assert.equal(calls[0].init.signal.aborted, true);

    respond = () => new Response(JSON.stringify({ ok: true, data: [{ warehouses: 3 }], error: null }), { status: 200 });
    const res = await bundle.testConnection({
      config: validConfig(),
      instanceId: null,
      instanceSlug: null,
    });
    assert.match(res.detail, /3 warehouses/);
    assert.equal(calls[1].init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);
  });
});
