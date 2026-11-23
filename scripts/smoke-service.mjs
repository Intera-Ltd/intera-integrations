#!/usr/bin/env node
// Starts a built service image, waits for deployment.health_path as the
// installation agent does, then calls the built bundle against it the way
// Core does (testConnection and every data source, with intera_service_url).
//
// deployment.settings get their default; a setting without one gets an
// obviously fake value (secrets and strings "smoke-test", numbers 0, booleans
// false), so a service that requires a setting still starts.
//
// Usage: node scripts/smoke-service.mjs <integration-dir> <local-image>
import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import yaml from 'js-yaml';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);
const [dir, image] = process.argv.slice(2);
if (!dir || !image) {
  console.error('usage: smoke-service.mjs <integration-dir> <local-image>');
  process.exit(2);
}
const manifest = yaml.load(fs.readFileSync(path.join(dir, 'manifest.yaml'), 'utf8'), { schema: yaml.CORE_SCHEMA });
const deployment = manifest.deployment;
if (deployment?.type !== 'docker') {
  console.log('no docker deployment, nothing to smoke-test');
  process.exit(0);
}

const name = `smoke-${process.pid}`;
const FAKE = { string: 'smoke-test', secret: 'smoke-test', number: 0, boolean: false };
const variables = { ...(deployment.env ?? {}) };
for (const setting of deployment.settings ?? []) {
  const value = setting.default ?? FAKE[setting.kind];
  variables[setting.env ?? setting.name.toUpperCase()] = String(value);
}
const env = Object.entries(variables).flatMap(([key, value]) => ['-e', `${key}=${value}`]);
execFileSync('docker', ['run', '-d', '--name', name, '-p', `127.0.0.1::${deployment.port}`, ...env, image], { stdio: 'inherit' });
const cleanup = () => spawnSync('docker', ['rm', '-f', name], { stdio: 'ignore' });

try {
  const mapping = execFileSync('docker', ['port', name, `${deployment.port}/tcp`], { encoding: 'utf8' }).trim().split('\n')[0];
  const baseUrl = `http://127.0.0.1:${mapping.split(':').pop()}`;
  const healthPath = deployment.health_path ?? '/';
  const deadline = Date.now() + 60_000;
  let healthy = false;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(`${baseUrl}${healthPath}`, { signal: AbortSignal.timeout(2_000) });
      if (response.ok) {
        healthy = true;
        break;
      }
    } catch {
      // not up yet
    }
    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }
  if (!healthy) {
    spawnSync('docker', ['logs', '--tail', '50', name], { stdio: 'inherit' });
    throw new Error(`GET ${healthPath} did not answer 2xx within 60 s`);
  }
  console.log(`healthy: GET ${healthPath}`);
  const user = execFileSync('docker', ['image', 'inspect', '--format', '{{.Config.User}}', image], { encoding: 'utf8' }).trim();
  if (user === '' || /^(root|0)(:|$)/.test(user)) throw new Error('the image runs as root; set a non-root USER');
  console.log(`runs as user ${user}`);

  const bundleEntry = path.resolve(dir, 'bundle', 'dist', 'index.js');
  const loaded = require(bundleEntry);
  const bundle = loaded.default && loaded.default.manifest ? loaded.default : loaded;
  if (!bundle?.manifest || typeof bundle.manifest.id !== 'string') {
    throw new Error('bundle/dist/index.js did not export a valid manifest');
  }
  console.log(`loaded bundle: ${bundle.manifest.id}@${bundle.manifest.version}`);

  const needsExternalServiceSecrets = (deployment.settings ?? []).some(
    (s) => s.kind === 'secret' || (s.required === true && s.default === undefined),
  );
  const needsConnectionSecrets = (bundle.manifest.configSchema ?? []).some(
    (f) => f.required === true && f.defaultValue === undefined && f.default === undefined,
  );
  if (needsExternalServiceSecrets || needsConnectionSecrets) {
    console.log('skipping live getData smoke calls: integration requires upstream credentials');
  } else {
    const result = spawnSync(process.execPath, [path.join(ROOT, 'scripts', 'try-bundle.mjs'), dir, '--service-url', baseUrl], { stdio: 'inherit' });
    if (result.status !== 0) throw new Error('the bundle failed against the running service');
  }
} catch (error) {
  console.error(`::error title=${dir}::${error.message}`);
  process.exitCode = 1;
} finally {
  cleanup();
}
