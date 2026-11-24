import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const distBundle = require('../dist/index.js');
const {
  bundle,
  cached,
  clearResponseCache,
  createResponseCache,
  getResponseCacheStats,
  MAX_RESPONSE_CACHE_BYTES,
  MAX_RESPONSE_CACHE_ENTRIES,
} = require('./dist/harness.cjs');

let calls;
let respond;
let seq = 0;
let fakeNow = null;
const realFetch = globalThis.fetch;
const realDateNow = Date.now;

const nextToken = () => `secret-asana-token-${++seq}`;
const context = (config, extra = {}) => ({ userId: 'u1', roles: ['admin'], config, ...extra });
const source = (id) => bundle.dataSources.find((s) => s.id === id);

beforeEach(() => {
  calls = [];
  fakeNow = null;
  Date.now = () => (fakeNow === null ? realDateNow() : fakeNow);
  respond = (url) => {
    const u = new URL(url);
    if (u.pathname.endsWith('/workspaces')) {
      return new Response(
        JSON.stringify({
          data: [{ gid: 'ws-1', name: 'Acme Workspace', is_organization: true, email_domains: ['example.com'] }],
        }),
        { status: 200 },
      );
    }
    if (u.pathname.endsWith('/projects')) {
      return new Response(
        JSON.stringify({
          data: [
            {
              gid: 'proj-1',
              name: 'Platform Launch',
              archived: false,
              completed: false,
              created_at: '2026-01-10T00:00:00.000Z',
              modified_at: '2026-02-01T00:00:00.000Z',
              due_on: '2026-01-20',
            },
            {
              gid: 'proj-2',
              name: 'Q4 Expansion',
              archived: false,
              completed: true,
              created_at: '2026-02-15T00:00:00.000Z',
              modified_at: '2026-02-10T00:00:00.000Z',
              due_on: '2026-03-01',
            },
          ],
        }),
        { status: 200 },
      );
    }
    return new Response(JSON.stringify({ data: [] }), { status: 200 });
  };
  globalThis.fetch = async (url, init) => {
    calls.push({ url: String(url), init });
    return respond(String(url), init);
  };
});

afterEach(() => {
  globalThis.fetch = realFetch;
  Date.now = realDateNow;
});

describe('asana bundle contract', () => {
  it('exports manifest, 16 dataSources, getDataSources, and testConnection', () => {
    assert.deepEqual(distBundle.manifest, bundle.manifest);
    assert.equal(distBundle.dataSources.length, 16);
    assert.equal(typeof distBundle.testConnection, 'function');
    assert.equal(bundle.manifest.id, 'intera-asana-integration');
    assert.equal(bundle.manifest.version, '3.2.1');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.equal(bundle.dataSources.length, 16);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
    for (const ds of bundle.dataSources) {
      assert.ok(ds.fields.length > 0, `${ds.id} should declare fields`);
    }
  });

  it('rejects missing config or non-https non-loopback apiUrl before making any network request', async () => {
    await assert.rejects(source('asana_projects').getData({}, context({})), /accessToken/);
    await assert.rejects(source('asana_projects').getData({}, context(undefined)), /config is required/);
    await assert.rejects(bundle.testConnection({ config: {}, instanceId: null, instanceSlug: null }), /accessToken/);
    await assert.rejects(
      source('asana_workspaces').getData({}, context({ accessToken: 'tok', apiUrl: 'http://app.asana.com/api/1.0' })),
      /must use https/,
    );
    await assert.rejects(
      source('asana_workspaces').getData({}, context({ accessToken: 'tok', apiUrl: 'https://user:pass@app.asana.com/api/1.0' })),
      /userinfo/,
    );
    await assert.rejects(
      source('asana_workspaces').getData({}, context({ accessToken: 'tok', apiUrl: 'https://app.asana.com/api/1.0#frag' })),
      /fragment/,
    );
    assert.equal(calls.length, 0);

    const loopbackRows = await source('asana_workspaces').getData(
      {},
      context({ accessToken: nextToken(), apiUrl: 'http://127.0.0.1:18080/api/1.0' }),
    );
    assert.equal(loopbackRows.length, 1);
    assert.ok(calls[0].url.startsWith('http://127.0.0.1:18080/api/1.0/workspaces'));
  });

  it('evicts oldest and expired entries by count and byte budget in response-cache', async () => {
    fakeNow = 20_000;
    const smallCache = createResponseCache({ maxEntries: 4, maxBytes: 4_096 });
    let loads = 0;
    for (let i = 0; i < 6; i += 1) {
      await smallCache.cached(`item-${i}`, 60_000, async () => ({ index: i, seq: ++loads }));
    }
    assert.equal(smallCache.stats().entries, 4);

    // Oldest entries (item-0, item-1) were evicted; item-5 remains cached.
    const before = loads;
    await smallCache.cached('item-5', 60_000, async () => ({ seq: ++loads }));
    assert.equal(loads, before);
    await smallCache.cached('item-0', 60_000, async () => ({ seq: ++loads }));
    assert.equal(loads, before + 1);

    // Expired entries are swept eagerly on the next write.
    fakeNow += 70_000;
    await smallCache.cached('fresh', 60_000, async () => ({ ok: true }));
    assert.equal(smallCache.stats().entries, 1);

    // Default module cache enforces MAX_RESPONSE_CACHE_ENTRIES (1_000) and MAX_RESPONSE_CACHE_BYTES (16 MiB).
    clearResponseCache();
    for (let i = 0; i < MAX_RESPONSE_CACHE_ENTRIES + 12; i += 1) {
      await cached(`entry-${i}`, 60_000, async () => ({ i }));
    }
    assert.equal(getResponseCacheStats().entries, MAX_RESPONSE_CACHE_ENTRIES);

    clearResponseCache();
    const oneMiB = 'x'.repeat(1024 * 1024);
    for (let i = 0; i < 20; i += 1) {
      await cached(`large-${i}`, 60_000, async () => ({ payload: oneMiB }));
      assert.ok(getResponseCacheStats().bytes <= MAX_RESPONSE_CACHE_BYTES);
    }
    assert.ok(getResponseCacheStats().entries < 16);
    clearResponseCache();
  });

  it('fetches projects and evaluates Query Contract 1.5.0 filters and dateDiff expressions', async () => {
    const token = nextToken();
    const rows = await source('asana_projects').getData(
      {
        filter: {
          type: 'condition',
          field: 'modified_at',
          operator: '>',
          value: { type: 'field', name: 'created_at' },
        },
        expressions: [
          { alias: 'project_gid', expression: { type: 'field', field: 'project_gid' } },
          {
            alias: 'days_span',
            expression: {
              type: 'dateDiff',
              unit: 'day',
              start: { type: 'field', field: 'created_at' },
              end: { type: 'field', field: 'due_on' },
            },
          },
        ],
        groupBy: ['project_gid'],
      },
      context({ accessToken: token }),
    );
    assert.deepEqual(rows, [{ project_gid: 'proj-1', days_span: 10 }]);
  });

  it('evaluates snapEdge + offset relativeDate conditions', async () => {
    const token = nextToken();
    const rows = await source('asana_projects').getData(
      {
        filter: {
          type: 'condition',
          field: 'created_at',
          operator: '>=',
          value: {
            type: 'relativeDate',
            from: 'today',
            unit: 'year',
            amount: -10,
            snapTo: 'year',
            snapEdge: 'start',
            offset: { unit: 'day', amount: 1 },
          },
        },
      },
      context({ accessToken: token }),
    );
    assert.equal(rows.length, 2);
  });

  it('supports testConnection happy path', async () => {
    const token = nextToken();
    const res = await bundle.testConnection({
      config: { accessToken: token },
      instanceId: null,
      instanceSlug: null,
    });
    assert.match(res.detail, /Acme Workspace — 2 projects/);
  });

  it('surfaces upstream 401 without leaking the secret token', async () => {
    const token = nextToken();
    respond = () =>
      new Response(JSON.stringify({ errors: [{ message: 'Not Authorized' }] }), { status: 401 });
    await assert.rejects(
      async () => {
        await source('asana_workspaces').getData({}, context({ accessToken: token }));
      },
      (err) => {
        assert.match(err.message, /401/);
        assert.equal(err.message.includes(token), false, 'error must never contain secret token');
        return true;
      },
    );
  });

  it('stops waiting when context.signal aborts', async () => {
    const token = nextToken();
    const controller = new AbortController();
    respond = () => new Promise(() => {});
    const pending = source('asana_workspaces').getData({}, context({ accessToken: token }, { signal: controller.signal }));
    controller.abort(new Error('deadline exceeded'));
    await assert.rejects(pending, /deadline exceeded/);
  });
});
