// Unit tests for the built bundle (`npm test` builds first). Global fetch is
// replaced with a mock, so no service has to run.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');
const SERVICE_URL = 'http://service.test:8080';
const RECORDS = [{ id: 'r-1', name: 'First', amount: 1, updatedAt: '2026-01-01T00:00:00Z' }];

let calls;
const realFetch = globalThis.fetch;
beforeEach(() => {
  calls = [];
  globalThis.fetch = async (url) => {
    calls.push(String(url));
    return new Response(JSON.stringify(String(url).endsWith('/health') ? { status: 'ok' } : RECORDS));
  };
});
afterEach(() => {
  globalThis.fetch = realFetch;
});

it('reads records from intera_service_url', async () => {
  const [source] = bundle.dataSources;
  const rows = await source.getData({}, { userId: 'u', roles: [], config: { intera_service_url: SERVICE_URL } });
  assert.deepEqual(rows, RECORDS);
  assert.deepEqual(calls, [`${SERVICE_URL}/api/records`]);
});

it('fails without intera_service_url', async () => {
  const [source] = bundle.dataSources;
  await assert.rejects(source.getData({}, { userId: 'u', roles: [], config: {} }), /intera_service_url/);
});

it('tests the connection with /health', async () => {
  await bundle.testConnection({ config: { intera_service_url: SERVICE_URL }, instanceId: null, instanceSlug: null });
  assert.deepEqual(calls, [`${SERVICE_URL}/health`]);
});
