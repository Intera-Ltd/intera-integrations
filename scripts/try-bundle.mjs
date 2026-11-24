#!/usr/bin/env node
// Calls a built bundle the way Core does: testConnection, then getData of each
// data source, with the connection config you pass. Handy against a service
// container running on your machine.
//
// Usage:
//   node scripts/try-bundle.mjs <integration-dir> [--service-url <url>] [--config key=value]... [--limit N]
//
// Example:
//   docker run --rm -d -p 8080:8080 --name hello intera-hello-dotnet:1.0.0
//   node scripts/try-bundle.mjs integrations/intera/hello-dotnet --service-url http://localhost:8080
import path from 'node:path';
import { createRequire } from 'node:module';
import { parseArgs } from 'node:util';

const require = createRequire(import.meta.url);
const { values, positionals } = parseArgs({
  allowPositionals: true,
  options: {
    'service-url': { type: 'string' },
    'service-token': { type: 'string' },
    config: { type: 'string', multiple: true, default: [] },
    limit: { type: 'string', default: '5' },
  },
});
if (positionals.length !== 1) {
  console.error('usage: try-bundle.mjs <integration-dir> [--service-url <url>] [--service-token <token>] [--config key=value]... [--limit N]');
  process.exit(2);
}

const entry = path.resolve(positionals[0], 'bundle', 'dist', 'index.js');
const loaded = require(entry);
const bundle = loaded.default && loaded.default.manifest ? loaded.default : loaded;
const config = Object.fromEntries(
  values.config.map((pair) => {
    const at = pair.indexOf('=');
    return at < 0 ? [pair, ''] : [pair.slice(0, at), pair.slice(at + 1)];
  }),
);
if (values['service-url']) config.intera_service_url = values['service-url'];
if (values['service-token']) config.intera_service_token = values['service-token'];

console.log(`${bundle.manifest.id} ${bundle.manifest.version}`);
let failed = false;
if (typeof bundle.testConnection === 'function') {
  try {
    const result = await bundle.testConnection({ config, instanceId: null, instanceSlug: null });
    console.log('testConnection: ok', result?.detail ?? '');
  } catch (error) {
    failed = true;
    console.log('testConnection: FAILED', error.message);
  }
}
for (const source of bundle.dataSources ?? []) {
  try {
    const rows = await source.getData(
      { limit: Number(values.limit) },
      { userId: 'local-dev', roles: ['admin'], config },
    );
    console.log(`\n${source.id} (${source.name}): ${rows.length} row(s)`);
    console.table(rows);
  } catch (error) {
    failed = true;
    console.log(`\n${source.id}: FAILED`, error.message);
  }
}
process.exitCode = failed ? 1 : 0;
