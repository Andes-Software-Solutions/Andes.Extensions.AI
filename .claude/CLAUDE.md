# CLAUDE.md

Reusable project memory for **C#/.NET back ends and Angular front ends**. It loads automatically every session and governs how Claude Code works in this project. Drop it into a repository and follow it for all C# and Angular work.

## Repository layout (`.claude/`)

- `CLAUDE.md` — this file (always-on standards + delegation rules).
- `rules/` — detailed standards that **auto-apply by file type** (see [Detailed standards](#detailed-standards--clauderules)).
- `skills/` — invokable best-practice skills (`angular-developer`, `csharp-async`, `csharp-docs`, `csharp-xunit`, `ngrx-signal-store`).
- `agents/` — subagents (`angular-code-reviewer`, `csharp-code-reviewer`, `se-technical-writer`).
- `commands/` — slash commands (`/ngrx-signals-sync`).
- `settings.json` and `.mcp.json` (repo root) — model and MCP server configuration.

## C# coding standards (always)

Apply these to all C# you write or review. The detailed source of truth is in `.claude/rules/` and the skills in `.claude/skills/`.

**Language & formatting**

- Target the latest C# language version (currently **C# 14**).
- File-scoped namespaces; single-line `using` directives; honor `.editorconfig`.
- Prefer pattern matching and switch expressions.
- Use `nameof(...)` instead of string literals for member names.
- Put a newline before the opening `{` of every block; keep a method's final `return` on its own line.

**Naming**

- PascalCase for types, methods, and public members; camelCase for private fields and locals; prefix interfaces with `I` (e.g. `IUserService`).

**Nullable reference types**

- Declare variables non-nullable; validate `null` at entry points only.
- Use `is null` / `is not null` — **never** `== null` / `!= null`.
- Trust the null annotations; do not add redundant null checks the type system already rules out.

**Async** (see the `csharp-async` skill)

- Suffix async methods with `Async`; return `Task`, `Task<T>`, or `ValueTask<T>` (for hot paths).
- **Never** block with `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`.
- No `async void` except event handlers; always `await` Task-returning calls.
- Use `ConfigureAwait(false)` in library code; flow a `CancellationToken` through long-running operations.
- Parallelize with `Task.WhenAll` / `Task.WhenAny`.

**Validation & error handling**

- `try`/`catch` around `await`s; never silently swallow exceptions.
- Validate with FluentValidation or DataAnnotations; centralize with global exception middleware.
- Return errors as Problem Details (RFC 9457).

**Logging & security**

- Inject `ILogger<T>` via the constructor; use structured logging (e.g. Serilog).
- **Never log PII or secrets.**
- Prefer `DefaultAzureCredential` + Azure Key Vault / Managed Identity over secrets in code or config.

**Documentation** (see the `csharp-docs` skill)

- XML doc comments on all public APIs: `<summary>` starts with a present-tense, third-person verb; document `<param>`, `<returns>`, and `<exception>`; use `<see langword>` for keywords, `<inheritdoc/>` for overrides, and `<example>` with `<code language="csharp">`.

**Testing** (see the `csharp-xunit` skill)

- xUnit; tests live in a `[ProjectName].Tests` project; name tests `MethodName_Scenario_ExpectedBehavior`.
- Follow Arrange-Act-Assert structure but do **not** write `// Arrange` / `// Act` / `// Assert` comments.
- Data-driven tests with `[Theory]` + `[InlineData]` / `[MemberData]`; isolate with Moq or NSubstitute; run with `dotnet test`.

**Review posture**

- Make only **high-confidence** suggestions. Comment on _why_ a non-obvious design decision was made, not just what it does.

## Angular / NgRx state (always)

- Standalone components, `ChangeDetectionStrategy.OnPush`, signals for state. Assume zoneless.
- Non-trivial state belongs in an **NgRx Signal Store** (`@ngrx/signals`) — invoke the `ngrx-signal-store` skill rather than hand-rolling a `BehaviorSubject` service.
- Keep `protectedState` on so only the store's own methods write state; use `patchState` with standalone updaters.
- Reach for `rxMethod` (not `signalMethod`) whenever requests can overlap — `switchMap` is what prevents a stale response overwriting a fresh one.
- One store per entity type; use `withEntities` for keyed collections.
- This is **not** classic NgRx: no actions, reducers, or effects unless the Events plugin is a deliberate choice.
- For Angular questions that are not about state, use the `angular-developer` skill and the `angular-cli` MCP server instead of relying on memory.

## Skills

Available via the Skill tool:

- `csharp-async` — async/await best practices.
- `csharp-docs` — XML documentation conventions.
- `csharp-xunit` — xUnit unit-testing patterns.
- `angular-developer` — the official Angular team agent skill (from <https://angular.dev/ai/agent-skills>, installed from `github.com/angular/skills`). General Angular implementation guidance with progressive disclosure: `SKILL.md` routes to `references/` files for components, signals (`linkedSignal`, `resource`, effects), forms (incl. Signal Forms), DI, routing/SSR, styling, and testing. For state management, `ngrx-signal-store` remains the source of truth.
- `ngrx-signal-store` — NgRx Signal Store patterns for Angular. Uses progressive disclosure: `SKILL.md` carries the decision rules, and `references/` files (entities, async/RxJS, custom features, testing, events, recipes, API) are read only when needed. Pinned to a specific `@ngrx/signals` version and refreshed with `/ngrx-signals-sync`.

## Detailed standards — `.claude/rules/`

The full guidelines live in `.claude/rules/` and **load automatically when you edit a matching file** (path-scoped rules). You do not need to open them manually; if one is out of context, `Read` it directly.

| Area                              | Rule file                                 | Auto-applies to                                                    |
| --------------------------------- | ----------------------------------------- | ------------------------------------------------------------------ |
| General C#                        | `.claude/rules/csharp.md`                 | `**/*.cs`                                                          |
| REST / ASP.NET Core APIs          | `.claude/rules/aspnet-rest-apis.md`       | `**/*.cs`, `**/*.json`                                             |
| Azure Functions (isolated worker) | `.claude/rules/azure-functions-csharp.md` | `**/*.cs`, `**/host.json`, `**/local.settings.json`, `**/*.csproj` |
| Blazor components                 | `.claude/rules/blazor.md`                 | `**/*.razor`, `**/*.razor.cs`, `**/*.razor.css`                    |
| MCP servers in C#                 | `.claude/rules/csharp-mcp-server.md`      | `**/*.cs`, `**/*.csproj`                                           |

## MCP servers — see `@.mcp.json`

`.claude/settings.json` sets `enableAllProjectMcpServers: true`, so the servers configured in `@.mcp.json` are available. Use them when relevant:

- **`microsoft-learn`** — Ground .NET/Azure answers in official Microsoft Learn docs. Before answering a version-specific .NET or Azure question, query it (`microsoft_docs_search` → `microsoft_code_sample_search` → `microsoft_docs_fetch`) instead of relying on memory.
- **`angular-cli`** — Ground Angular answers in the installed Angular version rather than memory (`list_projects` → `get_best_practices` → `search_documentation` → `find_examples`). Use it for components, zoneless, routing, and CLI work; for state management, the `ngrx-signal-store` skill is the source of truth.
- **`terraform`** — infrastructure-as-code.

## Delegation rules

- **After implementing or modifying C# code**, delegate a quality review to the `csharp-code-reviewer` subagent (runs on Opus, with the C# skills preloaded). It reports findings; it does not edit files.
- **After implementing or modifying Angular code**, delegate a quality review to the `angular-code-reviewer` subagent (runs on Opus, with the `angular-developer` and `ngrx-signal-store` skills preloaded). It reports findings; it does not edit files.
- **When a new feature is implemented, or implementation details need documenting**, delegate to the `se-technical-writer` subagent to author or update Markdown docs under `docs/` (create the folder if it does not exist).

## Common commands

```bash
dotnet build     # compile
dotnet test      # run xUnit tests
dotnet format    # apply .editorconfig formatting
```

## Maintenance

- `/ngrx-signals-sync` — check the official NgRx Signals docs for changes and refresh the `ngrx-signal-store` skill if they drifted. Cheap and silent when nothing changed; when something did, it edits the skill and leaves the diff in the working tree for review rather than committing.
