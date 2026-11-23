import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');
const SERVICE_URL = 'http://jira-service.example.test:8080';

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
  it('exports manifest, dataSources, getDataSources, provisionInstance, and testConnection', () => {
    assert.equal(bundle.manifest.id, 'intera-jira-integration');
    assert.equal(bundle.manifest.version, '3.2.0');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.ok(bundle.dataSources.length >= 10);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
    assert.equal(typeof bundle.provisionInstance, 'function');
  });

  it('uses intera_service_url and forwards optional siteUrl, jqlScope, and accessToken', async () => {
    const rows = await issues().getData(
      {},
      context({ intera_service_url: SERVICE_URL, siteUrl: 'https://acme.atlassian.net', jqlScope: 'project = ENG' }),
    );
    assert.equal(rows.length, 1);
    assert.equal(calls[0].url, `${SERVICE_URL}/api/Source/issues/query`);
    assert.equal(calls[0].init.headers.Authorization, undefined);
    assert.equal(calls[0].init.headers['X-Jira-Site'], 'https://acme.atlassian.net');
    assert.equal(calls[0].init.headers['X-Jira-Jql-Scope'], encodeURIComponent('project = ENG'));

    await issues().getData({}, context({ apiBaseUrl: 'https://jira-proxy.example.test', accessToken: 'tok-jira' }));
    assert.equal(calls[1].url, 'https://jira-proxy.example.test/api/Source/issues/query');
    assert.equal(calls[1].init.headers.Authorization, 'Bearer tok-jira');
  });

  it('validates service URL before making any request', async () => {
    await assert.rejects(issues().getData({}, context({})), /intera_service_url/);
    await assert.rejects(issues().getData({}, context({ intera_service_url: 'file:///etc/hosts' })), /http or https/);
    assert.equal(calls.length, 0);
  });

  it('never leaks secret tokens or raw non-JSON upstream bodies in error messages', async () => {
    const secret = 'secret-jira-inbound-token';
    respond = () => new Response(`bad gateway ${secret}`, { status: 502 });
    await assert.rejects(
      issues().getData({}, context({ intera_service_url: SERVICE_URL, accessToken: secret })),
      (err) => {
        assert.match(err.message, /status 502/);
        assert.equal(err.message.includes(secret), false);
        return true;
      },
    );
  });

  it('forwards context.signal and supports testConnection + provisionInstance', async () => {
    const controller = new AbortController();
    await issues().getData({}, context({ intera_service_url: SERVICE_URL }, { signal: controller.signal }));
    assert.equal(calls[0].init.signal.aborted, false);
    controller.abort();
    assert.equal(calls[0].init.signal.aborted, true);

    respond = (url) => {
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

    const conn = await bundle.testConnection({ config: { intera_service_url: SERVICE_URL }, instanceId: null, instanceSlug: null });
    assert.match(conn.detail, /https:\/\/acme\.atlassian\.net \(5 projects\)/);

    const prov = await bundle.provisionInstance({ config: { intera_service_url: SERVICE_URL }, instanceId: null, instanceSlug: null });
    assert.ok(prov.dataSources[0].fields.some((f) => f.name === 'customfield_10042'));
  });
});
