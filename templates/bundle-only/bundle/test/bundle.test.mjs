// Unit tests for the built bundle (`npm test` builds first). Global fetch is
// replaced with a mock, so no external API is called.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');
const CONFIG = { base_url: 'https://api.example.test/v1/', api_key: 'test-key' };
const RECORDS = [{ id: 'r-1', name: 'First', amount: 1, updatedAt: '2026-01-01T00:00:00Z' }];

let calls;
const realFetch = globalThis.fetch;
beforeEach(() => {
  calls = [];
  globalThis.fetch = async (url, init) => {
    calls.push({ url: String(url), init });
    return new Response(JSON.stringify(RECORDS));
  };
});
afterEach(() => {
  globalThis.fetch = realFetch;
});

it('reads records with the API key', async () => {
  const [source] = bundle.dataSources;
  const rows = await source.getData({}, { userId: 'u', roles: [], config: CONFIG });
  assert.deepEqual(rows, RECORDS);
  assert.equal(calls[0].url, 'https://api.example.test/v1/records');
  assert.equal(calls[0].init.headers.authorization, 'Bearer test-key');
});

it('fails without configuration and makes no request', async () => {
  const [source] = bundle.dataSources;
  await assert.rejects(source.getData({}, { userId: 'u', roles: [], config: {} }), /base_url/);
  assert.equal(calls.length, 0);
});

it('does not leak the API key in errors', async () => {
  globalThis.fetch = async () => new Response('denied', { status: 401 });
  await assert.rejects(bundle.testConnection({ config: CONFIG, instanceId: null, instanceSlug: null }), (error) => {
    assert.doesNotMatch(error.message, /test-key/);
    return /401/.test(error.message);
  });
});
