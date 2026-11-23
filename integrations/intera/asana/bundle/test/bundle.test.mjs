import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');

let calls;
let respond;
let seq = 0;
const realFetch = globalThis.fetch;

const nextToken = () => `secret-asana-token-${++seq}`;
const context = (config, extra = {}) => ({ userId: 'u1', roles: ['admin'], config, ...extra });
const source = (id) => bundle.dataSources.find((s) => s.id === id);

beforeEach(() => {
  calls = [];
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
});

describe('asana bundle contract', () => {
  it('exports manifest, 16 dataSources, getDataSources, and testConnection', () => {
    assert.equal(bundle.manifest.id, 'intera-asana-integration');
    assert.equal(bundle.manifest.version, '3.2.0');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.equal(bundle.dataSources.length, 16);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
    for (const ds of bundle.dataSources) {
      assert.ok(ds.fields.length > 0, `${ds.id} should declare fields`);
    }
  });

  it('rejects missing config before making any network request', async () => {
    await assert.rejects(source('asana_projects').getData({}, context({})), /accessToken/);
    await assert.rejects(source('asana_projects').getData({}, context(undefined)), /config is required/);
    await assert.rejects(bundle.testConnection({ config: {}, instanceId: null, instanceSlug: null }), /accessToken/);
    assert.equal(calls.length, 0);
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
