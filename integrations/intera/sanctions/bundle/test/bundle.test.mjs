import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const bundle = require('../dist/index.js');

const SAMPLE_XML = `<?xml version="1.0" encoding="UTF-8"?>
<export generationDate="2026-08-05T00:00:00Z">
  <sanctionEntity logicalId="1001" euReferenceNumber="EU.1001.10" designationDate="2026-01-10">
    <subjectType code="enterprise" />
    <regulation programme="TEST" numberTitle="2026/100" regulationType="regulation" publicationDate="2026-01-12" entryIntoForceDate="2026-01-15">
      <publicationUrl>https://eur-lex.europa.eu/example</publicationUrl>
    </regulation>
    <nameAlias logicalId="2001" wholeName="Example Shipping Ltd" nameLanguage="EN" strong="true" />
    <identification identificationTypeCode="imo" number="9123456" />
  </sanctionEntity>
</export>`;

let calls;
let respond;
let seq = 0;
const realFetch = globalThis.fetch;

const nextUrl = () => `https://sanctions.example.test/list-${++seq}.xml`;
const context = (config, extra = {}) => ({ userId: 'u1', roles: ['admin'], config, ...extra });
const euSource = () => bundle.dataSources.find((s) => s.id === 'eu_sanctions_list');

beforeEach(() => {
  calls = [];
  respond = () => new Response(SAMPLE_XML, { status: 200 });
  globalThis.fetch = async (url, init) => {
    calls.push({ url: String(url), init });
    return respond(String(url), init);
  };
});

afterEach(() => {
  globalThis.fetch = realFetch;
});

describe('sanctions bundle contract', () => {
  it('exports manifest, eu_sanctions_list dataSource, getDataSources, and testConnection', () => {
    assert.equal(bundle.manifest.id, 'intera-sanctions-integration');
    assert.equal(bundle.manifest.name, 'SanctionsCheck');
    assert.equal(bundle.manifest.version, '1.2.0');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
    assert.ok(euSource().fields.length > 10);
  });

  it('rejects non-http(s) or malformed eu_list_url before calling fetch', async () => {
    await assert.rejects(
      euSource().getData({}, context({ eu_list_url: 'file:///etc/passwd' })),
      /http\(s\) URL/,
    );
    await assert.rejects(
      euSource().getData({}, context({ eu_list_url: 'not-a-url' })),
      /not a valid URL/,
    );
    assert.equal(calls.length, 0);
  });

  it('downloads and parses EU sanctions XML and evaluates Query Contract 1.5.0 dateDiff and FieldRefValue', async () => {
    const url = nextUrl();
    const rows = await euSource().getData(
      {
        filter: {
          type: 'condition',
          field: 'regulation_in_force_on',
          operator: '>',
          value: { type: 'field', name: 'listed_on' },
        },
        expressions: [
          { alias: 'primary_name', expression: { type: 'field', field: 'primary_name' } },
          {
            alias: 'days_to_force',
            expression: {
              type: 'dateDiff',
              unit: 'day',
              start: { type: 'field', field: 'listed_on' },
              end: { type: 'field', field: 'regulation_in_force_on' },
            },
          },
        ],
        groupBy: ['primary_name'],
      },
      context({ eu_list_url: url }),
    );
    assert.deepEqual(rows, [{ primary_name: 'Example Shipping Ltd', days_to_force: 5 }]);
    assert.equal(calls.length, 1);
  });

  it('supports testConnection happy path', async () => {
    const url = nextUrl();
    const res = await bundle.testConnection({
      config: { eu_list_url: url },
      instanceId: null,
      instanceSlug: null,
    });
    assert.match(res.detail, /1 subjects, 1 names/);
  });

  it('rejects malformed XML and non-2xx HTTP responses cleanly', async () => {
    respond = () => new Response('<html>Error</html>', { status: 200 });
    await assert.rejects(euSource().getData({}, context({ eu_list_url: nextUrl() })), /not well-formed XML|expected <export>/);

    respond = () => new Response('unavailable', { status: 503 });
    await assert.rejects(euSource().getData({}, context({ eu_list_url: nextUrl() })), /HTTP 503/);
  });

  it('stops waiting when context.signal aborts', async () => {
    const controller = new AbortController();
    respond = () => new Promise(() => {});
    const pending = euSource().getData(
      {},
      context({ eu_list_url: nextUrl() }, { signal: controller.signal }),
    );
    controller.abort(new Error('request timed out'));
    await assert.rejects(pending, /request timed out/);
  });
});
