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
  it('exports manifest, dataSources, getDataSources, testConnection, and webhook hooks', () => {
    assert.equal(bundle.manifest.id, 'intera-xero-integration');
    assert.equal(bundle.manifest.version, '3.3.0');
    assert.equal(bundle.manifest.requires.core, '>=0.5.25');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.deepEqual(bundle.manifest.capabilities, ['datasource', 'webhook']);
    assert.equal(bundle.manifest.webhooks?.[0]?.key, 'oauth-callback');
    assert.ok(bundle.dataSources.length >= 15);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
    assert.equal(typeof bundle.registerWebhook, 'function');
    assert.equal(typeof bundle.onWebhook, 'function');
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

  it('forwards context.signal to fetch and supports connected testConnection', async () => {
    const controller = new AbortController();
    await invoices().getData({}, context({ intera_service_url: SERVICE_URL }, { signal: controller.signal }));
    assert.equal(calls[0].init.signal.aborted, false);
    controller.abort();
    assert.equal(calls[0].init.signal.aborted, true);

    respond = (url) => {
      if (url.endsWith('/oauth/status')) {
        return new Response(
          JSON.stringify({
            configured: true,
            has_access_token: true,
            has_refresh_token: true,
            redirect_uri: 'https://core.example.test/api/webhooks/xero-1',
            redirect_uri_source: 'intera',
          }),
          { status: 200 },
        );
      }
      return new Response(JSON.stringify({ ok: true, data: [{ name: 'Acme Books Ltd', is_demo_company: false }], error: null }), {
        status: 200,
      });
    };
    const res = await bundle.testConnection({
      config: { intera_service_url: SERVICE_URL },
      instanceId: null,
      instanceSlug: null,
    });
    assert.equal(res.status, 'ok');
    assert.match(res.detail, /Xero: Acme Books Ltd/);
  });

  it('returns needs_authorization with authorizeUrl and auto-registers webhookUrl when unsigned', async () => {
    respond = (url, init) => {
      if (url.endsWith('/oauth/status')) {
        return new Response(
          JSON.stringify({
            configured: true,
            has_access_token: false,
            has_refresh_token: false,
            redirect_uri: '',
            redirect_uri_source: null,
          }),
          { status: 200 },
        );
      }
      if (url.endsWith('/oauth/redirect-uri') && init.method === 'PUT') {
        return new Response(
          JSON.stringify({ ok: true, data: { redirect_uri: 'https://core.example.test/api/webhooks/xero-1', source: 'intera' }, error: null }),
          { status: 200 },
        );
      }
      if (url.endsWith('/oauth/authorize-url')) {
        return new Response(
          JSON.stringify({ ok: true, data: { url: 'https://login.xero.com/identity/connect/authorize?state=abc' }, error: null }),
          { status: 200 },
        );
      }
      throw new Error(`Unexpected URL ${url}`);
    };

    const res = await bundle.testConnection({
      config: { intera_service_url: SERVICE_URL },
      instanceId: 'inst-1',
      instanceSlug: 'xero',
      webhookUrls: [{ handlerKey: 'oauth-callback', url: 'https://core.example.test/api/webhooks/xero-1' }],
    });

    assert.equal(res.status, 'needs_authorization');
    assert.equal(res.authorizeUrl, 'https://login.xero.com/identity/connect/authorize?state=abc');
    assert.ok(calls.some((c) => c.url.endsWith('/oauth/redirect-uri') && c.init.method === 'PUT'));
  });

  it('fails with clear container env var message when OAuth client is not configured', async () => {
    respond = () =>
      new Response(
        JSON.stringify({
          configured: false,
          has_access_token: false,
          has_refresh_token: false,
          redirect_uri: '',
          redirect_uri_source: null,
        }),
        { status: 200 },
      );

    await assert.rejects(
      bundle.testConnection({ config: { intera_service_url: SERVICE_URL }, instanceId: null, instanceSlug: null }),
      /XERO__CLIENTID.*XERO__CLIENTSECRET/,
    );
  });

  it('supports registerWebhook and onWebhook OAuth callback relay', async () => {
    respond = (url) => {
      if (url.endsWith('/oauth/redirect-uri')) {
        return new Response(
          JSON.stringify({ ok: true, data: { redirect_uri: 'https://core.example.test/api/webhooks/xero-1', source: 'intera' }, error: null }),
          { status: 200 },
        );
      }
      if (url.includes('/oauth/callback?')) {
        return new Response('<html><body>Xero connected</body></html>', {
          status: 200,
          headers: { 'content-type': 'text/html; charset=utf-8' },
        });
      }
      throw new Error(`Unexpected URL ${url}`);
    };

    const reg = await bundle.registerWebhook({
      config: { intera_service_url: SERVICE_URL },
      handlerKey: 'oauth-callback',
      webhookUrl: 'https://core.example.test/api/webhooks/xero-1',
      publicId: 'xero-1',
    });
    assert.equal(reg.ok, true);

    const relayed = await bundle.onWebhook(
      {
        method: 'GET',
        rawQuery: 'code=auth-code-1&state=state-1&ignored=drop',
        headers: {},
        rawBody: Buffer.alloc(0),
        sourceIp: '127.0.0.1',
        receivedAt: new Date(),
      },
      {
        config: { intera_service_url: SERVICE_URL },
        handlerKey: 'oauth-callback',
        webhookUrl: 'https://core.example.test/api/webhooks/xero-1',
      },
    );
    assert.equal(relayed.status, 200);
    assert.match(String(relayed.body), /Xero connected/);
    assert.ok(calls.some((c) => c.url === `${SERVICE_URL}/oauth/callback?code=auth-code-1&state=state-1`));
  });
});
