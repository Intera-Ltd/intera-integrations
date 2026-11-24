import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');
const SERVICE_URL = 'http://soft1-service.example.test:8080';
const SERVICE_TOKEN = 'svc-token-soft1-98765';

let calls;
let respond;
const realFetch = globalThis.fetch;

beforeEach(() => {
  calls = [];
  respond = () =>
    new Response(
      JSON.stringify({
        ok: true,
        data: [{ company_id: '1000', name: 'Acme Hellas SA' }],
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
const customers = () => bundle.dataSources.find((s) => s.id === 'soft1_customers');

describe('soft1 bundle contract', () => {
  it('exports manifest with empty configSchema, 51 dataSources, getDataSources, and testConnection', () => {
    assert.equal(bundle.manifest.id, 'intera-soft1-integration');
    assert.equal(bundle.manifest.version, '3.2.0');
    assert.equal(bundle.manifest.requires.core, '>=0.5.94');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.deepEqual(bundle.manifest.configSchema, []);
    assert.equal(bundle.dataSources.length, 51);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
  });

  it('validates intera_service_url and intera_service_token before making any request', async () => {
    await assert.rejects(customers().getData({}, context({})), /redeploy the integration/i);
    await assert.rejects(
      customers().getData({}, context({ intera_service_url: SERVICE_URL })),
      /redeploy the integration/i,
    );
    await assert.rejects(
      customers().getData({}, context({ intera_service_url: 'file:///etc/passwd', intera_service_token: SERVICE_TOKEN })),
      /http or https/i,
    );
    await assert.rejects(
      customers().getData({}, context({ intera_service_url: 'http://169.254.169.254/latest', intera_service_token: SERVICE_TOKEN })),
      /link-local/i,
    );
    assert.equal(calls.length, 0);
  });

  it('sends Authorization Bearer token and returns paged { rows, total } when X-Total-Count is set', async () => {
    respond = () =>
      new Response(
        JSON.stringify({ ok: true, data: [{ customer_id: 'c-1', code: 'C001', name: 'Alpha AE' }], error: null }),
        { status: 200, headers: { 'X-Total-Count': '95' } },
      );

    const page = await customers().getData({ offset: 0, limit: 25 }, context(validConfig()));
    assert.deepEqual(page, { rows: [{ customer_id: 'c-1', code: 'C001', name: 'Alpha AE' }], total: 95 });
    assert.equal(calls[0].url, `${SERVICE_URL}/api/Source/customers/query`);
    assert.equal(calls[0].init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);

    const body = JSON.parse(calls[0].init.body);
    assert.equal(body.skip, 0);
    assert.equal(body.top, 25);
  });

  it('never echoes raw HTML/non-JSON upstream bodies or secret tokens in error messages', async () => {
    respond = () => new Response(`<html>502 Bad Gateway ${SERVICE_TOKEN}</html>`, { status: 502 });
    await assert.rejects(
      customers().getData({}, context(validConfig())),
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
    await customers().getData({}, context(validConfig(), { signal: controller.signal }));
    assert.equal(calls[0].init.signal.aborted, false);
    controller.abort();
    assert.equal(calls[0].init.signal.aborted, true);

    const res = await bundle.testConnection({
      config: validConfig(),
      instanceId: null,
      instanceSlug: null,
    });
    assert.equal(res.detail, 'Soft1: Acme Hellas SA');
    assert.equal(calls[1].init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);
  });
});
