#!/usr/bin/env node
// Prints a JSON list of integration and template folders changed between two
// commits, for the CI matrix. A change to shared tooling (scripts/, the root
// package files or the workflows) selects every folder.
//
// Usage: node scripts/changed-integrations.mjs <base-sha> <head-sha>
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';

const [base, head] = process.argv.slice(2);
if (!base || !head) {
  console.error('usage: changed-integrations.mjs <base-sha> <head-sha>');
  process.exit(2);
}
const SHA = /^[0-9a-f]{7,64}$/;
if (!SHA.test(base) || !SHA.test(head)) {
  console.error('base and head must be commit SHAs');
  process.exit(2);
}

const SLUG = '[a-z0-9]+(?:-[a-z0-9]+)*';
const FOLDER = new RegExp(`^(integrations/${SLUG}/${SLUG}|templates/${SLUG})/`);
const SHARED = /^(scripts\/|shared\/|package(-lock)?\.json$|\.github\/workflows\/)/;

const changed = execFileSync('git', ['diff', '--name-only', `${base}...${head}`], { encoding: 'utf8' })
  .split('\n')
  .filter(Boolean);

const all = () => {
  const dirs = [];
  for (const developer of fs.existsSync('integrations') ? fs.readdirSync('integrations', { withFileTypes: true }) : []) {
    if (!developer.isDirectory()) continue;
    for (const integration of fs.readdirSync(`integrations/${developer.name}`, { withFileTypes: true })) {
      if (integration.isDirectory()) dirs.push(`integrations/${developer.name}/${integration.name}`);
    }
  }
  for (const template of fs.existsSync('templates') ? fs.readdirSync('templates', { withFileTypes: true }) : []) {
    if (template.isDirectory()) dirs.push(`templates/${template.name}`);
  }
  return dirs;
};

const selected = new Set();
if (changed.some((file) => SHARED.test(file))) {
  for (const dir of all()) selected.add(dir);
} else {
  for (const file of changed) {
    const match = FOLDER.exec(file);
    if (match) selected.add(match[1]);
  }
}
// Only folders that still exist and have a bundle (deleted ones need no build),
// and only names that match the slug rules (they end up in shell commands).
const strict = new RegExp(`^(integrations/${SLUG}/${SLUG}|templates/${SLUG})$`);
const dirs = [...selected]
  .filter((dir) => strict.test(dir) && fs.existsSync(`${dir}/bundle/package.json`))
  .sort();
process.stdout.write(JSON.stringify(dirs));
