import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { afterEach, beforeEach, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const __dirname = path.dirname(fileURLToPath(import.meta.url));
const bundle = require('../dist/index.js');
const SERVICE_URL = 'http://teamcity-service.example.test:8080';
const SERVICE_TOKEN = 'svc-token-teamcity-54321';

let calls;
let respond;
const realFetch = globalThis.fetch;

beforeEach(() => {
  calls = [];
  respond = () =>
    new Response(
      JSON.stringify({
        ok: true,
        data: [{ build_type_id: 'Core_Ci', build_type_name: 'CI', last_build_status: 'SUCCESS' }],
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
const buildTypes = () => bundle.dataSources.find((s) => s.id === 'teamcity_build_types');

describe('teamcity bundle contract', () => {
  it('exports manifest with empty configSchema, copied icon asset, 2 dataSources, getDataSources, and testConnection', () => {
    assert.equal(bundle.manifest.id, 'intera-teamcity-integration');
    assert.equal(bundle.manifest.version, '3.2.0');
    assert.equal(bundle.manifest.requires.core, '>=0.5.94');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.equal(bundle.manifest.iconFile, 'assets/icon.svg');
    assert.deepEqual(bundle.manifest.configSchema, []);
    assert.equal(bundle.dataSources.length, 2);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
    assert.equal(fs.existsSync(path.join(__dirname, '..', 'dist', 'assets', 'icon.svg')), true);
  });

  it('validates intera_service_url and intera_service_token before making any request', async () => {
    await assert.rejects(buildTypes().getData({}, context({})), /redeploy the integration/i);
    await assert.rejects(
      buildTypes().getData({}, context({ intera_service_url: SERVICE_URL })),
      /redeploy the integration/i,
    );
    await assert.rejects(
      buildTypes().getData({}, context({ intera_service_url: 'ftp://example.test', intera_service_token: SERVICE_TOKEN })),
      /http or https/i,
    );
    await assert.rejects(
      buildTypes().getData({}, context({ intera_service_url: 'http://169.254.169.254/latest', intera_service_token: SERVICE_TOKEN })),
      /link-local/i,
    );
    assert.equal(calls.length, 0);
  });

  it('sends Authorization Bearer token and returns paged { rows, total } when X-Total-Count is set', async () => {
    respond = () =>
      new Response(
        JSON.stringify({ ok: true, data: [{ build_type_id: 'Core_Ci' }], error: null }),
        { status: 200, headers: { 'X-Total-Count': '27' } },
      );

    const page = await buildTypes().getData({ offset: 0, limit: 10 }, context(validConfig()));
    assert.deepEqual(page, { rows: [{ build_type_id: 'Core_Ci' }], total: 27 });
    assert.equal(calls[0].url, `${SERVICE_URL}/api/BuildType/query`);
    assert.equal(calls[0].init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);

    const body = JSON.parse(calls[0].init.body);
    assert.equal(body.skip, 0);
    assert.equal(body.top, 10);
  });

  it('never echoes raw HTML/non-JSON upstream bodies or secret tokens in error messages', async () => {
    respond = () => new Response(`<html>502 Bad Gateway ${SERVICE_TOKEN}</html>`, { status: 502 });
    await assert.rejects(
      buildTypes().getData({}, context(validConfig())),
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
    await buildTypes().getData({}, context(validConfig(), { signal: controller.signal }));
    assert.equal(calls[0].init.signal.aborted, false);
    controller.abort();
    assert.equal(calls[0].init.signal.aborted, true);

    respond = () =>
      new Response(JSON.stringify({ ok: true, data: [{ build_configurations: 8 }], error: null }), { status: 200 });
    const res = await bundle.testConnection({
      config: validConfig(),
      instanceId: null,
      instanceSlug: null,
    });
    assert.equal(res.detail, 'TeamCity: 8 build configurations in scope');
    assert.equal(calls[1].init.headers.Authorization, `Bearer ${SERVICE_TOKEN}`);
  });
});
