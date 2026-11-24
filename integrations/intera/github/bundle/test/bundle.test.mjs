import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const distBundle = require('../dist/index.js');
const {
  bundle,
  createResponseCache,
  GitHubClient,
  clearConditionalCache,
  getConditionalCacheStats,
  CONDITIONAL_CACHE_TTL_MS,
  MAX_CONDITIONAL_BYTES,
  MAX_CONDITIONAL_ENTRIES,
  parseGitHubConfig,
} = require('./dist/harness.cjs');

let calls;
let respond;
let seq = 0;
let fakeNow = null;
const realFetch = globalThis.fetch;
const realDateNow = Date.now;

const nextToken = () => `ghp_secret_test_token_${++seq}`;
const context = (config, extra = {}) => ({ userId: 'u1', roles: ['admin'], config, ...extra });
const source = (id) => bundle.dataSources.find((s) => s.id === id);

beforeEach(() => {
  calls = [];
  fakeNow = null;
  Date.now = () => (fakeNow === null ? realDateNow() : fakeNow);
  respond = (url) => {
    const u = new URL(url);
    if (u.pathname.endsWith('/users/acme-org')) {
      return new Response(JSON.stringify({ login: 'acme-org', type: 'Organization' }), {
        status: 200,
        headers: { 'x-ratelimit-limit': '5000', 'x-ratelimit-remaining': '4998' },
      });
    }
    if (u.pathname.endsWith('/orgs/acme-org/repos')) {
      return new Response(
        JSON.stringify([
          {
            id: 101,
            name: 'core-api',
            full_name: 'acme-org/core-api',
            private: true,
            archived: false,
            default_branch: 'main',
            language: 'TypeScript',
            open_issues_count: 4,
            stargazers_count: 12,
            forks_count: 2,
            size: 1024,
            created_at: '2026-01-01T00:00:00Z',
            updated_at: '2026-02-01T00:00:00Z',
            pushed_at: '2026-02-11T00:00:00Z',
            html_url: 'https://github.com/acme-org/core-api',
          },
          {
            id: 102,
            name: 'legacy-docs',
            full_name: 'acme-org/legacy-docs',
            private: false,
            archived: true,
            default_branch: 'main',
            language: 'Markdown',
            open_issues_count: 0,
            stargazers_count: 1,
            forks_count: 0,
            size: 128,
            created_at: '2025-05-01T00:00:00Z',
            updated_at: '2025-05-01T00:00:00Z',
            pushed_at: '2025-04-20T00:00:00Z',
            html_url: 'https://github.com/acme-org/legacy-docs',
          },
        ]),
        { status: 200 },
      );
    }
    return new Response(JSON.stringify([]), { status: 200 });
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

describe('github bundle contract', () => {
  it('exports manifest, 11 dataSources, getDataSources, and testConnection', () => {
    assert.deepEqual(distBundle.manifest, bundle.manifest);
    assert.equal(distBundle.dataSources.length, 11);
    assert.equal(typeof distBundle.testConnection, 'function');
    assert.equal(bundle.manifest.id, 'intera-github-integration');
    assert.equal(bundle.manifest.version, '3.2.1');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.equal(bundle.dataSources.length, 11);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
  });

  it('enforces https on apiUrl (except loopback) and rejects userinfo and fragments before calling fetch', async () => {
    await assert.rejects(source('github_repositories').getData({}, context({})), /accessToken/);
    await assert.rejects(
      source('github_repositories').getData({}, context({ accessToken: 'tok', owner: 'acme-org', apiUrl: 'ftp://example.com' })),
      /http\(s\) URL/,
    );
    await assert.rejects(
      source('github_repositories').getData({}, context({ accessToken: 'tok', owner: 'acme-org', apiUrl: 'http://ghes.example.com/api/v3' })),
      /must use https/,
    );
    await assert.rejects(
      async () => {
        await source('github_repositories').getData(
          {},
          context({ accessToken: 'tok', owner: 'acme-org', apiUrl: 'https://git:topsecret@ghes.example.com/api/v3' }),
        );
      },
      (err) => {
        assert.match(err.message, /userinfo/);
        assert.equal(err.message.includes('topsecret'), false);
        return true;
      },
    );
    await assert.rejects(
      source('github_repositories').getData({}, context({ accessToken: 'tok', owner: 'acme-org', apiUrl: 'https://ghes.example.com/api/v3#frag' })),
      /fragment/,
    );
    assert.equal(calls.length, 0);

    const loopbackRows = await source('github_repositories').getData(
      {},
      context({ accessToken: nextToken(), owner: 'acme-org', apiUrl: 'http://127.0.0.1:18080/api/v3' }),
    );
    assert.equal(loopbackRows.length, 2);
    assert.ok(calls[0].url.startsWith('http://127.0.0.1:18080/api/v3/'));

    const localhostRows = await source('github_repositories').getData(
      {},
      context({ accessToken: nextToken(), owner: 'acme-org', apiUrl: 'http://localhost:18080/api/v3' }),
    );
    assert.equal(localhostRows.length, 2);

    const ipv6Rows = await source('github_repositories').getData(
      {},
      context({ accessToken: nextToken(), owner: 'acme-org', apiUrl: 'http://[::1]:18080/api/v3' }),
    );
    assert.equal(ipv6Rows.length, 2);
  });

  it('evicts oldest and expired entries by count and byte budget in response-cache', async () => {
    fakeNow = 10_000;
    const countCache = createResponseCache({ maxEntries: 4, maxBytes: 1_000_000 });
    let loads = 0;
    for (let i = 0; i < 6; i += 1) {
      await countCache.cached(`k-${i}`, 60_000, async () => ({ index: i, count: ++loads }));
    }
    assert.equal(countCache.stats().entries, 4);

    // k-0 and k-1 were evicted; k-5 is still cached.
    const hitsBefore = loads;
    await countCache.cached('k-5', 60_000, async () => ({ count: ++loads }));
    assert.equal(loads, hitsBefore);
    await countCache.cached('k-0', 60_000, async () => ({ count: ++loads }));
    assert.equal(loads, hitsBefore + 1);

    // Eager expired sweep on write removes expired entries immediately.
    fakeNow += 70_000;
    await countCache.cached('fresh', 60_000, async () => ({ ok: true }));
    assert.equal(countCache.stats().entries, 1);

    // Byte budget eviction drops oldest entries so total bytes stays <= maxBytes.
    const byteCache = createResponseCache({ maxEntries: 100, maxBytes: 4_096 });
    let byteLoads = 0;
    for (let i = 0; i < 8; i += 1) {
      await byteCache.cached(`blob-${i}`, 60_000, async () => ({
        payload: 'x'.repeat(1_024),
        seq: ++byteLoads,
      }));
      assert.ok(byteCache.stats().bytes <= 4_096);
    }
    assert.ok(byteCache.stats().entries <= 3);
    const beforeReload = byteLoads;
    await byteCache.cached('blob-7', 60_000, async () => ({ payload: 'x'.repeat(1_024), seq: ++byteLoads }));
    assert.equal(byteLoads, beforeReload);
    await byteCache.cached('blob-0', 60_000, async () => ({ payload: 'x'.repeat(1_024), seq: ++byteLoads }));
    assert.equal(byteLoads, beforeReload + 1);
  });

  it('bounds conditional ETag cache by entry count, byte budget, and TTL', async () => {
    clearConditionalCache();
    fakeNow = 100_000;
    const cfg = parseGitHubConfig({ owner: 'acme-org', accessToken: nextToken() });
    const client = new GitHubClient(cfg);

    respond = (url, init) => {
      const u = new URL(url);
      if (init?.headers?.['If-None-Match']) {
        return new Response(null, { status: 304 });
      }
      return new Response(JSON.stringify({ path: u.pathname }), {
        status: 200,
        headers: { etag: `"etag-${u.pathname}"` },
      });
    };

    for (let i = 0; i < MAX_CONDITIONAL_ENTRIES + 10; i += 1) {
      await client.get(`/repos/acme-org/r-${i}`, {}, 0);
    }
    assert.equal(getConditionalCacheStats().entries, MAX_CONDITIONAL_ENTRIES);

    // Entry r-0 was evicted (no If-None-Match), while the newest entry sends If-None-Match.
    calls = [];
    await client.get('/repos/acme-org/r-0', {}, 0);
    assert.equal(calls[0].init.headers['If-None-Match'], undefined);

    calls = [];
    const latestIdx = MAX_CONDITIONAL_ENTRIES + 9;
    await client.get(`/repos/acme-org/r-${latestIdx}`, {}, 0);
    assert.equal(calls[0].init.headers['If-None-Match'], `"etag-/repos/acme-org/r-${latestIdx}"`);

    // TTL expiry drops conditional validators once CONDITIONAL_CACHE_TTL_MS elapses.
    fakeNow += CONDITIONAL_CACHE_TTL_MS + 1_000;
    calls = [];
    await client.get(`/repos/acme-org/r-${latestIdx}`, {}, 0);
    assert.equal(calls[0].init.headers['If-None-Match'], undefined);

    // Byte budget cap keeps conditional cache <= MAX_CONDITIONAL_BYTES.
    clearConditionalCache();
    const oneMiB = 'a'.repeat(1024 * 1024);
    respond = (url) =>
      new Response(JSON.stringify({ data: oneMiB }), {
        status: 200,
        headers: { etag: `"etag-${url}"` },
      });
    for (let i = 0; i < 20; i += 1) {
      await client.get(`/repos/acme-org/large-${i}`, {}, 0);
      assert.ok(getConditionalCacheStats().bytes <= MAX_CONDITIONAL_BYTES);
    }
    assert.ok(getConditionalCacheStats().entries < 16);
    clearConditionalCache();
  });

  it('fetches repositories and evaluates Query Contract 1.5.0 field-ref and dateDiff', async () => {
    const token = nextToken();
    const rows = await source('github_repositories').getData(
      {
        filter: {
          type: 'condition',
          field: 'pushed_at',
          operator: '>',
          value: { type: 'field', name: 'updated_at' },
        },
        expressions: [
          { alias: 'name', expression: { type: 'field', field: 'name' } },
          {
            alias: 'days_since_update',
            expression: {
              type: 'dateDiff',
              unit: 'day',
              start: { type: 'field', field: 'updated_at' },
              end: { type: 'field', field: 'pushed_at' },
            },
          },
        ],
        groupBy: ['name'],
      },
      context({ owner: 'acme-org', accessToken: token }),
    );
    assert.deepEqual(rows, [{ name: 'core-api', days_since_update: 10 }]);
  });

  it('supports testConnection happy path', async () => {
    const token = nextToken();
    const res = await bundle.testConnection({
      config: { owner: 'acme-org', accessToken: token },
      instanceId: null,
      instanceSlug: null,
    });
    assert.match(res.detail, /acme-org — 2 repositories \(1 archived\)/);
  });

  it('surfaces upstream 401 without leaking the secret token', async () => {
    const token = nextToken();
    respond = () => new Response(JSON.stringify({ message: 'Bad credentials' }), { status: 401 });
    await assert.rejects(
      async () => {
        await source('github_repositories').getData({}, context({ owner: 'acme-org', accessToken: token }));
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
    const pending = source('github_repositories').getData(
      {},
      context({ owner: 'acme-org', accessToken: token }, { signal: controller.signal }),
    );
    controller.abort(new Error('cancelled by host'));
    await assert.rejects(pending, /cancelled by host/);
  });
});
