---
description: Check the upstream NgRx Signals docs for changes and refresh the ngrx-signal-store skill if they have drifted.
argument-hint: "[--check-only]"
allowed-tools: Bash(node:*), Bash(curl:*), Bash(git status:*), Bash(git diff:*), Read, Edit, Glob, Grep
---

Refresh the `ngrx-signal-store` skill against the official NgRx docs.

The skill is pinned to a snapshot recorded in `.claude/skills/ngrx-signal-store/sources.json`: a blob sha per upstream doc page, the `@ngrx/signals` version, and a `mapsTo` list saying which skill file each page feeds.

## 1. Check for drift

```bash
node .claude/skills/ngrx-signal-store/scripts/check-updates.mjs --json
```

Branch on the exit code — it is the contract:

- **`0` — up to date.** Print the one-line summary and **stop immediately**. Do not read files, do not edit anything, do not touch git. This is the path that runs almost every week and it should cost close to nothing. Resist the urge to "just double-check" the references; the shas already did.
- **`1` — the check failed** (network, rate limit, bad JSON). Report the error and stop. Do not edit anything on the basis of a failed check — a rate-limited run tells you nothing about upstream.
- **`10` — drift detected.** Continue below.

If the user passed `--check-only`, print the drift report and stop here without editing.

## 2. Read what actually changed

The report gives you `changed` (pages whose content moved), `added`, `removed`, `affectedFiles` (the union of the changed pages' `mapsTo`), `rawUrls`, and a `version` delta.

Fetch **only the changed pages**:

```bash
curl -sSL <rawUrl>
```

Upstream markdown wraps code in `ngrx-code-example`, `ngrx-code-tabs`, and `ngrx-docs-alert` web components. Strip the wrappers, but read the `inform` and `warn` alerts carefully — that is where the official best-practice guidance lives, and a change to one of those is usually the most important thing in the diff.

## 3. Propagate only what matters

Read the affected skill file(s) named in `affectedFiles` and compare semantically.

Propagate: new, renamed, or removed APIs; a changed default; a changed recommendation; a new idiom in an example; a deprecation; a version bump.

Ignore: prose reflow, typo fixes, link changes, and site-chrome churn. A blob sha moves for all of those. Rewriting a reference file on every sha change is how the skill slowly degrades into a bad paraphrase of the docs — the sha tells you *where to look*, not *what to write*.

Then apply **minimal** edits:

- Patch the affected `references/*.md` file(s).
- Touch `SKILL.md` only if a decision rule, a production default, a trap, or the pinned version line actually changed. Its body loads on every trigger, so it earns its length.
- If a page appears in `added`, do not silently drop it: say what it covers, propose which reference file it should map to, and add it to `pages` in `sources.json` with that `mapsTo` once the user agrees. A page with an empty `mapsTo` is invisible to future syncs — that is the one way this pipeline can quietly rot.
- If a page appears in `removed`, check whether the skill still documents something upstream has dropped.

## 4. Re-pin

```bash
node .claude/skills/ngrx-signal-store/scripts/check-updates.mjs --pin
```

This rewrites the shas, the version, and `pinnedAt` from live upstream. Do not hand-edit shas — the script is there so that no one has to transcribe seventeen hex strings correctly.

## 5. Report, and leave the diff for review

Summarize: which pages changed, what materially changed in each, which skill files you edited, and anything you deliberately chose not to propagate.

**Do not commit, do not push, do not open a PR.** Leave the edits in the working tree.

This repo *is* the guidance, and there are no tests that would catch a bad semantic diff. An unattended job that quietly rewrites the guidance and commits it would surface its mistakes weeks later, as advice NgRx never gave. `git diff` is the review surface and `git checkout --` is the undo; a human decides.
