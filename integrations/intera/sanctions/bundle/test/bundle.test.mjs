import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { afterEach, beforeEach, describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const distBundle = require('../dist/index.js');
const {
  bundle,
  downloadText,
  decodeEntities,
  parseXml,
  LIST_MAX_STALE_MS,
  LIST_REFRESH_INTERVAL_MS,
  LIST_RETRY_INTERVAL_MS,
} = require('./dist/harness.cjs');

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
let fakeNow = null;
const realFetch = globalThis.fetch;
const realDateNow = Date.now;

const nextUrl = () => `https://sanctions.example.test/list-${++seq}.xml`;
const context = (config, extra = {}) => ({ userId: 'u1', roles: ['admin'], config, ...extra });
const euSource = () => bundle.dataSources.find((s) => s.id === 'eu_sanctions_list');

beforeEach(() => {
  calls = [];
  fakeNow = null;
  Date.now = () => (fakeNow === null ? realDateNow() : fakeNow);
  respond = () => new Response(SAMPLE_XML, { status: 200 });
  globalThis.fetch = async (url, init) => {
    calls.push({ url: String(url), init });
    return respond(String(url), init);
  };
});

afterEach(() => {
  globalThis.fetch = realFetch;
  Date.now = realDateNow;
});

describe('sanctions bundle contract', () => {
  it('exports manifest, eu_sanctions_list dataSource, getDataSources, and testConnection', () => {
    assert.deepEqual(distBundle.manifest, bundle.manifest);
    assert.equal(typeof distBundle.testConnection, 'function');
    assert.equal(bundle.manifest.id, 'intera-sanctions-integration');
    assert.equal(bundle.manifest.name, 'SanctionsCheck');
    assert.equal(bundle.manifest.version, '1.2.1');
    assert.equal(bundle.manifest.compatibility.integration_contract, 2);
    assert.deepEqual(bundle.getDataSources(), bundle.dataSources);
    assert.equal(typeof bundle.testConnection, 'function');
    assert.ok(euSource().fields.length > 10);
  });

  it('rejects non-https non-loopback, userinfo, fragment, or malformed eu_list_url while allowing loopback http', async () => {
    await assert.rejects(
      euSource().getData({}, context({ eu_list_url: 'file:///etc/passwd' })),
      /http\(s\) URL/,
    );
    await assert.rejects(
      euSource().getData({}, context({ eu_list_url: 'not-a-url' })),
      /not a valid URL/,
    );
    await assert.rejects(
      euSource().getData({}, context({ eu_list_url: 'http://example.com/list.xml' })),
      /must use https/,
    );
    await assert.rejects(
      euSource().getData({}, context({ eu_list_url: 'https://user:pass@example.com/list.xml' })),
      /userinfo/,
    );
    await assert.rejects(
      euSource().getData({}, context({ eu_list_url: 'https://example.com/list.xml#section' })),
      /fragment/,
    );
    assert.equal(calls.length, 0);

    const loopbackIpv4Rows = await euSource().getData(
      {},
      context({ eu_list_url: 'http://127.0.0.1:18080/eu-fsf.xml' }),
    );
    assert.equal(loopbackIpv4Rows.length, 1);

    const loopbackLocalhostRows = await euSource().getData(
      {},
      context({ eu_list_url: 'http://localhost:18080/eu-fsf.xml' }),
    );
    assert.equal(loopbackLocalhostRows.length, 1);

    const loopbackIpv6Rows = await euSource().getData(
      {},
      context({ eu_list_url: 'http://[::1]:18080/eu-fsf.xml' }),
    );
    assert.equal(loopbackIpv6Rows.length, 1);
    assert.equal(calls.length, 3);
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

  it('handles out-of-range numeric entities and UTF-16 surrogates in XML without throwing', async () => {
    assert.equal(decodeEntities('A&#x110000;B&#55296;C&#xDFFF;D&#x41;'), 'A\uFFFDB\uFFFDC\uFFFDD' + 'A');
    const parsed = parseXml('<root attr="&#x110000;-&#55296;">&#x110000;/&#55296;</root>');
    assert.equal(parsed.attributes.attr, '\uFFFD-\uFFFD');
    assert.equal(parsed.text, '\uFFFD/\uFFFD');

    respond = () =>
      new Response(
        `<?xml version="1.0" encoding="UTF-8"?>
<export generationDate="2026-08-05T00:00:00Z">
  <sanctionEntity logicalId="3001" designationDate="2026-01-10">
    <subjectType code="person" />
    <nameAlias logicalId="4001" wholeName="Entity &#x110000; &#55296; Name" strong="true" />
    <remark>Note &#x110000; and &#55296;</remark>
  </sanctionEntity>
</export>`,
        { status: 200 },
      );

    const rows = await euSource().getData({}, context({ eu_list_url: nextUrl() }));
    assert.equal(rows.length, 1);
    assert.equal(rows[0].name, 'Entity \uFFFD \uFFFD Name');
    assert.equal(rows[0].remark, 'Note \uFFFD and \uFFFD');
  });

  it('throws during retryAfter when cached list is older than LIST_MAX_STALE_MS without re-fetching', async () => {
    const url = nextUrl();
    fakeNow = 1_000_000;

    const initialRows = await euSource().getData({}, context({ eu_list_url: url }));
    assert.equal(initialRows.length, 1);
    assert.equal(calls.length, 1);

    // Within LIST_MAX_STALE_MS (2 days old), a failed refresh still serves the recent copy during backoff.
    fakeNow += 2 * LIST_REFRESH_INTERVAL_MS;
    const origWarn = console.warn;
    console.warn = () => {};
    try {
      respond = () => new Response('upstream outage', { status: 503 });
      const fallbackRows = await euSource().getData({}, context({ eu_list_url: url }));
      assert.equal(fallbackRows.length, 1);
      assert.equal(calls.length, 2);

      // Advance past LIST_MAX_STALE_MS (> 7 days since initial fetch) and past first retryAfter:
      // refresh fails again, and neither that call nor a subsequent call inside retryAfter may return stale rows.
      fakeNow = 1_000_000 + LIST_MAX_STALE_MS + 60_000;
      await assert.rejects(
        euSource().getData({}, context({ eu_list_url: url })),
        /HTTP 503/,
      );
      assert.equal(calls.length, 3);

      // During retryAfter backoff (e.g. 60 seconds later), must still throw and NOT issue another fetch.
      fakeNow += 60_000;
      await assert.rejects(
        euSource().getData({}, context({ eu_list_url: url })),
        /HTTP 503.*retry backoff active/,
      );
      assert.equal(calls.length, 3);
    } finally {
      console.warn = origWarn;
    }
  });

  it('backs off without re-downloading when initial download fails (list === null)', async () => {
    const url = nextUrl();
    fakeNow = 5_000_000;
    respond = () => new Response('bad gateway', { status: 502 });

    await assert.rejects(
      euSource().getData({}, context({ eu_list_url: url })),
      /HTTP 502/,
    );
    assert.equal(calls.length, 1);

    // Subsequent call within LIST_RETRY_INTERVAL_MS throws stored error immediately without calling fetch.
    fakeNow += 30_000;
    await assert.rejects(
      euSource().getData({}, context({ eu_list_url: url })),
      /HTTP 502.*retry backoff active/,
    );
    assert.equal(calls.length, 1);

    // Once retryAfter elapses, a fresh download is attempted and succeeds.
    fakeNow += LIST_RETRY_INTERVAL_MS;
    respond = () => new Response(SAMPLE_XML, { status: 200 });
    const rows = await euSource().getData({}, context({ eu_list_url: url }));
    assert.equal(rows.length, 1);
    assert.equal(calls.length, 2);
  });

  it('enforces max response byte cap while streaming XML downloads', async () => {
    const url = nextUrl();
    respond = () =>
      new Response('x'.repeat(2_048), {
        status: 200,
      });
    await assert.rejects(downloadText(url, 1_024), /exceeds the 1024-byte size limit/);
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
