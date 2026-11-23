import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');
const SERVICE_URL = 'http://xero-service.example.test:8080';

let calls;
let respond;
const realFetch = globalThis.fetch;

beforeEach(() => {
  calls = [];
  respond = () =>
    new Response(
      JSON.stringify({
        ok: true,
        data: [{ invoice_id: 'inv-1', invoice_number: 'INV-0001' }],
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
const invoices = () => bundle.dataSources.find((s) => s.id === 'xero_invoices');

describe('xero bundle contract', () => {
  it('exports manifest, dataSources, getDataSources, and testConnection', () => {
    assert.equal(bundle.manifest.id, 'intera-xero-integration');
    assert.equal(bundle.manifest.version, '3.2.0');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.ok(bundle.dataSources.length >= 15);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
  });

  it('uses intera_service_url and forwards optional tenantId and accessToken', async () => {
    const rows = await invoices().getData({}, context({ intera_service_url: SERVICE_URL, tenantId: 'ten-42' }));
    assert.equal(rows.length, 1);
    assert.equal(calls[0].url, `${SERVICE_URL}/api/Source/invoices/query`);
    assert.equal(calls[0].init.headers.Authorization, undefined);
    assert.equal(calls[0].init.headers['X-Xero-Tenant-Id'], 'ten-42');

    await invoices().getData({}, context({ apiBaseUrl: 'https://xero-proxy.example.test', accessToken: 'tok-xero' }));
    assert.equal(calls[1].url, 'https://xero-proxy.example.test/api/Source/invoices/query');
    assert.equal(calls[1].init.headers.Authorization, 'Bearer tok-xero');
  });

  it('validates service URL before making any request', async () => {
    await assert.rejects(invoices().getData({}, context({})), /intera_service_url/);
    await assert.rejects(invoices().getData({}, context({ intera_service_url: 'javascript:alert(1)' })), /http or https/);
    assert.equal(calls.length, 0);
  });

  it('never echoes raw non-JSON upstream bodies or secret tokens in error messages', async () => {
    const secret = 'secret-xero-inbound-token';
    respond = () => new Response(`bad gateway ${secret}`, { status: 502 });
    await assert.rejects(
      invoices().getData({}, context({ intera_service_url: SERVICE_URL, accessToken: secret })),
      (err) => {
        assert.match(err.message, /status 502/);
        assert.equal(err.message.includes(secret), false);
        return true;
      },
    );
  });

  it('forwards context.signal to fetch and supports testConnection', async () => {
    const controller = new AbortController();
    await invoices().getData({}, context({ intera_service_url: SERVICE_URL }, { signal: controller.signal }));
    assert.equal(calls[0].init.signal.aborted, false);
    controller.abort();
    assert.equal(calls[0].init.signal.aborted, true);

    respond = () =>
      new Response(JSON.stringify({ ok: true, data: [{ name: 'Acme Books Ltd', is_demo_company: false }], error: null }), {
        status: 200,
      });
    const res = await bundle.testConnection({
      config: { intera_service_url: SERVICE_URL },
      instanceId: null,
      instanceSlug: null,
    });
    assert.match(res.detail, /Xero: Acme Books Ltd/);
  });
});
