import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');
const SERVICE_URL = 'http://zendesk-service.example.test:8080';
const SERVICE_TOKEN = 'svc-zendesk-token-test';
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
        data: [{ ticket_id: 1001, subject: 'Example support ticket' }],
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
const tickets = () => bundle.dataSources.find((s) => s.id === 'zendesk_tickets');

describe('zendesk bundle contract', () => {
  it('exports manifest, dataSources, getDataSources, provisionInstance, testConnection, and webhook hooks', () => {
    assert.equal(bundle.manifest.id, 'intera-zendesk-integration');
    assert.equal(bundle.manifest.version, '4.2.0');
    assert.equal(bundle.manifest.requires.core, '>=0.5.94');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.deepEqual(bundle.manifest.configSchema, []);
    assert.ok(bundle.dataSources.length >= 10);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
    assert.equal(typeof bundle.provisionInstance, 'function');
    assert.equal(typeof bundle.registerWebhook, 'function');
    assert.equal(typeof bundle.onWebhook, 'function');
  });

  it('uses intera_service_url and sends Bearer intera_service_token on data queries', async () => {
    const rows = await tickets().getData({}, context(baseConfig()));
    assert.equal(rows.length, 1);
    assert.equal(calls[0].url, `${SERVICE_URL}/api/Source/tickets/query`);
    assert.equal(calls[0].init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);
  });

  it('validates service URL and fails fast on missing intera_service_token before making any request', async () => {
    await assert.rejects(tickets().getData({}, context({})), /intera_service_url/);
    await assert.rejects(
      tickets().getData({}, context({ intera_service_url: 'ftp://bad.example.test', intera_service_token: SERVICE_TOKEN })),
      /http or https/,
    );
    await assert.rejects(
      tickets().getData(
        {},
        context({ intera_service_url: 'http://169.254.169.254/latest/meta-data', intera_service_token: SERVICE_TOKEN }),
      ),
      /metadata/,
    );
    await assert.rejects(
      tickets().getData({}, context({ intera_service_url: SERVICE_URL })),
      /redeploy the integration/,
    );
    await assert.rejects(
      tickets().getData({}, context({ intera_service_url: SERVICE_URL, intera_service_token: ' ' })),
      /redeploy the integration/,
    );
    assert.equal(calls.length, 0);
  });

  it('never leaks intera_service_token or raw non-JSON upstream bodies in error messages', async () => {
    const secret = 'secret-zendesk-service-token';
    respond = () => new Response(`bad gateway ${secret}`, { status: 502 });
    await assert.rejects(
      tickets().getData({}, context({ intera_service_url: SERVICE_URL, intera_service_token: secret })),
      (err) => {
        assert.match(err.message, /status 502/);
        assert.equal(err.message.includes(secret), false);
        return true;
      },
    );

    respond = () =>
      new Response(JSON.stringify({ ok: false, data: null, error: `unauthorized ${secret}` }), { status: 401 });
    await assert.rejects(
      tickets().getData({}, context({ intera_service_url: SERVICE_URL, intera_service_token: secret })),
      (err) => {
        assert.equal(err.message.includes(secret), false);
        return true;
      },
    );
  });

  it('forwards context.signal and sends Bearer token on testConnection + registerWebhook + onWebhook', async () => {
    const controller = new AbortController();
    await tickets().getData({}, context(baseConfig(), { signal: controller.signal }));
    assert.equal(calls[0].init.signal.aborted, false);
    controller.abort();
    assert.equal(calls[0].init.signal.aborted, true);

    calls = [];
    respond = (url) => {
      if (url.endsWith('/oauth/status')) {
        return new Response(
          JSON.stringify({
            configured: true,
            has_access_token: true,
            has_refresh_token: true,
            redirect_uri: 'https://core.example.test/api/webhooks/123',
            redirect_uri_source: 'intera',
          }),
          { status: 200 },
        );
      }
      if (url.endsWith('/oauth/redirect-uri')) {
        return new Response(JSON.stringify({ ok: true, data: { redirect_uri: 'https://core.example.test/api/webhooks/123' }, error: null }), {
          status: 200,
        });
      }
      if (url.includes('/oauth/callback?')) {
        return new Response('<html><body>Zendesk connected</body></html>', {
          status: 200,
          headers: { 'content-type': 'text/html; charset=utf-8' },
        });
      }
      return new Response(
        JSON.stringify({ ok: true, data: [{ zendesk_subdomain: 'acme', groups: 4 }], error: null }),
        { status: 200 },
      );
    };

    const conn = await bundle.testConnection({ config: baseConfig(), instanceId: null, instanceSlug: null });
    assert.equal(conn.status, 'ok');
    assert.match(conn.detail, /Zendesk: acme \(4 groups\)/);

    const reg = await bundle.registerWebhook({
      config: baseConfig(),
      handlerKey: 'oauth-callback',
      webhookUrl: 'https://core.example.test/api/webhooks/123',
      publicId: '123',
    });
    assert.equal(reg.ok, true);

    const relayed = await bundle.onWebhook(
      {
        method: 'GET',
        rawQuery: 'code=zd-code&state=zd-state',
        headers: {},
        rawBody: Buffer.alloc(0),
        sourceIp: '127.0.0.1',
        receivedAt: new Date(),
      },
      {
        config: baseConfig(),
        handlerKey: 'oauth-callback',
        webhookUrl: 'https://core.example.test/api/webhooks/123',
      },
    );
    assert.equal(relayed.status, 200);
    assert.match(String(relayed.body), /Zendesk connected/);

    assert.ok(calls.some((c) => c.url.endsWith('/oauth/status')));
    assert.ok(calls.some((c) => c.url.includes('/oauth/callback?')));
    for (const call of calls) {
      assert.equal(call.init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);
    }
  });

  it('returns needs_authorization with authorizeUrl and names ZENDESK__CLIENTID/CLIENTSECRET when unconfigured', async () => {
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
          JSON.stringify({ ok: true, data: { redirect_uri: 'https://core.example.test/api/webhooks/123', source: 'intera' }, error: null }),
          { status: 200 },
        );
      }
      if (url.endsWith('/oauth/authorize-url')) {
        return new Response(
          JSON.stringify({ ok: true, data: { url: 'https://acme.zendesk.com/oauth/authorizations/new?state=zd1' }, error: null }),
          { status: 200 },
        );
      }
      throw new Error(`Unexpected URL ${url}`);
    };

    const pending = await bundle.testConnection({
      config: baseConfig(),
      instanceId: 'inst-zd',
      instanceSlug: 'zendesk',
      webhookUrls: [{ handlerKey: 'oauth-callback', url: 'https://core.example.test/api/webhooks/123' }],
    });
    assert.equal(pending.status, 'needs_authorization');
    assert.equal(pending.authorizeUrl, 'https://acme.zendesk.com/oauth/authorizations/new?state=zd1');
    for (const call of calls) {
      assert.equal(call.init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);
    }

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
      bundle.testConnection({ config: baseConfig(), instanceId: null, instanceSlug: null }),
      /ZENDESK__CLIENTID.*ZENDESK__CLIENTSECRET/,
    );
  });
});
