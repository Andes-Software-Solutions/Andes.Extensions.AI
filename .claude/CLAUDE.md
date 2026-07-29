# CLAUDE.md

Project memory for **Andes.Extensions.AI** — a C#/.NET solution that ships **`Andes.Extensions.AI`** (core), **`Andes.Extensions.AI.Mcp`** (MCP satellite), **`Andes.Extensions.AI.Agent`** (Agent Framework satellite), and **`Andes.Extensions.AI.UI`** (UI status-contract satellite), NuGet packages of composable **Microsoft.Extensions.AI `IChatClient` middlewares**. It loads automatically every session and governs how Claude Code works in this repository.

## What this project is

`Andes.Extensions.AI` (PackageId `Andes.Extensions.AI`, target `net10.0`, C# 14) provides extension functionality for Microsoft.Extensions.AI and, in later phases, the Microsoft Agent Framework. The first middleware is **tool tracking** (`ToolTrackingChatClient` + `UseToolTracking()`):

- Tracks every `AIFunction` invocation made by the assistant by wrapping tools in an internal `TrackingAIFunction : DelegatingAIFunction` (request-scoped; the caller's `ChatOptions` is cloned, never mutated).
- Emits progress statuses ("Calling {Tool} Tool" headers with tool-reported subheaders like "Extracting…") **in-band** as `ChatProgressContent` items merged into the streaming response via a `Channel<ChatResponseUpdate>` pump, and **out-of-band** to `IChatProgressObserver` implementations. Tool authors report subheaders through the ambient `ChatProgress.Report(...)` API (AsyncLocal; safe no-op outside a tracked request).
- Records token usage (input/output/total, model id, provider name from `ChatClientMetadata`) per request, per model turn (streaming), and per tool-call scope — including usage reported inside tools (`ChatProgress.ReportUsage`) and totals of nested tracked pipelines (AsyncLocal ambient scope tree) — rolled up into a `ChatUsageReport` (streaming: final `UsageReportContent` update; non-streaming: `ChatResponse.AdditionalProperties["andes.ai.usage_report"]`).
- Numeric progress is first-class: `ChatProgress.Report(status, progress, progressTotal)` and `IChatProgressReporter.Report(status, progress, progressTotal)` (default interface method) populate `ChatProgressUpdate.Progress`/`ProgressTotal` (doubles, nullable).
- **Nested tool scopes**: `ChatProgress.BeginToolScope(descriptor, owner)` (returns a public `ChatProgressToolScope` handle, `Fail()`/`Dispose()`) opens a child scope on the ambient tracker so nested operations render as child activity cards and appear as child `ToolCallUsage` entries. Dedup is by scope **owner identity** (`ToolScope.IsOwnedBy` — reference equality plus the `GetService` probe chain): when the outer tracker already opened the scope for the same function, the call returns an inactive no-op. Both satellite wrappers (`AgentTrackingAIFunction`, `McpTrackingAIFunction`) call it in `InvokeCoreAsync`, so an agent/MCP tool nested inside another agent or invoked directly inside a tool body gets its own child card; a recursive self-invocation stays flat (documented limitation). CallId is taken from `FunctionInvokingChatClient.CurrentContext` only when the context's `Function` IS the owner. Static-only by design — not on `IChatProgressReporter` (captured reporters may run off-flow).
- **MCP tools ship via the satellite package `Andes.Extensions.AI.Mcp`** (references `ModelContextProtocol.Core` only — the core package must stay MCP-free): `WithTracking(...)` wraps an `McpClientTool` in the internal `McpTrackingAIFunction` (carries the server display name, bridges MCP progress notifications into `ToolProgress` updates via a per-invocation `WithProgress` + `McpProgressBridge` that captures `ChatProgress.Current` at invocation time — never ambiently at report time, since notifications arrive on the MCP receive loop), and `UseMcpToolClassification()` installs a composing `ToolClassifier` (`GetService(typeof(McpClientTool))` probe; wrapper name > resolver > default). User `DelegatingAIFunction` wrappers are never deep-unwrapped for bridging. Late notifications are dropped best-effort — no completion gate.
- **Agent-Framework-agents-as-tools ship via the satellite package `Andes.Extensions.AI.Agent`** (references `Microsoft.Agents.AI` only — core and Mcp must stay Agent-Framework-free): `WithTracking(this AIAgent, ...)` wraps `agent.AsAIFunction()` in the internal `AgentTrackingAIFunction : DelegatingAIFunction` (exposes the **original** agent via `GetService` — the framework's `AsAIFunction()` exposes neither the agent nor `AgentResponse.Usage`), and `UseAgentToolClassification()` installs a composing `ToolClassifier` (`GetService<AIAgent>()` probe; resolver > agent name > function name for `DisplayName`; `Source` = agent name, else `Id`). Usage capture (`trackUsage: true` default) is an internal `UsageReportingAIAgent : DelegatingAIAgent` that calls `ChatProgress.ReportUsage(response.Usage)` after each successful run — **ambient resolution at run time is correct here** (agents run in-process on the caller's async flow; the inverse of the MCP bridge's capture-at-invocation). Pass `trackUsage: false` when the agent's own pipeline uses `UseToolTracking()` (nested rollup would double-count). Opt-in `reportFunctionCalls: true` uses the Agent Framework's function-invocation middleware to report `"Calling {Function} Tool"` statuses (names only; local function-invoking agents only — hosted agents throw).

Key invariant: **`UseToolTracking()` must be registered before `UseFunctionInvocation()`** — the tracker wraps the tools that the `FunctionInvokingChatClient` executes and observes the merged stream from outside the invocation loop.

Privacy invariant: progress events and reports never carry prompt content, tool arguments, or tool results; the only opt-in is `ToolTrackingOptions.IncludeToolArguments` (default `false`, stringified arguments only).

## Solution layout

- `Andes.Extensions.slnx` — solution (XML format).
- `Andes.Extensions.AI\` — the core package source. Public surface at the root plus `Progress\`, `Usage\`, `Tools\`; implementation details in `Internal\` (plus the internal `TrackingAIFunction` in `Tools\`).
- `Andes.Extensions.AI.Mcp\` — the MCP satellite package (RootNamespace `Andes.Extensions.AI`, MEAI-satellite convention). Public `McpToolTrackingExtensions` + `ToolTrackingOptionsMcpExtensions` at the root; `McpTrackingAIFunction`/`McpProgressBridge` in `Internal\`.
- `Andes.Extensions.AI.Agent\` — the Agent Framework satellite package (same RootNamespace convention). Public `AgentToolTrackingExtensions` + `ToolTrackingOptionsAgentExtensions` at the root; `AgentTrackingAIFunction`/`UsageReportingAIAgent` in `Internal\`.
- `tests\Andes.Extensions.AI.Unit.Test\` — core unit tests; no network. The `Infrastructure\ScriptedChatClient` fake replays scripted `ChatResponseUpdate` turns and drives the **real** `FunctionInvokingChatClient`.
- `tests\Andes.Extensions.AI.Mcp.Unit.Test\` — MCP unit tests; no network. Links the core test infrastructure files (`<Compile Include Link>`), and `Infrastructure\InMemoryMcpFixture` hosts a **real** MCP client/server pair over in-process pipes (with a `ProgressAck` gate so progress tests are deterministic).
- `tests\Andes.Extensions.AI.Integration.Test\` — Azure OpenAI tests. Configuration comes from a **gitignored `appsettings.integration.json`** (copy `appsettings.integration.sample.json`; section `AzureOpenAI` with `Endpoint`/`ApiKey`/`Deployment`). **Never environment variables.** Tests `[SkippableFact]`-skip cleanly when the file is missing or incomplete. Do not set `Temperature` in integration tests — reasoning-model deployments reject non-default values.
- `tests\Andes.Extensions.AI.Mcp.Integration.Test\` — MCP Azure OpenAI tests; links the sibling's `AzureOpenAIFixture.cs` and its gitignored `appsettings.integration.json` (single config location), and spawns `Andes.Extensions.AI.TestMcpServer` over stdio.
- `tests\Andes.Extensions.AI.Agent.Unit.Test\` — Agent satellite unit tests; no network. Links the core test infrastructure files; inner agents are real `ChatClientAgent`s built with `scriptedChatClient.AsAIAgent(...)`.
- `tests\Andes.Extensions.AI.Agent.Integration.Test\` — Agent satellite Azure OpenAI tests; links `AzureOpenAIFixture.cs` and the shared gitignored `appsettings.integration.json`; the inner agent runs over a raw (untracked) chat client built from the fixture settings.
- `tests\Andes.Extensions.AI.TestMcpServer\` — stdio MCP console server ("Andes Test MCP": `echo`, `add`, `count_down`) used by the MCP integration tests via `ProjectReference` + `dotnet <dll>`.
- `docs\` — developer documentation (getting-started, architecture, mcp, agents, ui).
- `releases\` — per-release notes (`v{version}.md`, matching the release-tag convention); a new file is required for every version bump.
- Build infrastructure: `Directory.Build.props` (warnings as errors, C# 14, deterministic builds, XML docs required), `Directory.Packages.props` (**central package management — all versions live here**), `global.json` (SDK pin), `.editorconfig` (style rules; `CA2007` is an error in the library, off in tests via `tests\.editorconfig`).

## Project-specific conventions

- **Central package management**: never put a `Version` on a `PackageReference`; add/update pins in `Directory.Packages.props`. Use latest **stable** package versions only (no pre-release).
- The library targets **net10.0 only**; C# 14 features are welcome.
- `ConfigureAwait(false)` on every `await` in the library (enforced by CA2007-as-error); not required in tests.
- Events and logs must never carry prompt content, tool arguments, or tool results. Tool-argument capture exists only behind `ToolTrackingOptions.IncludeToolArguments` (default `false`).
- Public API changes require XML docs (missing docs fail the build) and a matching update under `docs\`.
- Packaging metadata lives in each package's csproj; `dotnet pack -c Release` must produce the nupkg + snupkg with the README embedded (root README for core, each satellite's own `README.md` for the satellites).
- The four packages version in **lockstep** (all `0.3.0` today); each satellite's `ProjectReference` to core becomes a `>= {version}` NuGet dependency automatically.

## C# coding standards (always)

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

- `try`/`catch` around `await`s; never silently swallow exceptions (documented exception: observer callbacks are isolated so a faulty observer cannot corrupt the response stream).
- Validate arguments at public entry points (`ArgumentNullException.ThrowIfNull`).
- Return errors as Problem Details (RFC 9457) in any future HTTP surface.

**Logging & security**

- Inject `ILogger<T>`/`ILoggerFactory`; use structured logging (source-generated `[LoggerMessage]` if logging is added).
- **Never log PII or secrets.**
- Prefer `DefaultAzureCredential` + Azure Key Vault / Managed Identity over secrets in code or config (integration tests use an API key from the gitignored `appsettings.integration.json` by explicit choice).

**Documentation** (see the `csharp-docs` skill)

- XML doc comments on all public APIs: `<summary>` starts with a present-tense, third-person verb; document `<param>`, `<returns>`, and `<exception>`; use `<see langword>` for keywords, `<inheritdoc/>` for overrides, and `<example>` with `<code language="csharp">`.

**Testing** (see the `csharp-xunit` skill)

- xUnit; tests live under `tests\`; name tests `MethodName_Scenario_ExpectedBehavior`.
- Follow Arrange-Act-Assert structure but do **not** write `// Arrange` / `// Act` / `// Assert` comments.
- Data-driven tests with `[Theory]` + `[InlineData]` / `[MemberData]`; isolate with hand-rolled fakes (`ScriptedChatClient`); run with `dotnet test`.
- Integration tests use `[SkippableFact]` and must skip cleanly when `appsettings.integration.json` is not configured.

**Review posture**

- Make only **high-confidence** suggestions. Comment on _why_ a non-obvious design decision was made, not just what it does.

## Skills

Available via the Skill tool:

- `csharp-async` — async/await best practices.
- `csharp-docs` — XML documentation conventions.
- `csharp-xunit` — xUnit unit-testing patterns.
- `microsoft-agent-framework` — Microsoft Agent Framework guidance (agents as tools, sessions, middleware).

## Detailed standards — `.claude/rules/`

The full guidelines live in `.claude/rules/` and **load automatically when you edit a matching file** (path-scoped rules). If one is out of context, `Read` it directly.

| Area                              | Rule file                                 | Auto-applies to                                                     |
| --------------------------------- | ----------------------------------------- | ------------------------------------------------------------------- |
| General C#                        | `.claude/rules/csharp.md`                 | `**/*.cs`                                                           |
| REST / ASP.NET Core APIs          | `.claude/rules/aspnet-rest-apis.md`       | `**/*.cs`, `**/*.json`                                              |
| Azure Functions (isolated worker) | `.claude/rules/azure-functions-csharp.md` | `**/*.cs`, `**/host.json`, `**/local.settings.json`, `**/*.csproj`  |
| Blazor components                 | `.claude/rules/blazor.md`                 | `**/*.razor`, `**/*.razor.cs`, `**/*.razor.css`                     |
| MCP servers in C#                 | `.claude/rules/csharp-mcp-server.md`      | `**/*.cs`, `**/*.csproj`                                            |

## MCP servers — see `@.mcp.json`

`.claude/settings.json` sets `enableAllProjectMcpServers: true`, so the servers configured in `@.mcp.json` are available. Use them when relevant:

- **`microsoft-learn`** — Ground .NET/Azure answers in official Microsoft Learn docs. Before answering a version-specific .NET, Microsoft.Extensions.AI, Agent Framework, or Azure question, query it (`microsoft_docs_search` → `microsoft_code_sample_search` → `microsoft_docs_fetch`) instead of relying on memory.
- **`terraform`** — infrastructure-as-code, if deployment automation is added.

## Delegation rules

- **After implementing or modifying C# code**, delegate a quality review to the `csharp-code-reviewer` subagent. It reports findings; it does not edit files.
- **After creating or modifying GitHub Actions workflow files** (`.github/workflows/*.yml` or composite actions), delegate a review to the `github-actions-reviewer` subagent. It reports findings; it does not edit files.
- **When a new feature is implemented, or implementation details need documenting**, delegate to the `se-technical-writer` subagent to author or update Markdown docs under `docs/`.
- **On every release change** (a version bump in the package csprojs or a new release tag), delegate to the `se-technical-writer` subagent to author `releases/v{version}.md` documenting what was added, changed, and fixed relative to the previous release. Every claim must trace to git history or current sources — never invent dates or features.

## Common commands

```bash
dotnet build                                    # compile (net10.0, warnings are errors)
dotnet test                                     # unit tests + integration tests (integration auto-skips)
dotnet pack Andes.Extensions.AI -c Release      # produce core nupkg + snupkg
dotnet pack Andes.Extensions.AI.Mcp -c Release  # produce MCP satellite nupkg + snupkg
dotnet pack Andes.Extensions.AI.Agent -c Release # produce Agent satellite nupkg + snupkg
dotnet format                                   # apply .editorconfig formatting
```

Integration tests run for real only when `tests\Andes.Extensions.AI.Integration.Test\appsettings.integration.json` exists with the `AzureOpenAI` section filled in (copy the `.sample` file). Do not use environment variables for this.
