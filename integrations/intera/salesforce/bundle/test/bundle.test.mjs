import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');
const SERVICE_URL = 'http://salesforce-service.example.test:8080';

let calls;
let respond;
const realFetch = globalThis.fetch;

beforeEach(() => {
  calls = [];
  respond = () =>
    new Response(
      JSON.stringify({
        ok: true,
        data: [{ account_id: '001000000000001', account_name: 'Example Corp' }],
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

const context = (config, extra = {}) => ({ userId: 'u1', roles: ['admin'], config, ...extra });
const accounts = () => bundle.dataSources.find((s) => s.id === 'salesforce_accounts');

describe('salesforce bundle contract', () => {
  it('exports manifest, 6 dataSources, getDataSources, and testConnection', () => {
    assert.equal(bundle.manifest.id, 'intera-salesforce-integration');
    assert.equal(bundle.manifest.version, '3.2.0');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.equal(bundle.dataSources.length, 6);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
  });

  it('uses intera_service_url without requiring accessToken, and allows apiBaseUrl + accessToken override', async () => {
    const rows = await accounts().getData({}, context({ intera_service_url: `${SERVICE_URL}/` }));
    assert.equal(rows.length, 1);
    assert.equal(calls[0].url, `${SERVICE_URL}/api/account/query`);
    assert.equal(calls[0].init.headers.Authorization, undefined);

    await accounts().getData(
      {},
      context({ intera_service_url: SERVICE_URL, apiBaseUrl: 'https://override.example.test', accessToken: 'tok-123' }),
    );
    assert.equal(calls[1].url, 'https://override.example.test/api/account/query');
    assert.equal(calls[1].init.headers.Authorization, 'Bearer tok-123');
  });

  it('forwards Query Contract 1.5.0 filters and expressions and returns paged { rows, total } when X-Total-Count is set', async () => {
    respond = () =>
      new Response(
        JSON.stringify({ ok: true, data: [{ account_id: '001', age_days: 42 }], error: null }),
        { status: 200, headers: { 'X-Total-Count': '18' } },
      );

    const filter = {
      type: 'expression',
      left: {
        type: 'dateDiff',
        unit: 'day',
        start: { type: 'field', field: 'created_date' },
        end: { type: 'now', anchor: 'today' },
      },
      operator: '>',
      right: { type: 'value', value: 30 },
    };

    const page = await accounts().getData({ filter, offset: 0, limit: 10 }, context({ intera_service_url: SERVICE_URL }));
    assert.deepEqual(page, { rows: [{ account_id: '001', age_days: 42 }], total: 18 });
    const body = JSON.parse(calls[0].init.body);
    assert.deepEqual(body.filters, [filter]);
    assert.equal(body.skip, 0);
    assert.equal(body.top, 10);
  });

  it('validates service URL before making any request', async () => {
    await assert.rejects(accounts().getData({}, context({})), /intera_service_url/);
    await assert.rejects(accounts().getData({}, context({ intera_service_url: 'file:///etc/passwd' })), /http or https/);
    assert.equal(calls.length, 0);
  });

  it('never echoes raw non-JSON upstream bodies or secret tokens in error messages', async () => {
    const secret = 'super-secret-inbound-token';
    respond = () => new Response(`raw body with ${secret}`, { status: 502 });
    await assert.rejects(
      accounts().getData({}, context({ intera_service_url: SERVICE_URL, accessToken: secret })),
      (err) => {
        assert.match(err.message, /status 502/);
        assert.equal(err.message.includes(secret), false);
        return true;
      },
    );
  });

  it('forwards context.signal to fetch and supports testConnection', async () => {
    const controller = new AbortController();
    await accounts().getData({}, context({ intera_service_url: SERVICE_URL }, { signal: controller.signal }));
    assert.equal(calls[0].init.signal.aborted, false);
    controller.abort();
    assert.equal(calls[0].init.signal.aborted, true);

    respond = () => new Response(JSON.stringify({ ok: true, data: [{ accounts: 12 }], error: null }), { status: 200 });
    const res = await bundle.testConnection({
      config: { intera_service_url: SERVICE_URL },
      instanceId: null,
      instanceSlug: null,
    });
    assert.match(res.detail, /12 accounts/);
  });
});
