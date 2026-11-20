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
const ENV_NAME = /^[A-Z_][A-Z0-9_]*$/;
const PROVIDES = /^((?:[a-z0-9]+(?:\.[a-z0-9]+)*)|(?:[a-z][a-z0-9_]*))@(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$/;

const CONTRACT_VERSIONS = [2];
const TOP_LEVEL = ['id', 'name', 'version', 'runtime_id', 'developer', 'compatibility', 'categories', 'roles', 'capabilities', 'license', 'provides', 'deployment'];
const COMPATIBILITY = ['intera', 'intera_min', 'intera_max', 'integration_contract'];
const CAPABILITIES = ['datasources', 'metrics', 'external_assets', 'widgets', 'writeback'];
const DOCKER_KEYS = ['type', 'image', 'port', 'health_path', 'env'];

const isRecord = (value) => typeof value === 'object' && value !== null && !Array.isArray(value);
const show = (value) => (typeof value === 'string' ? JSON.stringify(value) : String(value));

function checkManifest(manifest, problems, warnings, { release }) {
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
    if (!DOCKER_KEYS.includes(key)) fail(`deployment.${key}`, 'unknown field (deployment.settings is coming in the next platform release)');
  }
  if (manifest.runtime_id === undefined) fail('runtime_id', 'is required to install an integration from Marketplace');
  if (typeof deployment.image !== 'string' || !IMAGE.test(deployment.image)) {
    fail('deployment.image', `must be an image pinned by digest, e.g. ghcr.io/acme/billing@sha256:<64 hex>; got ${show(deployment.image)}`);
  } else if (deployment.image.endsWith(PLACEHOLDER_DIGEST)) {
    if (release) fail('deployment.image', 'still has the placeholder digest; run scripts/set-image-digest.sh');
    else warnings.push('deployment.image: placeholder digest (CI writes the real one at release)');
  }
  if (!Number.isInteger(deployment.port) || deployment.port < 1 || deployment.port > 65535) {
    fail('deployment.port', `must be an integer from 1 to 65535; got ${show(deployment.port)}`);
  }
  if (deployment.health_path !== undefined && (typeof deployment.health_path !== 'string' || !HEALTH_PATH.test(deployment.health_path))) {
    fail('deployment.health_path', `must be an absolute path such as /health; got ${show(deployment.health_path)}`);
  }
  if (deployment.env !== undefined) {
    if (!isRecord(deployment.env)) fail('deployment.env', 'must be a mapping');
    else {
      if (Object.keys(deployment.env).length > 50) fail('deployment.env', 'must have at most 50 variables');
      for (const [name, value] of Object.entries(deployment.env)) {
        if (!ENV_NAME.test(name)) fail(`deployment.env.${name}`, 'must be an upper snake case name');
        if (typeof value !== 'string') fail(`deployment.env.${name}`, 'must be a quoted string');
        if (/(secret|token|password|passwd|api_?key|private_?key|credential)/i.test(name)) {
          fail(`deployment.env.${name}`, 'looks like a secret; manifests are public, never put secrets in them');
        }
      }
    }
  }
}

/** Layout rules for integrations/<developer>/<slug>/. */
function checkLayout(dir, manifest, problems) {
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

/** Image names are ghcr.io/intera-ltd/<integration>, so slugs must be unique. */
function checkUniqueSlugs(failures) {
  const owners = new Map();
  for (const dir of listAll()) {
    const rel = path.relative(ROOT, dir).split(path.sep);
    if (rel[0] !== 'integrations') continue;
    const list = owners.get(rel[2]) ?? [];
    list.push(rel.join('/'));
    owners.set(rel[2], list);
  }
  for (const [slug, dirs] of owners) {
    if (dirs.length > 1) failures.push(`integration slug "${slug}" is used more than once: ${dirs.join(', ')}`);
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
      checkLayout(dir, manifest, problems);
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

process.exitCode = main(process.argv.slice(2));
