import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');
const SERVICE_URL = 'http://zoho-inventory-service.example.test:8080';
const SERVICE_TOKEN = 'svc-zoho-token-test';
const baseConfig = (extra = {}) => ({
  intera_service_url: SERVICE_URL,
  intera_service_token: SERVICE_TOKEN,
  ...extra,
});

let calls;
let respond;
const realFetch = globalThis.fetch;

beforeEach(() => {
  calls = [];
  respond = () =>
    new Response(
      JSON.stringify({
        ok: true,
        data: [{ customer_id: 'c-1', customer_name: 'Example Supply Co' }],
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
const customers = () => bundle.dataSources.find((s) => s.id === 'zoho_inventory_customers');

describe('zoho-inventory bundle contract', () => {
  it('exports manifest, 22 dataSources, getDataSources, provisionInstance, and testConnection', () => {
    assert.equal(bundle.manifest.id, 'intera-zoho-inventory-integration');
    assert.equal(bundle.manifest.version, '3.4.0');
    assert.equal(bundle.manifest.requires.core, '>=0.5.94');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.deepEqual(bundle.manifest.configSchema, []);
    assert.equal(bundle.dataSources.length, 22);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
    assert.equal(typeof bundle.provisionInstance, 'function');
  });

  it('uses intera_service_url and sends Bearer intera_service_token on every request', async () => {
    const rows = await customers().getData({}, context(baseConfig()));
    assert.equal(rows.length, 1);
    assert.equal(calls[0].url, `${SERVICE_URL}/api/customer/query`);
    assert.equal(calls[0].init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);
  });

  it('validates service URL and fails fast on missing intera_service_token before making any request', async () => {
    await assert.rejects(customers().getData({}, context({})), /intera_service_url/);
    await assert.rejects(
      customers().getData({}, context({ intera_service_url: 'ftp://bad.example.test', intera_service_token: SERVICE_TOKEN })),
      /http or https/,
    );
    await assert.rejects(
      customers().getData(
        {},
        context({ intera_service_url: 'http://169.254.169.254/latest/meta-data', intera_service_token: SERVICE_TOKEN }),
      ),
      /metadata/,
    );
    await assert.rejects(
      customers().getData({}, context({ intera_service_url: SERVICE_URL })),
      /redeploy the integration/,
    );
    await assert.rejects(
      customers().getData({}, context({ intera_service_url: SERVICE_URL, intera_service_token: ' ' })),
      /redeploy the integration/,
    );
    assert.equal(calls.length, 0);
  });

  it('never leaks intera_service_token or raw non-JSON upstream bodies in error messages', async () => {
    const secret = 'secret-zoho-service-token';
    respond = () => new Response(`upstream html ${secret}`, { status: 500 });
    await assert.rejects(
      customers().getData({}, context({ intera_service_url: SERVICE_URL, intera_service_token: secret })),
      (err) => {
        assert.match(err.message, /status 500/);
        assert.equal(err.message.includes(secret), false);
        return true;
      },
    );

    respond = () =>
      new Response(JSON.stringify({ ok: false, data: null, error: `denied ${secret}` }), { status: 401 });
    await assert.rejects(
      customers().getData({}, context({ intera_service_url: SERVICE_URL, intera_service_token: secret })),
      (err) => {
        assert.equal(err.message.includes(secret), false);
        return true;
      },
    );
  });

  it('forwards context.signal and sends Bearer token on testConnection + provisionInstance', async () => {
    const controller = new AbortController();
    await customers().getData({}, context(baseConfig(), { signal: controller.signal }));
    assert.equal(calls[0].init.signal.aborted, false);
    controller.abort();
    assert.equal(calls[0].init.signal.aborted, true);

    calls = [];
    respond = (url) => {
      if (url.endsWith('/api/organization')) {
        return new Response(
          JSON.stringify({ ok: true, data: { organization_id: 'org-1', name: 'Example Logistics', currency_code: 'EUR' }, error: null }),
          { status: 200 },
        );
      }
      return new Response(
        JSON.stringify({
          ok: true,
          data: { custom_fields: { customers: [{ name: 'cf_tier', label: 'Tier', type: 'string' }] } },
          error: null,
        }),
        { status: 200 },
      );
    };

    const conn = await bundle.testConnection({ config: baseConfig(), instanceId: null, instanceSlug: null });
    assert.match(conn.detail, /Example Logistics, EUR/);

    const prov = await bundle.provisionInstance({ config: baseConfig(), instanceId: null, instanceSlug: null });
    const provCust = prov.dataSources.find((d) => d.id === 'zoho_inventory_customers');
    assert.ok(provCust.fields.some((f) => f.name === 'cf_tier'));
    assert.equal(calls.length, 2);
    for (const call of calls) {
      assert.equal(call.init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);
    }
  });
});
