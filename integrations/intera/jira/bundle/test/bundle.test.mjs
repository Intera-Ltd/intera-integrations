import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');
const SERVICE_URL = 'http://jira-service.example.test:8080';
const SERVICE_TOKEN = 'svc-jira-token-test';
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
        data: [{ issue_key: 'ENG-10', summary: 'Example issue' }],
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
const issues = () => bundle.dataSources.find((s) => s.id === 'jira_issues');

describe('jira bundle contract', () => {
  it('exports manifest, dataSources, getDataSources, provisionInstance, testConnection, and webhook hooks', () => {
    assert.equal(bundle.manifest.id, 'intera-jira-integration');
    assert.equal(bundle.manifest.version, '3.4.0');
    assert.equal(bundle.manifest.requires.core, '>=0.5.94');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.deepEqual(bundle.manifest.capabilities, ['datasource', 'webhook']);
    assert.deepEqual(
      bundle.manifest.configSchema.map((f) => f.name),
      ['siteUrl', 'jqlScope'],
    );
    assert.equal(bundle.manifest.webhooks?.[0]?.key, 'oauth-callback');
    assert.ok(bundle.dataSources.length >= 10);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
    assert.equal(typeof bundle.provisionInstance, 'function');
    assert.equal(typeof bundle.registerWebhook, 'function');
    assert.equal(typeof bundle.onWebhook, 'function');
  });

  it('uses intera_service_url and intera_service_token, and forwards optional siteUrl and jqlScope', async () => {
    const rows = await issues().getData(
      {},
      context(baseConfig({ siteUrl: 'https://acme.atlassian.net', jqlScope: 'project = ENG' })),
    );
    assert.equal(rows.length, 1);
    assert.equal(calls[0].url, `${SERVICE_URL}/api/Source/issues/query`);
    assert.equal(calls[0].init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);
    assert.equal(calls[0].init.headers['X-Jira-Site'], 'https://acme.atlassian.net');
    assert.equal(calls[0].init.headers['X-Jira-Jql-Scope'], encodeURIComponent('project = ENG'));
  });

  it('validates service URL and fails fast on missing intera_service_token before making any request', async () => {
    await assert.rejects(issues().getData({}, context({})), /intera_service_url/);
    await assert.rejects(
      issues().getData({}, context({ intera_service_url: 'file:///etc/hosts', intera_service_token: SERVICE_TOKEN })),
      /http or https/,
    );
    await assert.rejects(
      issues().getData(
        {},
        context({ intera_service_url: 'http://169.254.169.254/latest/meta-data', intera_service_token: SERVICE_TOKEN }),
      ),
      /metadata/,
    );
    await assert.rejects(
      issues().getData({}, context({ intera_service_url: SERVICE_URL })),
      /redeploy the integration/,
    );
    await assert.rejects(
      issues().getData({}, context({ intera_service_url: SERVICE_URL, intera_service_token: '   ' })),
      /redeploy the integration/,
    );
    assert.equal(calls.length, 0);
  });

  it('never leaks intera_service_token or raw non-JSON upstream bodies in error messages', async () => {
    const secret = 'secret-jira-service-token';
    respond = () => new Response(`bad gateway ${secret}`, { status: 502 });
    await assert.rejects(
      issues().getData({}, context({ intera_service_url: SERVICE_URL, intera_service_token: secret })),
      (err) => {
        assert.match(err.message, /status 502/);
        assert.equal(err.message.includes(secret), false);
        return true;
      },
    );

    respond = () =>
      new Response(JSON.stringify({ ok: false, data: null, error: `rejected token ${secret}` }), { status: 401 });
    await assert.rejects(
      issues().getData({}, context({ intera_service_url: SERVICE_URL, intera_service_token: secret })),
      (err) => {
        assert.equal(err.message.includes(secret), false);
        return true;
      },
    );
  });

  it('forwards context.signal and sends Bearer token on connected testConnection + provisionInstance', async () => {
    const controller = new AbortController();
    await issues().getData({}, context(baseConfig(), { signal: controller.signal }));
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
            redirect_uri: 'https://core.example.test/api/webhooks/jira-1',
            redirect_uri_source: 'intera',
          }),
          { status: 200 },
        );
      }
      if (url.endsWith('/api/Schema/custom-fields')) {
        return new Response(
          JSON.stringify({ ok: true, data: [{ name: 'customfield_10042', label: 'Story Points', type: 'Number' }], error: null }),
          { status: 200 },
        );
      }
      return new Response(
        JSON.stringify({ ok: true, data: [{ jira_site_url: 'https://acme.atlassian.net', projects: 5 }], error: null }),
        { status: 200 },
      );
    };

    const conn = await bundle.testConnection({ config: baseConfig(), instanceId: null, instanceSlug: null });
    assert.equal(conn.status, 'ok');
    assert.match(conn.detail, /https:\/\/acme\.atlassian\.net \(5 projects\)/);

    const prov = await bundle.provisionInstance({ config: baseConfig(), instanceId: null, instanceSlug: null });
    assert.ok(prov.dataSources[0].fields.some((f) => f.name === 'customfield_10042'));
    assert.ok(calls.length >= 4);
    for (const call of calls) {
      assert.equal(call.init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);
    }
  });

  it('allows provisionInstance before OAuth sign-in by checking /api/Schema/sources and returning empty overrides', async () => {
    respond = (url) => {
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
      if (url.endsWith('/api/Schema/sources')) {
        return new Response(JSON.stringify({ ok: true, data: [{ id: 'issues' }], error: null }), { status: 200 });
      }
      throw new Error(`Unexpected URL ${url}`);
    };

    const prov = await bundle.provisionInstance({ config: baseConfig(), instanceId: null, instanceSlug: null });
    assert.deepEqual(prov, { dataSources: [] });
    assert.ok(calls.some((c) => c.url.endsWith('/api/Schema/sources')));
    for (const call of calls) {
      assert.equal(call.init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);
    }
  });

  it('returns needs_authorization with authorizeUrl and sends Bearer token on status, redirect-uri, authorize-url, and callback', async () => {
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
          JSON.stringify({ ok: true, data: { redirect_uri: 'https://core.example.test/api/webhooks/jira-1', source: 'intera' }, error: null }),
          { status: 200 },
        );
      }
      if (url.endsWith('/oauth/authorize-url')) {
        return new Response(
          JSON.stringify({ ok: true, data: { url: 'https://auth.atlassian.com/authorize?state=xyz' }, error: null }),
          { status: 200 },
        );
      }
      if (url.includes('/oauth/callback?')) {
        return new Response('<html><body>Jira connected</body></html>', {
          status: 200,
          headers: { 'content-type': 'text/html; charset=utf-8' },
        });
      }
      throw new Error(`Unexpected URL ${url}`);
    };

    const conn = await bundle.testConnection({
      config: baseConfig(),
      instanceId: 'inst-jira',
      instanceSlug: 'jira',
      webhookUrls: [{ handlerKey: 'oauth-callback', url: 'https://core.example.test/api/webhooks/jira-1' }],
    });
    assert.equal(conn.status, 'needs_authorization');
    assert.equal(conn.authorizeUrl, 'https://auth.atlassian.com/authorize?state=xyz');

    const reg = await bundle.registerWebhook({
      config: baseConfig(),
      handlerKey: 'oauth-callback',
      webhookUrl: 'https://core.example.test/api/webhooks/jira-1',
      publicId: 'jira-1',
    });
    assert.equal(reg.ok, true);

    const relayed = await bundle.onWebhook(
      {
        method: 'GET',
        rawQuery: 'code=jira-code&state=xyz',
        headers: {},
        rawBody: Buffer.alloc(0),
        sourceIp: '127.0.0.1',
        receivedAt: new Date(),
      },
      {
        config: baseConfig(),
        handlerKey: 'oauth-callback',
        webhookUrl: 'https://core.example.test/api/webhooks/jira-1',
      },
    );
    assert.equal(relayed.status, 200);
    assert.match(String(relayed.body), /Jira connected/);

    assert.ok(calls.some((c) => c.url.endsWith('/oauth/status')));
    assert.ok(calls.some((c) => c.url.includes('/oauth/callback?')));
    for (const call of calls) {
      assert.equal(call.init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);
    }
  });
});
