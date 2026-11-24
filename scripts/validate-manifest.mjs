#!/usr/bin/env node
// Checks integration folders before review: required manifest.yaml fields,
// the repository layout, and the built bundle when dist/ exists.
//
// This is a quick pre-check, not the full validation. The Integration Library
// validates manifest.yaml again when a version is uploaded and is the source
// of truth.
//
// Usage:
//   node scripts/validate-manifest.mjs <integration-dir>...   check these folders
//   node scripts/validate-manifest.mjs --all                  every integration and template
//   add --release to also reject the placeholder image digest
//   add --label=<name> to name the folder in the output
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import yaml from 'js-yaml';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);

const SLUG = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
const INTEGRATION_ID = /^[a-z0-9]+(?:-[a-z0-9]+)*(?:\.[a-z0-9]+(?:-[a-z0-9]+)*)+$/;
const RUNTIME_ID = /^[a-z0-9]+(?:[-_][a-z0-9]+)*$/;
const VERSION = /^(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$/;
const INTERA_MAX = /^(?:0|[1-9]\d*)\.(?:(?:0|[1-9]\d*)\.(?:0|[1-9]\d*|x)|x)$/;
const IMAGE = /^[a-z0-9]+(?:[._-][a-z0-9]+)*(?::[0-9]+)?(?:\/[a-z0-9]+(?:[._-][a-z0-9]+)*)*@sha256:[a-f0-9]{64}$/;
const PLACEHOLDER_DIGEST = `@sha256:${'0'.repeat(64)}`;
const HEALTH_PATH = /^\/[A-Za-z0-9._~!$&'()*+,;=:@%/-]*$/;
const DATA_PATH = /^(?:\/(?!\.{1,2}(?:\/|$))[A-Za-z0-9._@+-]+)+$/;
const ENV_NAME = /^[A-Z_][A-Z0-9_]*$/;
const PROVIDES = /^((?:[a-z0-9]+(?:\.[a-z0-9]+)*)|(?:[a-z][a-z0-9_]*))@(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$/;

const CONTRACT_VERSIONS = [2];
const TOP_LEVEL = ['id', 'name', 'version', 'runtime_id', 'developer', 'compatibility', 'categories', 'roles', 'capabilities', 'license', 'provides', 'deployment'];
const COMPATIBILITY = ['intera', 'intera_min', 'intera_max', 'integration_contract'];
const CAPABILITIES = ['datasources', 'metrics', 'external_assets', 'widgets', 'writeback'];
const DOCKER_KEYS = ['type', 'image', 'port', 'health_path', 'env', 'data_path', 'settings'];
const SETTING_KEYS = ['name', 'label', 'description', 'kind', 'required', 'env', 'default'];
const SETTING_KINDS = ['string', 'secret', 'number', 'boolean'];
const SETTING_NAME = /^[a-z][a-z0-9_]{0,63}$/;
// The installation agent passes setting values through the environment of the
// docker CLI, so these names are taken.
const RESERVED_SETTING_ENV = /^(?:PATH|HOME|DOCKER_.*)$/;
const PLATFORM_ENV = /^INTERA_SERVICE_/;
const MAX_DATA_PATH = 200;
const MAX_SETTINGS = 30;
const MAX_VARIABLES = 50; // deployment.env and deployment.settings together
const MAX_ENV_VALUE = 4096;
const SECRET_NAME = /(secret|token|password|passwd|api_?key|private_?key|credential)/i;

const isRecord = (value) => typeof value === 'object' && value !== null && !Array.isArray(value);
const show = (value) => (typeof value === 'string' ? JSON.stringify(value) : String(value));

export function checkManifest(manifest, problems, warnings, { release }) {
  const fail = (field, message) => problems.push(field ? `${field}: ${message}` : message);
  if (!isRecord(manifest)) {
    fail('', 'manifest.yaml must be a mapping');
    return;
  }
  for (const key of Object.keys(manifest)) {
    if (!TOP_LEVEL.includes(key)) fail(key, 'unknown field (the catalog rejects it)');
  }
  const text = (field, value, pattern, max = 128) => {
    if (typeof value !== 'string' || value.trim() === '') return fail(field, `must be a non-empty string; got ${show(value)}`);
    if (value.length > max) return fail(field, `must be at most ${max} characters`);
    if (pattern && !pattern.test(value)) fail(field, `has an invalid format: ${show(value)}`);
  };
  const version = (field, value) => {
    if (typeof value !== 'string' || !VERSION.test(value)) fail(field, `must be a quoted version 'X.Y.Z'; got ${show(value)}`);
  };
  const list = (field, value, pattern) => {
    if (value === undefined || value === null) return;
    if (!Array.isArray(value)) return fail(field, 'must be a list');
    if (value.length > 50) fail(field, 'must have at most 50 items');
    value.forEach((item, i) => {
      if (typeof item !== 'string' || !pattern.test(item)) fail(`${field}[${i}]`, `has an invalid value: ${show(item)}`);
    });
    if (new Set(value).size !== value.length) fail(field, 'has duplicates');
  };

  text('id', manifest.id, INTEGRATION_ID);
  text('name', manifest.name);
  version('version', manifest.version);
  if (manifest.runtime_id !== undefined) text('runtime_id', manifest.runtime_id, RUNTIME_ID, 64);
  if (!isRecord(manifest.developer)) fail('developer', 'must be a mapping with id');
  else {
    text('developer.id', manifest.developer.id, SLUG, 64);
    if (typeof manifest.id === 'string' && !manifest.id.startsWith(`${manifest.developer.id}.`)) {
      fail('id', `must start with the developer id "${manifest.developer.id}."`);
    }
  }

  const compat = manifest.compatibility;
  if (!isRecord(compat)) fail('compatibility', 'must be a mapping with intera_min and integration_contract');
  else {
    for (const key of Object.keys(compat)) if (!COMPATIBILITY.includes(key)) fail(`compatibility.${key}`, 'unknown field');
    const flat = compat.intera_min !== undefined || compat.intera_max !== undefined;
    if (compat.intera !== undefined && flat) fail('compatibility', 'declare the INTERA range once: intera.min/max or intera_min/intera_max');
    const range = compat.intera !== undefined ? compat.intera : { min: compat.intera_min, max: compat.intera_max };
    const prefix = compat.intera !== undefined ? 'compatibility.intera.' : 'compatibility.intera_';
    if (!isRecord(range)) fail('compatibility.intera', 'must be a mapping with min and optional max');
    else {
      version(`${prefix}min`, range.min);
      if (range.max !== undefined && range.max !== null && (typeof range.max !== 'string' || !INTERA_MAX.test(range.max))) {
        fail(`${prefix}max`, `must be a quoted X.Y.Z, X.Y.x or X.x; got ${show(range.max)}`);
      }
    }
    if (!CONTRACT_VERSIONS.includes(compat.integration_contract)) {
      fail('compatibility.integration_contract', `must be one of ${CONTRACT_VERSIONS.join(', ')}; got ${show(compat.integration_contract)}`);
    }
  }

  list('categories', manifest.categories, SLUG);
  list('roles', manifest.roles, SLUG);
  list('provides', manifest.provides, PROVIDES);
  if (manifest.license !== undefined) text('license', manifest.license);

  if (manifest.capabilities !== undefined) {
    if (!isRecord(manifest.capabilities)) fail('capabilities', 'must be a mapping of booleans');
    else {
      for (const [key, value] of Object.entries(manifest.capabilities)) {
        if (!CAPABILITIES.includes(key)) fail(`capabilities.${key}`, 'unknown capability');
        else if (typeof value !== 'boolean') fail(`capabilities.${key}`, 'must be true or false');
      }
    }
  }

  const deployment = manifest.deployment;
  if (deployment === undefined || deployment === null) return;
  if (!isRecord(deployment)) return fail('deployment', 'must be a mapping with type');
  if (deployment.type === 'none') {
    for (const key of Object.keys(deployment)) if (key !== 'type') fail(`deployment.${key}`, 'unknown field for type none');
    return;
  }
  if (deployment.type !== 'docker') return fail('deployment.type', `must be none or docker; got ${show(deployment.type)}`);
  for (const key of Object.keys(deployment)) {
    if (!DOCKER_KEYS.includes(key)) fail(`deployment.${key}`, 'unknown field');
  }
  if (manifest.runtime_id === undefined) fail('runtime_id', 'is required to install an integration from Marketplace');
  if (typeof deployment.image !== 'string' || !IMAGE.test(deployment.image)) {
    fail('deployment.image', `must be an image pinned by digest, e.g. ghcr.io/intera-ltd/acme/billing@sha256:<64 hex>; got ${show(deployment.image)}`);
  } else {
    if (
      deployment.image.startsWith('ghcr.io/intera-ltd/') &&
      isRecord(manifest.developer) &&
      typeof manifest.developer.id === 'string' &&
      typeof manifest.id === 'string' &&
      manifest.id.startsWith(`${manifest.developer.id}.`)
    ) {
      const slug = manifest.id.slice(manifest.developer.id.length + 1);
      const expectedPrefix = `ghcr.io/intera-ltd/${manifest.developer.id}/${slug}@sha256:`;
      if (!deployment.image.startsWith(expectedPrefix)) {
        fail('deployment.image', `in ghcr.io/intera-ltd must include the developer namespace: "${expectedPrefix}<64 hex>"`);
      }
    }
    if (deployment.image.endsWith(PLACEHOLDER_DIGEST)) {
      if (release) fail('deployment.image', 'still has the placeholder digest; run scripts/set-image-digest.sh');
      else warnings.push('deployment.image: placeholder digest (CI writes the real one at release)');
    }
  }
  if (!Number.isInteger(deployment.port) || deployment.port < 1 || deployment.port > 65535) {
    fail('deployment.port', `must be an integer from 1 to 65535; got ${show(deployment.port)}`);
  }
  if (deployment.health_path !== undefined && (typeof deployment.health_path !== 'string' || !HEALTH_PATH.test(deployment.health_path))) {
    fail('deployment.health_path', `must be an absolute path such as /health; got ${show(deployment.health_path)}`);
  }
  if (deployment.data_path !== undefined && deployment.data_path !== null) {
    text('deployment.data_path', deployment.data_path, DATA_PATH, MAX_DATA_PATH);
  }
  const env = isRecord(deployment.env) ? deployment.env : {};
  if (deployment.env !== undefined) {
    if (!isRecord(deployment.env)) fail('deployment.env', 'must be a mapping');
    else {
      if (Object.keys(env).length > MAX_VARIABLES) fail('deployment.env', `must have at most ${MAX_VARIABLES} variables`);
      for (const [name, value] of Object.entries(env)) {
        if (!ENV_NAME.test(name)) fail(`deployment.env.${name}`, 'must be an upper snake case name');
        else if (PLATFORM_ENV.test(name)) fail(`deployment.env.${name}`, `${name} is reserved: INTERA_SERVICE_* are set by Core`);
        if (typeof value !== 'string' || value.length > MAX_ENV_VALUE) fail(`deployment.env.${name}`, `must be a quoted string of at most ${MAX_ENV_VALUE} characters`);
        if (!PLATFORM_ENV.test(name) && SECRET_NAME.test(name)) {
          fail(`deployment.env.${name}`, 'looks like a secret; manifests are public: declare it in deployment.settings with kind: secret');
        }
      }
    }
  }
  checkSettings(deployment.settings, env, fail, warnings);
}

/** A setting's default has the type of its kind; a secret has none. */
function checkSettingDefault(setting, at, fail) {
  const value = setting.default;
  if (value === undefined || value === null) return;
  const field = `${at}.default`;
  if (setting.kind === 'secret') fail(field, 'a secret has no default: the installation sets it');
  else if (setting.kind === 'number' && (typeof value !== 'number' || !Number.isFinite(value))) fail(field, `must be a number; got ${show(value)}`);
  else if (setting.kind === 'boolean' && typeof value !== 'boolean') fail(field, `must be true or false; got ${show(value)}`);
  else if (setting.kind === 'string' && (typeof value !== 'string' || value.length > MAX_ENV_VALUE)) {
    fail(field, `must be a string of at most ${MAX_ENV_VALUE} characters; got ${show(value)}`);
  }
}

/**
 * deployment.settings: values of the container that the administrator of the
 * installation sets. Names and variables are unique and do not repeat
 * deployment.env; env and settings together hold at most 50 variables.
 */
function checkSettings(settings, env, fail, warnings) {
  if (settings === undefined || settings === null) return;
  if (!Array.isArray(settings)) return fail('deployment.settings', 'must be a list');
  if (settings.length > MAX_SETTINGS) return fail('deployment.settings', `must have at most ${MAX_SETTINGS} settings`);
  const names = new Set();
  const variables = new Set(Object.keys(env));
  let count = 0;
  settings.forEach((setting, i) => {
    const at = `deployment.settings[${i}]`;
    if (!isRecord(setting)) return fail(at, `must be a mapping with name, label and kind; got ${show(setting)}`);
    for (const key of Object.keys(setting)) if (!SETTING_KEYS.includes(key)) fail(`${at}.${key}`, 'unknown field');
    let ok = true;
    const { name, label, description, kind, required } = setting;
    if (typeof name !== 'string' || !SETTING_NAME.test(name)) {
      fail(`${at}.name`, `must be a lowercase key a-z 0-9 _ that starts with a letter, up to 64 characters; got ${show(name)}`);
      ok = false;
    }
    if (typeof label !== 'string' || label.trim() === '' || label.length > 128) {
      fail(`${at}.label`, `must be a non-empty string of at most 128 characters; got ${show(label)}`);
      ok = false;
    }
    if (description !== undefined && description !== null && (typeof description !== 'string' || description.length > 500)) {
      fail(`${at}.description`, 'must be a string of at most 500 characters');
    }
    if (!SETTING_KINDS.includes(kind)) {
      fail(`${at}.kind`, `must be one of ${SETTING_KINDS.join(', ')}; got ${show(kind)}`);
      ok = false;
    }
    if (required !== undefined && typeof required !== 'boolean') fail(`${at}.required`, `must be true or false; got ${show(required)}`);
    let variable = typeof name === 'string' ? name.toUpperCase() : null;
    if (setting.env !== undefined && setting.env !== null) {
      variable = setting.env;
      if (typeof variable !== 'string' || variable.length > 64 || !ENV_NAME.test(variable)) {
        fail(`${at}.env`, `must be an upper snake case name of at most 64 characters; got ${show(variable)}`);
        variable = null;
      }
    }
    if (variable !== null && RESERVED_SETTING_ENV.test(variable)) {
      fail(`${at}.env`, `${variable} is reserved (PATH, HOME and DOCKER_* are taken by the installation agent)`);
      variable = null;
    } else if (variable !== null && PLATFORM_ENV.test(variable)) {
      fail(`${at}.env`, `${variable} is reserved: INTERA_SERVICE_* are set by Core`);
      variable = null;
    }
    if (SETTING_KINDS.includes(kind)) checkSettingDefault(setting, at, fail);
    if (kind !== 'secret' && typeof name === 'string' && (SECRET_NAME.test(name) || SECRET_NAME.test(variable ?? ''))) {
      warnings.push(`${at}: ${show(name)} looks like a secret; use kind: secret so the installation stores it encrypted`);
    }
    if (!ok || variable === null) return;
    if (names.has(name)) return fail(`${at}.name`, `duplicates ${show(name)}`);
    if (variables.has(variable)) return fail(`${at}.env`, `${variable} is already set by deployment.env or another setting`);
    names.add(name);
    variables.add(variable);
    count += 1;
  });
  if (Object.keys(env).length + count > MAX_VARIABLES) {
    fail('deployment.settings', `env and settings together must have at most ${MAX_VARIABLES} variables`);
  }
}

export const REPOSITORY_LICENSE = 'Apache-2.0';
const LICENSE_FILES = ['LICENSE', 'LICENSE.md', 'LICENSE.txt'];

/**
 * License of an integration folder. The repository license (Apache-2.0)
 * applies unless the folder brings its own: then a LICENSE file in the folder
 * and the `license` field in manifest.yaml are both required.
 */
export function checkLicense(license, hasLicenseFile, problems, warnings) {
  if (license === undefined || license === null) {
    if (hasLicenseFile) problems.push('the folder has a LICENSE file, so manifest.yaml must name it in license');
    else warnings.push(`license: not set; the repository license ${REPOSITORY_LICENSE} applies, declare it so the catalog shows it`);
    return;
  }
  if (typeof license === 'string' && license !== REPOSITORY_LICENSE && !hasLicenseFile) {
    problems.push(`license: ${show(license)} differs from the repository license ${REPOSITORY_LICENSE}; add its text as LICENSE in the integration folder`);
  }
}

/** Layout rules for integrations/<developer>/<slug>/. */
function checkLayout(dir, manifest, problems, warnings) {
  const rel = path.relative(ROOT, dir).split(path.sep);
  if (rel[0] !== 'integrations') return;
  const [, developer, slug] = rel;
  if (rel.length !== 3) return problems.push('integration folders live at integrations/<developer>/<integration>/');
  if (manifest?.developer?.id !== developer) problems.push(`developer.id must be "${developer}" (the folder name)`);
  if (manifest?.id !== `${developer}.${slug}`) problems.push(`id must be "${developer}.${slug}" (the folder names)`);
  for (const file of ['README.md', 'CHANGELOG.md', 'bundle/package.json', 'bundle/package-lock.json']) {
    if (!fs.existsSync(path.join(dir, file))) problems.push(`missing ${file}`);
  }
  const changelog = path.join(dir, 'CHANGELOG.md');
  if (fs.existsSync(changelog) && typeof manifest?.version === 'string' && !fs.readFileSync(changelog, 'utf8').includes(`[${manifest.version}]`)) {
    problems.push(`CHANGELOG.md has no entry for [${manifest.version}]`);
  }
  if (manifest?.deployment?.type === 'docker' && !fs.existsSync(path.join(dir, 'service', 'Dockerfile'))) {
    problems.push('deployment.type is docker but service/Dockerfile is missing');
  }
  const hasLicenseFile = LICENSE_FILES.some((file) => fs.existsSync(path.join(dir, file)));
  checkLicense(isRecord(manifest) ? manifest.license : undefined, hasLicenseFile, problems, warnings);
}

/** When the bundle is built, its runtime manifest must agree with manifest.yaml. */
function checkBuiltBundle(dir, manifest, problems, warnings) {
  const pkgFile = path.join(dir, 'bundle', 'package.json');
  if (fs.existsSync(pkgFile)) {
    const pkg = JSON.parse(fs.readFileSync(pkgFile, 'utf8'));
    if (pkg.version !== manifest.version) problems.push(`bundle/package.json version "${pkg.version}" must equal manifest.yaml version "${manifest.version}"`);
  }
  const entry = path.join(dir, 'bundle', 'dist', 'index.js');
  if (!fs.existsSync(entry)) {
    warnings.push('bundle/dist/index.js not built; bundle checks skipped');
    return;
  }
  let mod;
  try {
    mod = require(entry);
  } catch (error) {
    problems.push(`bundle/dist/index.js does not load: ${error.message}`);
    return;
  }
  const runtime = (mod.default && mod.default.manifest ? mod.default : mod).manifest;
  if (!isRecord(runtime)) return problems.push('bundle/dist/index.js does not export manifest');
  if (runtime.version !== manifest.version) problems.push(`bundle manifest.version "${runtime.version}" must equal manifest.yaml version "${manifest.version}"`);
  if (manifest.runtime_id !== undefined && runtime.id !== manifest.runtime_id) {
    problems.push(`bundle manifest.id "${runtime.id}" must equal runtime_id "${manifest.runtime_id}"`);
  }
  const contract = runtime.compatibility?.integration_contract;
  if (contract !== undefined && contract !== manifest.compatibility?.integration_contract) {
    problems.push('bundle compatibility.integration_contract must equal the one in manifest.yaml');
  }
}

function listAll() {
  const dirs = [];
  const base = path.join(ROOT, 'integrations');
  for (const developer of fs.existsSync(base) ? fs.readdirSync(base, { withFileTypes: true }) : []) {
    if (!developer.isDirectory()) continue;
    for (const integration of fs.readdirSync(path.join(base, developer.name), { withFileTypes: true })) {
      if (integration.isDirectory()) dirs.push(path.join(base, developer.name, integration.name));
    }
  }
  const templates = path.join(ROOT, 'templates');
  for (const entry of fs.existsSync(templates) ? fs.readdirSync(templates, { withFileTypes: true }) : []) {
    if (entry.isDirectory()) dirs.push(path.join(templates, entry.name));
  }
  return dirs;
}

/** Image names are ghcr.io/intera-ltd/<developer>/<integration>, scoped per developer. */
function checkUniqueSlugs(failures) {
  const seen = new Set();
  for (const dir of listAll()) {
    const rel = path.relative(ROOT, dir).split(path.sep);
    if (rel[0] !== 'integrations') continue;
    const key = `${rel[1]}/${rel[2]}`;
    if (seen.has(key)) failures.push(`integration folder "${key}" is listed more than once`);
    seen.add(key);
  }
}

function main(argv) {
  const release = argv.includes('--release');
  const labelArg = argv.find((arg) => arg.startsWith('--label='));
  const args = argv.filter((arg) => !arg.startsWith('--'));
  const dirs = argv.includes('--all') ? listAll() : args.map((arg) => path.resolve(arg));
  if (dirs.length === 0) {
    console.error('usage: validate-manifest.mjs [--release] (--all | <integration-dir>...)');
    return 2;
  }
  let failed = false;
  for (const dir of dirs) {
    const label = labelArg ? labelArg.slice('--label='.length) : path.relative(ROOT, dir) || '.';
    const problems = [];
    const warnings = [];
    const file = path.join(dir, 'manifest.yaml');
    let manifest;
    if (!fs.existsSync(file)) problems.push('manifest.yaml not found');
    else {
      try {
        manifest = yaml.load(fs.readFileSync(file, 'utf8'), { schema: yaml.CORE_SCHEMA, filename: file });
      } catch (error) {
        problems.push(`manifest.yaml is not valid YAML: ${error.message}`);
      }
    }
    if (manifest !== undefined) {
      checkManifest(manifest, problems, warnings, { release });
      checkLayout(dir, manifest, problems, warnings);
      if (isRecord(manifest)) checkBuiltBundle(dir, manifest, problems, warnings);
    }
    for (const warning of warnings) console.log(`::warning title=${label}::${warning}`);
    if (problems.length === 0) console.log(`ok  ${label}`);
    else {
      failed = true;
      console.log(`FAIL ${label}`);
      for (const problem of problems) console.log(`::error title=${label}::${problem}`);
    }
  }
  const repoProblems = [];
  checkUniqueSlugs(repoProblems);
  for (const problem of repoProblems) console.log(`::error::${problem}`);
  return failed || repoProblems.length > 0 ? 1 : 0;
}

if (process.argv[1] && fs.realpathSync(path.resolve(process.argv[1])) === fs.realpathSync(fileURLToPath(import.meta.url))) {
  process.exitCode = main(process.argv.slice(2));
}
