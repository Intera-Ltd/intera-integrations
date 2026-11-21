import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');
const SERVICE_URL = 'http://zendesk-service.example.test:8080';

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
    assert.equal(bundle.manifest.version, '4.1.0');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.ok(bundle.dataSources.length >= 10);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
    assert.equal(typeof bundle.provisionInstance, 'function');
    assert.equal(typeof bundle.registerWebhook, 'function');
    assert.equal(typeof bundle.onWebhook, 'function');
  });

  it('uses intera_service_url by default and supports apiBaseUrl + accessToken override', async () => {
    const rows = await tickets().getData({}, context({ intera_service_url: SERVICE_URL }));
    assert.equal(rows.length, 1);
    assert.equal(calls[0].url, `${SERVICE_URL}/api/Source/tickets/query`);
    assert.equal(calls[0].init.headers.Authorization, undefined);

    await tickets().getData({}, context({ apiBaseUrl: 'https://zendesk-proxy.example.test', accessToken: 'tok-zd' }));
    assert.equal(calls[1].url, 'https://zendesk-proxy.example.test/api/Source/tickets/query');
    assert.equal(calls[1].init.headers.Authorization, 'Bearer tok-zd');
  });

  it('validates service URL before making any request', async () => {
    await assert.rejects(tickets().getData({}, context({})), /intera_service_url/);
    await assert.rejects(tickets().getData({}, context({ intera_service_url: 'ftp://bad.example.test' })), /http or https/);
    assert.equal(calls.length, 0);
  });

  it('never leaks secret tokens or raw non-JSON upstream bodies in error messages', async () => {
    const secret = 'secret-zendesk-inbound-token';
    respond = () => new Response(`bad gateway ${secret}`, { status: 502 });
    await assert.rejects(
      tickets().getData({}, context({ intera_service_url: SERVICE_URL, accessToken: secret })),
      (err) => {
        assert.match(err.message, /status 502/);
        assert.equal(err.message.includes(secret), false);
        return true;
      },
    );
  });

  it('forwards context.signal and supports testConnection + registerWebhook', async () => {
    const controller = new AbortController();
    await tickets().getData({}, context({ intera_service_url: SERVICE_URL }, { signal: controller.signal }));
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
      return new Response(
        JSON.stringify({ ok: true, data: [{ zendesk_subdomain: 'acme', groups: 4 }], error: null }),
        { status: 200 },
      );
    };

    const conn = await bundle.testConnection({ config: { intera_service_url: SERVICE_URL }, instanceId: null, instanceSlug: null });
    assert.match(conn.detail, /Zendesk: acme \(4 groups\)/);

    const reg = await bundle.registerWebhook({
      config: { intera_service_url: SERVICE_URL },
      handlerKey: 'oauth-callback',
      webhookUrl: 'https://core.example.test/api/webhooks/123',
      publicId: '123',
    });
    assert.equal(reg.ok, true);
  });
});
