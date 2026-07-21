# awesome-claude-copilot

A curated, reusable **[Claude Code](https://code.claude.com)** configuration for **C#/.NET back ends and Angular front ends**, packaged so you can drop it into your own repository.

There is no application source code here. This repo is purely a portable set of assistant configuration — project memory, path-scoped rules, skills, subagents, and MCP servers — that encodes a team's standards once instead of re-explaining them in every prompt.

> The repo ships two parallel trees: `.claude/` for Claude Code and `.github/` for GitHub Copilot — repository instructions (`copilot-instructions.md`), path-scoped instructions (`instructions/*.instructions.md`), custom agents (`agents/*.agent.md`), and a `skills/` tree mirrored byte-for-byte from `.claude/skills/`. The `.github/` tree is self-contained: nothing in it references `.claude/`.

---

## Repository structure

```
.
├── .claude/
│   ├── CLAUDE.md                 # Always-loaded project memory: C# + Angular standards, delegation rules
│   ├── rules/                    # Path-scoped rules (auto-apply when a matching file is edited)
│   │   ├── csharp.md                       → **/*.cs
│   │   ├── aspnet-rest-apis.md             → **/*.cs, **/*.json
│   │   ├── azure-functions-csharp.md       → **/*.cs, **/host.json, **/local.settings.json, **/*.csproj
│   │   ├── blazor.md                       → **/*.razor, **/*.razor.cs, **/*.razor.css
│   │   └── csharp-mcp-server.md            → **/*.cs, **/*.csproj
│   ├── skills/                   # Invokable skills (Skill tool)
│   │   ├── csharp-async/SKILL.md
│   │   ├── csharp-docs/SKILL.md
│   │   ├── csharp-xunit/SKILL.md
│   │   └── ngrx-signal-store/    # Progressive-disclosure skill (see below)
│   │       ├── SKILL.md
│   │       ├── sources.json                # pinned upstream doc shas + @ngrx/signals version
│   │       ├── scripts/check-updates.mjs   # drift check against the live NgRx docs
│   │       └── references/                 # read on demand, not loaded up front
│   ├── agents/                   # Subagents
│   │   ├── csharp-code-reviewer.md         # Sonnet, read-only C#/.NET review
│   │   ├── angular-code-reviewer.md        # Sonnet, read-only Angular review
│   │   └── se-technical-writer.md          # Haiku, writes docs under docs/
│   ├── commands/                 # Slash commands
│   │   └── ngrx-signals-sync.md            # refresh the NgRx skill from upstream docs
│   └── settings.json             # Model + MCP defaults
│
└── .mcp.json                     # MCP servers
```

---

## What's covered

**C#/.NET** (latest C# / C# 14): file-scoped namespaces, pattern matching, `nameof`; PascalCase/camelCase and `I`-prefixed interfaces; nullable reference types with `is null` / `is not null`; async (`Async` suffix, no `.Result`/`.Wait()`/`async void`, `CancellationToken`, `ConfigureAwait(false)`); validation (FluentValidation/DataAnnotations) and Problem Details (RFC 9457); `ILogger<T>` structured logging, never logging PII/secrets, `DefaultAzureCredential` + Key Vault; XML doc comments; xUnit conventions.

Framework specifics live in `.claude/rules/`, which auto-load when you edit a matching file:

| Topic | Rule |
| --- | --- |
| General C# | `csharp.md` |
| ASP.NET Core REST APIs | `aspnet-rest-apis.md` |
| Azure Functions (isolated worker) | `azure-functions-csharp.md` |
| Blazor | `blazor.md` |
| MCP servers in C# | `csharp-mcp-server.md` |

**Angular**: NgRx Signal Store state management — see below.

**Skills**: `csharp-async`, `csharp-docs`, `csharp-xunit`, `ngrx-signal-store`.

---

## Angular / NgRx Signal Store

`ngrx-signal-store` is the most involved skill here, and the only self-updating one.

It uses **progressive disclosure**. `SKILL.md` is short and loads whenever the skill triggers: the mental model, the production defaults (keep `protectedState` on, inject via default parameters, standalone state updaters), the decision rules that are easiest to get wrong (`signalState` vs `signalStore`; `rxMethod` vs `signalMethod` vs a plain method; when a custom feature or the Events plugin is actually warranted), and the traps — including that `withDevtools` is *not* part of core NgRx, a common hallucination.

Everything else sits in `references/` and is read only when the task calls for it:

| Reference | Read when |
| --- | --- |
| `store-composition.md` | Authoring or reshaping a store's structure |
| `async-and-rxjs.md` | The store talks to HTTP or any async source |
| `entity-management.md` | State holds a keyed collection |
| `custom-features.md` | Logic repeats across stores |
| `testing.md` | Writing or fixing store tests |
| `events-plugin.md` | Event-based state, or several stores reacting to one event |
| `recipes.md` | Starting a new store from a known-good shape |
| `api-reference.md` | Checking a signature, import path, or entry point |

### Keeping it current

NgRx guidance goes stale, and a skill that confidently teaches last year's API is worse than no skill. So the skill is **pinned** to a snapshot of the upstream docs in `sources.json` — a blob sha per doc page, the `@ngrx/signals` version it was written against, and a `mapsTo` list saying which reference file each upstream page feeds.

```bash
# Is the skill still current? Exit 0 = yes, 10 = drifted, 1 = check failed.
node .claude/skills/ngrx-signal-store/scripts/check-updates.mjs
```

The check costs a handful of unauthenticated HTTP requests (set `GITHUB_TOKEN` to raise the 60/hour limit; a weekly cadence never approaches it). Run the whole refresh through the slash command:

```
/ngrx-signals-sync              # check, and update the skill if upstream moved
/ngrx-signals-sync --check-only # report drift without editing anything
```

When nothing has changed it prints one line and stops. When something has, it fetches only the changed pages, propagates the substantive differences into the reference files named by `mapsTo`, re-pins the shas, and **leaves the edits in the working tree for you to review** — it never commits. This repo *is* the guidance, and there are no tests that would catch a bad semantic diff, so a human approves it.

To run it on a schedule, drive the command from a loop:

```
/loop 7d /ngrx-signals-sync
```

A `/loop` only fires while a Claude Code session is open, so treat it as a convenience rather than a guarantee. Because all the logic lives in the command and the script, the same refresh can be driven by `/schedule` as a real cron routine, or by CI (fail the job on exit code 10), without changing the skill.

> The docs are fetched from the markdown behind `ngrx.io` (`ngrx/platform`, `projects/www/src/app/pages/guide/signals/`). `ngrx.io` itself is a JavaScript SPA and cannot be scraped — fetching it returns an empty nav shell, which is why the pipeline points at the source repo.

---

## MCP servers

Configured in `.mcp.json`; `.claude/settings.json` sets `enableAllProjectMcpServers: true`.

| Server | Transport | Use |
| --- | --- | --- |
| `microsoft-learn` | HTTP (`https://learn.microsoft.com/api/mcp`) | Ground .NET/Azure answers in official Microsoft Learn docs |
| `angular-cli` | stdio (`npx @angular/cli mcp`) | Ground Angular answers in the installed Angular version |
| `terraform` | stdio (Docker: `hashicorp/terraform-mcp-server`) | Infrastructure-as-code |

---

## Getting started

1. Copy [`.claude/`](.claude/) and [`.mcp.json`](.mcp.json) into the root of your repository.
2. Start Claude Code there. It loads `.claude/CLAUDE.md` every session, and the matching `.claude/rules/*.md` whenever you edit a relevant file.
3. The skills, the `csharp-code-reviewer` and `se-technical-writer` subagents, and the `/ngrx-signals-sync` command become available. Delegation is described in `CLAUDE.md`: after changing C#, `csharp-code-reviewer` reviews it (read-only); new features or implementation notes go to `se-technical-writer`, which writes Markdown under `docs/`.

Requirements: Node 18+ for the NgRx sync script (it uses global `fetch` and has no dependencies).

---

## Conventions for contributors

- **Rules:** a `.claude/rules/*.md` file with a `paths:` block list applies only to matching files; without `paths:` it loads at launch for every session.
- **Subagents:** `.claude/agents/*.md` frontmatter uses `name`, `description`, a `model` (`opus`/`sonnet`/`haiku`/`inherit`), and either a comma-separated `tools` list or a `skills:` list.
- **Skills:** one folder per skill containing `SKILL.md` with `name` and `description` frontmatter. Keep `SKILL.md` under ~500 lines; anything longer belongs in `references/`, pointed to from a table that says *when* to read each file.
- **Never hand-edit the shas in `sources.json`.** They are machine-maintained — run `node .claude/skills/ngrx-signal-store/scripts/check-updates.mjs --pin`.
