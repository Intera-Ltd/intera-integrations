// Unit tests of the manifest pre-check: npm test (node --test scripts/test/*.test.mjs)
import assert from 'node:assert/strict';
import test from 'node:test';
import { checkLicense, checkManifest } from '../validate-manifest.mjs';

const DIGEST = 'a'.repeat(64);

function manifest(deployment) {
  return {
    id: 'acme.billing',
    name: 'Acme Billing',
    version: '1.2.0',
    runtime_id: 'acme_billing',
    developer: { id: 'acme' },
    compatibility: { intera_min: '0.5.0', integration_contract: 2 },
    deployment: {
      type: 'docker',
      image: `ghcr.io/acme/billing@sha256:${DIGEST}`,
      port: 8080,
      health_path: '/health',
      ...deployment,
    },
  };
}

function check(deployment, options = { release: false }) {
  const problems = [];
  const warnings = [];
  checkManifest(manifest(deployment), problems, warnings, options);
  return { problems, warnings };
}

const token = { name: 'api_token', label: 'API token', kind: 'secret', required: true, env: 'BILLING__APITOKEN' };

test('accepts a docker deployment without settings', () => {
  assert.deepEqual(check({}).problems, []);
});

test('accepts settings of every kind', () => {
  const { problems, warnings } = check({
    env: { ASPNETCORE_HTTP_PORTS: '8080' },
    settings: [
      { ...token, description: 'Token of the Billing service account' },
      { name: 'region', label: 'Region', kind: 'string', default: 'eu' },
      { name: 'timeout_sec', label: 'Timeout', kind: 'number', default: 30 },
      { name: 'verbose', label: 'Verbose log', kind: 'boolean', default: false },
    ],
  });
  assert.deepEqual(problems, []);
  assert.deepEqual(warnings, []);
});

test('rejects settings that are not a list', () => {
  assert.deepEqual(check({ settings: { api_token: token } }).problems, ['deployment.settings: must be a list']);
});

test('rejects unknown fields, bad names, labels and kinds', () => {
  const { problems } = check({
    settings: [
      { name: 'Region', label: 'Region', kind: 'string' },
      { name: 'region', label: '', kind: 'string' },
      { name: 'region', label: 'Region', kind: 'text' },
      { name: 'zone', label: 'Zone', kind: 'string', secret: true },
      { name: 'area', label: 'Area', kind: 'string', required: 'yes' },
    ],
  });
  assert.equal(problems.length, 5);
  assert.match(problems[0], /^deployment\.settings\[0\]\.name: must be a lowercase key/);
  assert.match(problems[1], /^deployment\.settings\[1\]\.label: must be a non-empty string/);
  assert.match(problems[2], /^deployment\.settings\[2\]\.kind: must be one of string, secret, number, boolean/);
  assert.equal(problems[3], 'deployment.settings[3].secret: unknown field');
  assert.match(problems[4], /^deployment\.settings\[4\]\.required: must be true or false/);
});

test('a secret has no default and defaults match the kind', () => {
  const { problems } = check({
    settings: [
      { ...token, default: 'fake' },
      { name: 'timeout_sec', label: 'Timeout', kind: 'number', default: '30' },
      { name: 'verbose', label: 'Verbose', kind: 'boolean', default: 'false' },
      { name: 'region', label: 'Region', kind: 'string', default: 1 },
    ],
  });
  assert.deepEqual(problems.map((p) => p.split(':')[0]), [
    'deployment.settings[0].default',
    'deployment.settings[1].default',
    'deployment.settings[2].default',
    'deployment.settings[3].default',
  ]);
  assert.match(problems[0], /a secret has no default/);
});

test('env defaults to the name in upper case and must not repeat', () => {
  const { problems } = check({
    env: { REGION: 'eu' },
    settings: [
      { name: 'region', label: 'Region', kind: 'string' },
      { name: 'zone', label: 'Zone', kind: 'string', env: 'ZONE_ID' },
      { name: 'zone_2', label: 'Zone', kind: 'string', env: 'ZONE_ID' },
      { name: 'zone', label: 'Zone again', kind: 'string', env: 'OTHER' },
    ],
  });
  assert.deepEqual(problems, [
    'deployment.settings[0].env: REGION is already set by deployment.env or another setting',
    'deployment.settings[2].env: ZONE_ID is already set by deployment.env or another setting',
    'deployment.settings[3].name: duplicates "zone"',
  ]);
});

test('PATH, HOME and DOCKER_* are reserved', () => {
  const { problems } = check({
    settings: [
      { name: 'path', label: 'Path', kind: 'string' },
      { name: 'home_dir', label: 'Home', kind: 'string', env: 'HOME' },
      { name: 'docker_host', label: 'Docker', kind: 'string' },
      { name: 'cfg', label: 'Config', kind: 'string', env: 'lower' },
    ],
  });
  assert.equal(problems.length, 4);
  assert.match(problems[0], /PATH is reserved/);
  assert.match(problems[1], /HOME is reserved/);
  assert.match(problems[2], /DOCKER_HOST is reserved/);
  assert.match(problems[3], /^deployment\.settings\[3\]\.env: must be an upper snake case name/);
});

test('env and settings together hold at most 50 variables', () => {
  const env = Object.fromEntries(Array.from({ length: 45 }, (_, i) => [`FIXED_${i}`, 'x']));
  const settings = Array.from({ length: 6 }, (_, i) => ({ name: `s_${i}`, label: `S ${i}`, kind: 'string' }));
  assert.deepEqual(check({ env, settings }).problems, [
    'deployment.settings: env and settings together must have at most 50 variables',
  ]);
  assert.deepEqual(check({ env, settings: settings.slice(0, 5) }).problems, []);
});

test('at most 30 settings', () => {
  const settings = Array.from({ length: 31 }, (_, i) => ({ name: `s_${i}`, label: `S ${i}`, kind: 'string' }));
  assert.deepEqual(check({ settings }).problems, ['deployment.settings: must have at most 30 settings']);
});

test('warns about a secret-looking setting that is not a secret', () => {
  const { problems, warnings } = check({ settings: [{ ...token, kind: 'string' }] });
  assert.deepEqual(problems, []);
  assert.equal(warnings.length, 1);
  assert.match(warnings[0], /use kind: secret/);
});

test('secrets in deployment.env are still rejected', () => {
  const { problems } = check({ env: { API_TOKEN: 'x' } });
  assert.equal(problems.length, 1);
  assert.match(problems[0], /deployment\.settings with kind: secret/);
});

test('settings are not allowed for type none', () => {
  const problems = [];
  checkManifest({ ...manifest({}), deployment: { type: 'none', settings: [] } }, problems, [], { release: false });
  assert.deepEqual(problems, ['deployment.settings: unknown field for type none']);
});

function license(value, hasLicenseFile) {
  const problems = [];
  const warnings = [];
  checkLicense(value, hasLicenseFile, problems, warnings);
  return { problems, warnings };
}

test('the repository license needs no LICENSE file in the folder', () => {
  assert.deepEqual(license('Apache-2.0', false), { problems: [], warnings: [] });
});

test('another license needs its LICENSE file in the folder', () => {
  assert.match(license('MIT', false).problems[0], /add its text as LICENSE/);
  assert.deepEqual(license('MIT', true), { problems: [], warnings: [] });
});

test('a LICENSE file in the folder needs the license field', () => {
  assert.match(license(undefined, true).problems[0], /must name it in license/);
});

test('no license field and no LICENSE file is a warning', () => {
  const { problems, warnings } = license(undefined, false);
  assert.deepEqual(problems, []);
  assert.match(warnings[0], /Apache-2.0 applies/);
});
