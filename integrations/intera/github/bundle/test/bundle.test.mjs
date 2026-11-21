import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');

let calls;
let respond;
let seq = 0;
const realFetch = globalThis.fetch;

const nextToken = () => `ghp_secret_test_token_${++seq}`;
const context = (config, extra = {}) => ({ userId: 'u1', roles: ['admin'], config, ...extra });
const source = (id) => bundle.dataSources.find((s) => s.id === id);

beforeEach(() => {
  calls = [];
  respond = (url) => {
    const u = new URL(url);
    if (u.pathname === '/users/acme-org') {
      return new Response(JSON.stringify({ login: 'acme-org', type: 'Organization' }), {
        status: 200,
        headers: { 'x-ratelimit-limit': '5000', 'x-ratelimit-remaining': '4998' },
      });
    }
    if (u.pathname === '/orgs/acme-org/repos') {
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
});

describe('github bundle contract', () => {
  it('exports manifest, 11 dataSources, getDataSources, and testConnection', () => {
    assert.equal(bundle.manifest.id, 'intera-github-integration');
    assert.equal(bundle.manifest.version, '3.2.0');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.equal(bundle.dataSources.length, 11);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
  });

  it('validates configuration before making any network request', async () => {
    await assert.rejects(source('github_repositories').getData({}, context({})), /accessToken/);
    await assert.rejects(
      source('github_repositories').getData({}, context({ accessToken: 'tok', owner: 'acme-org', apiUrl: 'ftp://example.com' })),
      /http\(s\) URL/,
    );
    assert.equal(calls.length, 0);
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
