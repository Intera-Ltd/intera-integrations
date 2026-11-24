// Unit tests for the built bundle (run `npm test`, which builds first).
// They load dist/index.js the way Core does and replace global fetch with a mock.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');
const SERVICE_URL = 'http://hello-dotnet.test:8080';

const ITEMS = [
  { id: 'item-001', name: 'Starter plan', amount: 49.9, updatedAt: '2026-01-15T09:30:00+00:00' },
  { id: 'item-002', name: 'Team plan', amount: 199, updatedAt: '2026-02-03T14:05:00+00:00' },
  { id: 'item-003', name: 'Enterprise plan', amount: 1250.5, updatedAt: '2026-03-21T08:00:00+00:00' },
];

let calls;
let respond;
const realFetch = globalThis.fetch;

beforeEach(() => {
  calls = [];
  respond = () => new Response(JSON.stringify(ITEMS), { status: 200 });
  globalThis.fetch = async (url, init) => {
    calls.push({ url: String(url), init });
    return respond(String(url));
  };
});

afterEach(() => {
  globalThis.fetch = realFetch;
});

const context = (config) => ({ userId: 'u1', roles: ['admin'], config });
const items = () => bundle.dataSources.find((source) => source.id === 'items');

describe('contract exports', () => {
  it('exposes the manifest Core expects', () => {
    assert.equal(bundle.manifest.id, 'hello_dotnet');
    assert.equal(bundle.manifest.version, '1.0.1');
    assert.deepEqual(bundle.manifest.capabilities, ['datasource']);
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.equal(typeof bundle.testConnection, 'function');
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
  });

  it('declares the items data source with an identity field', () => {
    const source = items();
    assert.equal(source.name, 'Hello .NET items');
    assert.deepEqual(
      source.fields.map((field) => [field.name, field.type]),
      [['id', 'string'], ['name', 'string'], ['amount', 'number'], ['updatedAt', 'date']],
    );
    assert.equal(source.fields.filter((field) => field.identity).length, 1);
  });
});

describe('items data source', () => {
  it('reads /api/items from intera_service_url', async () => {
    const rows = await items().getData({}, context({ intera_service_url: SERVICE_URL }));
    assert.deepEqual(rows, ITEMS);
    assert.equal(calls.length, 1);
    assert.equal(calls[0].url, `${SERVICE_URL}/api/items`);
    assert.equal(calls[0].init.method, 'GET');
  });

  it('ignores a trailing slash in the service address', async () => {
    await items().getData({}, context({ intera_service_url: `${SERVICE_URL}/` }));
    assert.equal(calls[0].url, `${SERVICE_URL}/api/items`);
  });

  it('applies offset and limit', async () => {
    const rows = await items().getData({ offset: 1, limit: 1 }, context({ intera_service_url: SERVICE_URL }));
    assert.deepEqual(rows.map((row) => row.id), ['item-002']);
  });

  it('forwards Core cancellation to fetch', async () => {
    const controller = new AbortController();
    await items().getData({}, { ...context({ intera_service_url: SERVICE_URL }), signal: controller.signal });
    const signal = calls[0].init.signal;
    assert.equal(signal.aborted, false);
    controller.abort();
    assert.equal(signal.aborted, true);
  });

  it('fails without intera_service_url and makes no request', async () => {
    await assert.rejects(items().getData({}, context({})), /intera_service_url/);
    await assert.rejects(items().getData({}, context(undefined)), /intera_service_url/);
    assert.equal(calls.length, 0);
  });

  it('rejects a non-http service address', async () => {
    await assert.rejects(
      items().getData({}, context({ intera_service_url: 'file:///etc/passwd' })),
      /http or https/,
    );
    assert.equal(calls.length, 0);
  });

  it('reports a non-2xx answer', async () => {
    respond = () => new Response('boom', { status: 503 });
    await assert.rejects(
      items().getData({}, context({ intera_service_url: SERVICE_URL })),
      /503 on GET \/api\/items/,
    );
  });

  it('rejects records of the wrong shape', async () => {
    respond = () => new Response(JSON.stringify([{ id: 1 }]), { status: 200 });
    await assert.rejects(
      items().getData({}, context({ intera_service_url: SERVICE_URL })),
      /does not match/,
    );
  });
});

describe('testConnection', () => {
  it('checks /health', async () => {
    respond = () => new Response(JSON.stringify({ status: 'ok' }), { status: 200 });
    const result = await bundle.testConnection({
      config: { intera_service_url: SERVICE_URL },
      instanceId: null,
      instanceSlug: null,
    });
    assert.equal(calls[0].url, `${SERVICE_URL}/health`);
    assert.match(result.detail, /healthy/);
  });

  it('fails when the service is unhealthy', async () => {
    respond = () => new Response('', { status: 500 });
    await assert.rejects(
      bundle.testConnection({ config: { intera_service_url: SERVICE_URL }, instanceId: null, instanceSlug: null }),
      /500 on GET \/health/,
    );
  });
});
