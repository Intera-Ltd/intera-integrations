import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { afterEach, beforeEach, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');
const SERVICE_URL = 'http://eurobank-service.example.test:8080';
const SERVICE_TOKEN = 'svc-token-eurobank-12345';
const BASE_CONFIG = {
  intera_service_url: SERVICE_URL,
  intera_service_token: SERVICE_TOKEN,
};

let calls;
let respond;
const realFetch = globalThis.fetch;

beforeEach(() => {
  calls = [];
  respond = () =>
    new Response(
      JSON.stringify({
        ok: true,
        data: [
          {
            account_name: 'Operating EUR',
            iban: 'CY000000000000000001',
            currency: 'EUR',
            available_balance: 5400,
            current_balance: 5400,
            status: 'ACTIVE',
          },
        ],
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

const context = (config = BASE_CONFIG, extra = {}) => ({ userId: 'u1', roles: ['admin'], config, ...extra });
const accounts = () => bundle.dataSources.find((s) => s.id === 'eurobank-accounts');

describe('eurobank bundle contract', () => {
  it('exports manifest with zero manual configSchema fields, icon, dataSources, testConnection, and webhook hooks', () => {
    assert.equal(bundle.manifest.id, 'intera-eurobank-integration');
    assert.equal(bundle.manifest.name, 'Eurobank');
    assert.equal(bundle.manifest.version, '3.2.0');
    assert.equal(bundle.manifest.requires.core, '>=0.5.94');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.deepEqual(bundle.manifest.capabilities, ['datasource', 'webhook']);
    assert.deepEqual(bundle.manifest.configSchema, []);
    assert.deepEqual(bundle.manifest.webhooks, [
      { key: 'oauth-callback', methods: ['GET'], registration: 'auto' },
    ]);
    assert.equal(bundle.manifest.iconFile, 'assets/icon.svg');
    const distIcon = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', 'dist', 'assets', 'icon.svg');
    assert.equal(fs.existsSync(distIcon), true);
    assert.equal(bundle.dataSources.length, 1);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
    assert.equal(typeof bundle.registerWebhook, 'function');
    assert.equal(typeof bundle.onWebhook, 'function');
  });

  it('requires intera_service_url and intera_service_token before making any request', async () => {
    await assert.rejects(accounts().getData({}, context({})), /intera_service_url/);
    await assert.rejects(
      accounts().getData({}, context({ intera_service_url: SERVICE_URL })),
      /redeploy the integration/,
    );
    await assert.rejects(
      accounts().getData({}, context({ intera_service_url: 'javascript:alert(1)', intera_service_token: SERVICE_TOKEN })),
      /http or https/,
    );
    assert.equal(calls.length, 0);
  });

  it('sends Authorization Bearer token on data queries and supports X-Total-Count paging', async () => {
    respond = () =>
      new Response(
        JSON.stringify({
          ok: true,
          data: [{ account_name: 'Operating EUR', iban: 'CY000000000000000001' }],
          error: null,
        }),
        { status: 200, headers: { 'X-Total-Count': '4' } },
      );

    const page = await accounts().getData({ limit: 2, offset: 0 }, context());
    assert.deepEqual(page, {
      rows: [{ account_name: 'Operating EUR', iban: 'CY000000000000000001' }],
      total: 4,
    });
    assert.equal(calls[0].url, `${SERVICE_URL}/api/eurobank/accounts/query`);
    assert.equal(calls[0].init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);
  });

  it('never echoes raw non-JSON upstream bodies or secret tokens in error messages', async () => {
    const secret = 'secret-eurobank-token-value';
    respond = () => new Response(`bad gateway ${secret}`, { status: 502 });
    await assert.rejects(
      accounts().getData({}, context({ intera_service_url: SERVICE_URL, intera_service_token: secret })),
      (err) => {
        assert.match(err.message, /status 502/);
        assert.equal(err.message.includes(secret), false);
        return true;
      },
    );
  });

  it('forwards context.signal to fetch and supports connected testConnection with Bearer auth', async () => {
    const controller = new AbortController();
    await accounts().getData({}, context(BASE_CONFIG, { signal: controller.signal }));
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
            redirect_uri: 'https://core.example.test/api/webhooks/eurobank-1',
            redirect_uri_source: 'intera',
          }),
          { status: 200 },
        );
      }
      return new Response(
        JSON.stringify({ ok: true, data: [{ account_name: 'Operating EUR' }], error: null }),
        { status: 200 },
      );
    };

    const res = await bundle.testConnection({
      config: BASE_CONFIG,
      instanceId: null,
      instanceSlug: null,
    });
    assert.equal(res.status, 'ok');
    assert.match(res.detail, /Eurobank: 1 account\(s\)/);
    assert.ok(calls.every((c) => c.init.headers.Authorization === `Bearer ${SERVICE_TOKEN}`));
  });

  it('returns needs_authorization with authorizeUrl, auto-registers webhookUrl, and sends Bearer auth on every call', async () => {
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
          JSON.stringify({
            ok: true,
            data: { redirect_uri: 'https://core.example.test/api/webhooks/eurobank-1', source: 'intera' },
            error: null,
          }),
          { status: 200 },
        );
      }
      if (url.endsWith('/oauth/authorize-url')) {
        return new Response(
          JSON.stringify({
            ok: true,
            data: {
              url: 'https://oauth.hellenicbank.com/v2/oauth2/auth?response_type=code&client_id=cid&state=abc',
              redirect_uri: 'https://core.example.test/api/webhooks/eurobank-1',
            },
            error: null,
          }),
          { status: 200 },
        );
      }
      throw new Error(`Unexpected URL ${url}`);
    };

    const res = await bundle.testConnection({
      config: BASE_CONFIG,
      instanceId: 'inst-1',
      instanceSlug: 'eurobank',
      webhookUrls: [{ handlerKey: 'oauth-callback', url: 'https://core.example.test/api/webhooks/eurobank-1' }],
    });

    assert.equal(res.status, 'needs_authorization');
    assert.equal(
      res.authorizeUrl,
      'https://oauth.hellenicbank.com/v2/oauth2/auth?response_type=code&client_id=cid&state=abc',
    );
    assert.equal(calls.length, 3);
    assert.ok(calls.every((c) => c.init.headers.Authorization === `Bearer ${SERVICE_TOKEN}`));
  });

  it('supports registerWebhook and onWebhook OAuth callback relay with Bearer auth', async () => {
    respond = (url) => {
      if (url.endsWith('/oauth/redirect-uri')) {
        return new Response(
          JSON.stringify({
            ok: true,
            data: { redirect_uri: 'https://core.example.test/api/webhooks/eurobank-1', source: 'intera' },
            error: null,
          }),
          { status: 200 },
        );
      }
      if (url.includes('/oauth/callback?')) {
        return new Response('<html><body>Eurobank connected</body></html>', {
          status: 200,
          headers: { 'content-type': 'text/html; charset=utf-8' },
        });
      }
      throw new Error(`Unexpected URL ${url}`);
    };

    const reg = await bundle.registerWebhook({
      config: BASE_CONFIG,
      handlerKey: 'oauth-callback',
      webhookUrl: 'https://core.example.test/api/webhooks/eurobank-1',
      publicId: 'eurobank-1',
    });
    assert.equal(reg.ok, true);

    const relayed = await bundle.onWebhook(
      {
        method: 'GET',
        rawQuery: 'code=auth-code-1&state=state-1&extra=drop',
        headers: {},
        rawBody: Buffer.alloc(0),
        sourceIp: '127.0.0.1',
        receivedAt: new Date(),
      },
      {
        config: BASE_CONFIG,
        handlerKey: 'oauth-callback',
        webhookUrl: 'https://core.example.test/api/webhooks/eurobank-1',
      },
    );
    assert.equal(relayed.status, 200);
    assert.match(String(relayed.body), /Eurobank connected/);
    assert.ok(calls.some((c) => c.url === `${SERVICE_URL}/oauth/callback?code=auth-code-1&state=state-1`));
    assert.ok(calls.every((c) => c.init.headers.Authorization === `Bearer ${SERVICE_TOKEN}`));
  });
});
