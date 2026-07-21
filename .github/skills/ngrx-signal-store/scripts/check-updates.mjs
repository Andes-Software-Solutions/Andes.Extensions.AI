#!/usr/bin/env node
// Drift check for the ngrx-signal-store skill.
//
// Compares the pinned snapshot in sources.json against the live NgRx docs and the
// published @ngrx/signals version.
//
//   node check-updates.mjs            human-readable report
//   node check-updates.mjs --json     machine-readable report
//   node check-updates.mjs --pin      rewrite sources.json from live upstream
//
// Exit codes are the contract that /ngrx-signals-sync branches on:
//   0  up to date        10  drift detected        1  error (network, rate limit, parse)
// Keeping 1 distinct from 10 is what stops a rate-limit blip from being read as a doc change.
//
// The walk lists every directory rather than short-circuiting on the subdirectory's tree
// sha. The shortcut would save one request a week but makes a stale per-file sha in the
// manifest undetectable, and it silently fails closed -- reporting "up to date" forever is
// the one failure mode this script exists to prevent.

import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const HERE = dirname(fileURLToPath(import.meta.url));
const SOURCES = join(HERE, '..', 'sources.json');

const args = new Set(process.argv.slice(2));
const PIN = args.has('--pin');
const JSON_OUT = args.has('--json');

const token = process.env.GITHUB_TOKEN || process.env.GH_TOKEN;
const headers = {
  // GitHub rejects API requests that arrive without a User-Agent.
  'User-Agent': 'ngrx-signal-store-skill-sync',
  Accept: 'application/vnd.github+json',
  ...(token ? { Authorization: `Bearer ${token}` } : {}),
};

const manifest = JSON.parse(readFileSync(SOURCES, 'utf8'));
const { docsRoot, contentsApi, branch, rawBase, packageRegistry } = manifest.upstream;

async function getJson(url, extraHeaders = headers) {
  const res = await fetch(url, { headers: extraHeaders });
  if (!res.ok) {
    const hint =
      res.status === 403 && !token
        ? ' (unauthenticated GitHub API allows 60 req/hour; set GITHUB_TOKEN to raise it)'
        : '';
    throw new Error(`GET ${url} -> ${res.status} ${res.statusText}${hint}`);
  }
  return res.json();
}

/**
 * Walks the docs tree and returns path -> blobSha for every markdown page,
 * where path is relative to docsRoot. Recurses into subdirectories so a newly
 * added upstream folder is picked up rather than silently ignored.
 */
async function walk(prefix = '') {
  const path = prefix ? `${docsRoot}/${prefix}` : docsRoot;
  const entries = await getJson(`${contentsApi}/${path}?ref=${branch}`);
  const pages = new Map();
  for (const entry of entries) {
    const rel = prefix ? `${prefix}/${entry.name}` : entry.name;
    if (entry.type === 'file' && entry.name.endsWith('.md')) {
      pages.set(rel, entry.sha);
    } else if (entry.type === 'dir') {
      for (const [k, v] of await walk(rel)) pages.set(k, v);
    }
  }
  return pages;
}

async function latestVersion() {
  const { version } = await getJson(packageRegistry, { 'User-Agent': headers['User-Agent'] });
  return version;
}

// --- pin -------------------------------------------------------------------
// Rewrites every sha from live upstream while preserving the hand-authored mapsTo
// entries. Transcribing 17 shas by hand is exactly the work a model should never do.
if (PIN) {
  try {
    const [live, version] = await Promise.all([walk(), latestVersion()]);
    const existing = new Map(manifest.pages.map((p) => [p.path, p]));

    manifest.pages = [...live.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([path, blobSha]) => {
        const prev = existing.get(path);
        if (!prev) console.error(`NOTE: new upstream page, mapsTo is empty until you set it: ${path}`);
        return { path, blobSha, mapsTo: prev?.mapsTo ?? [] };
      });
    manifest.pinned = {
      packageVersion: version,
      pinnedAt: new Date().toISOString().slice(0, 10),
    };

    writeFileSync(SOURCES, JSON.stringify(manifest, null, 2) + '\n');
    console.log(`Pinned ${manifest.pages.length} pages at @ngrx/signals ${version}.`);
    process.exit(0);
  } catch (err) {
    console.error(`Pin failed: ${err.message}`);
    process.exit(1);
  }
}

// --- check -----------------------------------------------------------------
try {
  const [live, version] = await Promise.all([walk(), latestVersion()]);
  const pinned = new Map(manifest.pages.map((p) => [p.path, p]));

  const changed = [...live.entries()]
    .filter(([path, sha]) => pinned.has(path) && pinned.get(path).blobSha !== sha)
    .map(([path]) => pinned.get(path));
  const added = [...live.keys()].filter((p) => !pinned.has(p));
  const removed = [...pinned.keys()].filter((p) => !live.has(p));
  const versionChanged = version !== manifest.pinned.packageVersion;

  if (!changed.length && !added.length && !removed.length && !versionChanged) {
    const msg = `NgRx docs up to date (@ngrx/signals ${version}, ${manifest.pages.length} pages, pinned ${manifest.pinned.pinnedAt}).`;
    console.log(JSON_OUT ? JSON.stringify({ status: 'current', version }) : msg);
    process.exit(0);
  }

  const report = {
    status: 'drift',
    version: { pinned: manifest.pinned.packageVersion, latest: version, changed: versionChanged },
    changed: changed.map((p) => p.path),
    added,
    removed,
    affectedFiles: [...new Set(changed.flatMap((p) => p.mapsTo))].sort(),
    rawUrls: Object.fromEntries(changed.map((p) => [p.path, `${rawBase}${docsRoot}/${p.path}`])),
  };

  if (JSON_OUT) {
    console.log(JSON.stringify(report, null, 2));
  } else {
    console.log('NgRx docs have drifted from the pinned snapshot.\n');
    if (versionChanged) console.log(`  version:  ${manifest.pinned.packageVersion} -> ${version}`);
    if (changed.length) console.log(`  changed:  ${report.changed.join(', ')}`);
    if (added.length) console.log(`  added:    ${added.join(', ')}  (needs a mapsTo decision)`);
    if (removed.length) console.log(`  removed:  ${removed.join(', ')}`);
    if (report.affectedFiles.length) console.log(`\n  files to review: ${report.affectedFiles.join(', ')}`);
  }
  process.exit(10);
} catch (err) {
  console.error(`Check failed: ${err.message}`);
  process.exit(1);
}
