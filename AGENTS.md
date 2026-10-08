<!-- andes:begin v1.5.0 -->
# Andes engineering standards

Shared by Claude Code and GitHub Copilot. The `andes-init` skill manages this block and replaces it on refresh — put project-specific instructions after the `andes:end` marker (`andes-init` scaffolds those sections on first install).

## Communication & comments

- Lead with the answer or the change; no preamble, no restating the request. Don't re-summarize what the user already saw — report only what changed or went wrong. Match length to substance.
- Comment only what code cannot say: why a decision was made, constraints, non-obvious invariants, workarounds with links. Never narrate what code does or what changed. XML doc comments on public APIs are API documentation, not comments.

## Load the standards before you edit

Detailed standards live in skills that load on demand. Load the matching skill before writing or reviewing code:

| Working on | Load |
| --- | --- |
| Any `*.cs` | `csharp-standards`, plus `aspnet-rest-apis` (web APIs), `azure-functions-csharp` (Functions), `csharp-mcp-server` (MCP servers), `ef-core` (EF Core; `ef-core-base-entities` / `ef-core-enum-reference-tables` for entities and lookup tables), `csharp-async`, `csharp-docs` (public APIs) as the change needs; `dotnet-api-architecture` when adding, moving, renaming, or registering files, folders, or projects. Non-negotiables: Minimal APIs only (no controllers), FluentValidation only (no DataAnnotations), services query the `DbContext` directly (no repositories), primary constructors, collection expressions, `var`, and the `csharp-standards` file layout |
| .NET tests | `csharp-xunit` — xUnit v3 + NSubstitute only; never FluentAssertions, Shouldly, Moq, NUnit, or MSTest. Test databases: Testcontainers → SQLite in-memory → dedicated test database → EF Core InMemory as a last resort |
| `*.razor`, `*.razor.cs` | `blazor-wasm` |
| Angular code | `angular-standards`; `ngrx-signal-store` for any state; `angular-ui-architecture` when adding, moving, or naming files or wiring layer lint; `angular-developer` references for depth |
| `*.tf` | `terraform-conventions` |
| `.github/workflows/*.yml`, `action.yml` | `github-actions-hardening`, plus `github-actions-efficiency` / `github-actions-runtime-upgrade-conventions` when relevant |
| Microsoft Agent Framework | `microsoft-agent-framework` |

Ground version-specific answers in the MCP servers when they are installed — `microsoft-learn` (.NET, Azure, other Microsoft docs; ships with `andes-core`), `angular-cli` (Angular), `context7` (any other library; ships with `andes-core`), `terraform` (providers, modules) — instead of memory.

## Review loop

After changing code, run the matching reviewer on the diff: `andes-csharp-code-reviewer` (C#, including Blazor), `andes-angular-code-reviewer` (Angular), `andes-github-actions-reviewer` (workflows, composite actions). Terraform has no reviewer — run `terraform fmt -check` and `terraform validate` instead.

1. Reviewers report only High and Medium findings plus a verdict. They never edit files or hand work back.
2. The implementer fixes every reported finding, then runs the reviewer once more on only the files changed since round 1.
3. Two rounds maximum. If High findings remain after round 2, stop and report them to the user instead of iterating; list any open Medium findings in the final summary.
4. After a passing verdict (**Approve** or **Approve with changes**), invoke `andes-se-technical-writer` to update `docs/` and add the `CHANGELOG.md` entry — unless your caller said it handles documentation.

## Docs, changelog & requirements

- `andes-se-technical-writer` owns `docs/` and the root `CHANGELOG.md` ([Keep a Changelog](https://keepachangelog.com/en/1.1.0/)): one reader-facing entry per PR under `## [Unreleased]` in the matching subsection. Routine cleanups with no behavior change still get a one-line entry.
- To write a PRD, spec a feature, or break it into epics and user stories, delegate to `andes-prd-generator` (writes `docs/prd/`). If its report starts `PRD-STATUS: NEEDS-INPUT`, show its questions to the user verbatim and re-invoke it with the answers. It creates GitHub issues only after the user explicitly approves. PRDs and the implementation plans under `docs/plans/` get no changelog entry; plans reference story IDs (`US-xxx`). Plans are working files for following the implementation: never commit `docs/plans/`; keep it in `.gitignore`.
- When `andes-azure-devops` is installed, delegate creating, updating, or removing Azure DevOps epics, features, and stories (including from a PRD) to `andes-ado-backlog-manager`. It previews every change and always asks who to assign and which iteration; if its report starts `ADO-STATUS: NEEDS-INPUT` or `ADO-STATUS: NEEDS-SETUP`, show it to the user verbatim and re-invoke it with the answers and the user's explicit approval.
<!-- andes:end -->

## About this repository

**Andes.Extensions.AI** is a C#/.NET solution that ships four NuGet packages of composable **Microsoft.Extensions.AI `IChatClient` middlewares**: **`Andes.Extensions.AI`** (core), **`Andes.Extensions.AI.Mcp`** (MCP satellite), **`Andes.Extensions.AI.Agent`** (Agent Framework satellite), and **`Andes.Extensions.AI.UI`** (UI status-contract satellite). All target `net10.0` with C# 14.

The core middleware is **tool tracking** (`ToolTrackingChatClient` + `UseToolTracking()`):

- Tracks every `AIFunction` invocation made by the assistant by wrapping tools in an internal `TrackingAIFunction : DelegatingAIFunction` (request-scoped; the caller's `ChatOptions` is cloned, never mutated).
- Emits progress statuses ("Calling {Tool} Tool" headers with tool-reported subheaders like "Extracting…") **in-band** as `ChatProgressContent` items merged into the streaming response via a `Channel<ChatResponseUpdate>` pump, and **out-of-band** to `IChatProgressObserver` implementations. Tool authors report subheaders through the ambient `ChatProgress.Report(...)` API (AsyncLocal; safe no-op outside a tracked request).
- **Request-level statuses are detection-driven (v0.5)**: the middleware never auto-emits a request-start or synthetic "Thinking" status — the first in-band event of a tool request is `ToolInvoking`. `ChatProgressKind.Reasoning` (renamed from `Thinking` in 0.5.0) is emitted **once per model turn** when `TextReasoningContent` is detected in the stream (OpenAI Responses API today; content-based, so any provider surfacing it works), re-armed by `AdvanceIteration()` after each tool round-trip, mirrored post-hoc (once per request, observers-only) for non-streaming responses, and never carries reasoning text; it has **no public factory** — apps can never emit it. A matching `ChatProgressKind.ReasoningCompleted` (value 8, message "Reasoning completed") closes each detected turn at most once — raised when the first answer text or function call follows the reasoning, or at stream end (in-band, before the trailing `RequestCompleted`), with the elapsed reasoning time in `ChatProgressUpdate.Duration` (streaming only; `null` on the post-hoc mirror, where observers get a balanced pair) and never the reasoning text. `ChatProgressKind.Custom` (renamed from `RequestStarted` in 0.5.0; numeric value 0 preserved) is the developer-constructed status the middleware never emits: built via the sole public factory `ChatProgressUpdate.CreateCustom(message)` (message **required** — `ArgumentException.ThrowIfNullOrEmpty`; stamped `ChatProgressUpdate.ExternalScopeId`, depth 0, current UTC time) and wrapped with `ToResponseUpdate()` into the same synthetic shape the middleware writes. The UI package surfaces the reasoning summary *text* separately: `AssistantUiEventKind.ReasoningDelta` events (one per non-empty in-band `TextReasoningContent`; encrypted-only items skipped) accumulate into `AssistantStatusSnapshot.ReasoningText` (verbatim across tool round-trips; TypeScript mirror updated) — sourced only from in-band model content, never from progress metadata.
- Records token usage (input/output/total, model id, provider name from `ChatClientMetadata`) per request, per model turn (streaming), and per tool-call scope — including usage reported inside tools (`ChatProgress.ReportUsage`) and totals of nested tracked pipelines (AsyncLocal ambient scope tree) — rolled up into a `ChatUsageReport` (streaming: final `UsageReportContent` update; non-streaming: `ChatResponse.AdditionalProperties["andes.ai.usage_report"]`). Since 0.6.0, `ToolCallUsage.Iteration` (`required int`, stamped at scope open from the tracker's turn counter) correlates each tool call with the issuing `AssistantTurnUsage.Iteration` for per-tool prompt-cost attribution; nested children report the outer issuing turn's iteration, and the non-streaming path always stamps 0 (matching `Turns` being streaming-only).
- Numeric progress is first-class: `ChatProgress.Report(status, progress, progressTotal)` and `IChatProgressReporter.Report(status, progress, progressTotal)` (default interface method) populate `ChatProgressUpdate.Progress`/`ProgressTotal` (doubles, nullable).
- **Nested tool scopes**: `ChatProgress.BeginToolScope(descriptor, owner)` (returns a public `ChatProgressToolScope` handle, `Fail()`/`Dispose()`) opens a child scope on the ambient tracker so nested operations render as child activity cards and appear as child `ToolCallUsage` entries. Dedup is by scope **owner identity** (`ToolScope.IsOwnedBy` — reference equality plus the `GetService` probe chain): when the outer tracker already opened the scope for the same function, the call returns an inactive no-op. Both satellite wrappers (`AgentTrackingAIFunction`, `McpTrackingAIFunction`) call it in `InvokeCoreAsync`, so an agent/MCP tool nested inside another agent or invoked directly inside a tool body gets its own child card; a recursive self-invocation stays flat (documented limitation). CallId is taken from `FunctionInvokingChatClient.CurrentContext` only when the context's `Function` IS the owner. Static-only by design — not on `IChatProgressReporter` (captured reporters may run off-flow).
- **MCP tools ship via the satellite package `Andes.Extensions.AI.Mcp`** (references `ModelContextProtocol.Core` only — the core package must stay MCP-free): `WithTracking(...)` wraps an `McpClientTool` in the internal `McpTrackingAIFunction` (carries the server display name, bridges MCP progress notifications into `ToolProgress` updates via a per-invocation `WithProgress` + `McpProgressBridge` that captures `ChatProgress.Current` at invocation time — never ambiently at report time, since notifications arrive on the MCP receive loop), and `UseMcpToolClassification()` installs a composing `ToolClassifier` (`GetService(typeof(McpClientTool))` probe; wrapper name > resolver > default). User `DelegatingAIFunction` wrappers are never deep-unwrapped for bridging. Late notifications are dropped best-effort — no completion gate.
- **Agent-Framework-agents-as-tools ship via the satellite package `Andes.Extensions.AI.Agent`** (references `Microsoft.Agents.AI` only — core and Mcp must stay Agent-Framework-free): `WithTracking(this AIAgent, ...)` wraps `agent.AsAIFunction()` in the internal `AgentTrackingAIFunction : DelegatingAIFunction` (exposes the **original** agent via `GetService` — the framework's `AsAIFunction()` exposes neither the agent nor `AgentResponse.Usage`), and `UseAgentToolClassification()` installs a composing `ToolClassifier` (`GetService<AIAgent>()` probe; resolver > agent name > function name for `DisplayName`; `Source` = agent name, else `Id`). Usage capture (`trackUsage: true` default) is an internal `UsageReportingAIAgent : DelegatingAIAgent` that calls `ChatProgress.ReportUsage(response.Usage)` after each successful run — **ambient resolution at run time is correct here** (agents run in-process on the caller's async flow; the inverse of the MCP bridge's capture-at-invocation). Pass `trackUsage: false` when the agent's own pipeline uses `UseToolTracking()` (nested rollup would double-count). Opt-in `reportFunctionCalls: true` uses the Agent Framework's function-invocation middleware to report `"Calling {Function} Tool"` statuses (names only; local function-invoking agents only — hosted agents throw).
- **The UI status contract ships via the satellite package `Andes.Extensions.AI.UI`** (references core and `Microsoft.Extensions.AI.Abstractions` only): `ToUiEventsAsync()`/`ToStatusSnapshotsAsync()` project the in-band `ChatProgressContent`/`UsageReportContent` stream into flat `AssistantUiEvent` deltas, and `AssistantStatusReducer` folds them into an `AssistantStatusSnapshot` (the status line plus a hierarchy of `AssistantActivity` cards with sub-statuses, nested children, and token usage). `AssistantUiJsonContext` is the source-generated JSON context. The TypeScript mirror `typescript/andes-assistant-ui.ts` (`foldAssistantEvents`) ships in the package; change it together with the C# contract.

Key invariant: **`UseToolTracking()` must be registered before `UseFunctionInvocation()`** — the tracker wraps the tools that the `FunctionInvokingChatClient` executes and observes the merged stream from outside the invocation loop.

Privacy invariant: progress events and reports never carry prompt content, tool arguments, or tool results; the only opt-in is `ToolTrackingOptions.IncludeToolArguments` (default `false`, stringified arguments only).

## Layout

- `Andes.Extensions.slnx` — solution (XML format).
- `Andes.Extensions.AI\` — the core package source. Public surface at the root plus `Progress\`, `Usage\`, `Tools\`; implementation details in `Internal\` (plus the internal `TrackingAIFunction` in `Tools\`).
- `Andes.Extensions.AI.Mcp\` — the MCP satellite package (RootNamespace `Andes.Extensions.AI`, MEAI-satellite convention). Public `McpToolTrackingExtensions` + `ToolTrackingOptionsMcpExtensions` at the root; `McpTrackingAIFunction`/`McpProgressBridge` in `Internal\`.
- `Andes.Extensions.AI.Agent\` — the Agent Framework satellite package (same RootNamespace convention). Public `AgentToolTrackingExtensions` + `ToolTrackingOptionsAgentExtensions` at the root; `AgentTrackingAIFunction`/`UsageReportingAIAgent` in `Internal\`.
- `Andes.Extensions.AI.UI\` — the UI satellite package (same RootNamespace convention). Contract records, `ChatResponseUiExtensions`, `AssistantStatusReducer`, and `AssistantUiJsonContext` at the root; the TypeScript mirror in `typescript\`.
- `tests\Andes.Extensions.AI.Unit.Test\` — core unit tests; no network. The `Infrastructure\ScriptedChatClient` fake replays scripted `ChatResponseUpdate` turns and drives the **real** `FunctionInvokingChatClient`.
- `tests\Andes.Extensions.AI.Mcp.Unit.Test\` — MCP unit tests; no network. Links the core test infrastructure files (`<Compile Include Link>`), and `Infrastructure\InMemoryMcpFixture` hosts a **real** MCP client/server pair over in-process pipes (with a `ProgressAck` gate so progress tests are deterministic).
- `tests\Andes.Extensions.AI.Agent.Unit.Test\` — Agent satellite unit tests; no network. Links the core test infrastructure files; inner agents are real `ChatClientAgent`s built with `scriptedChatClient.AsAIAgent(...)`.
- `tests\Andes.Extensions.AI.UI.Unit.Test\` — UI satellite unit tests; no network. Links the core test infrastructure files.
- `tests\Andes.Extensions.AI.Integration.Test\` — Azure OpenAI tests. Configuration comes from a **gitignored `appsettings.integration.json`** (copy `appsettings.integration.sample.json`; section `AzureOpenAI` with `Endpoint`/`ApiKey`/`Deployment`, plus optional `ResponsesDeployment` — a reasoning-capable deployment that gates the Responses API tests, which otherwise skip). **Never environment variables.** Tests `[SkippableFact]`-skip cleanly when the file is missing or incomplete. Do not set `Temperature` in integration tests — reasoning-model deployments reject non-default values.
- `tests\Andes.Extensions.AI.Mcp.Integration.Test\` — MCP Azure OpenAI tests; links the sibling's `AzureOpenAIFixture.cs` and its gitignored `appsettings.integration.json` (single config location), and spawns `Andes.Extensions.AI.TestMcpServer` over stdio.
- `tests\Andes.Extensions.AI.Agent.Integration.Test\` — Agent satellite Azure OpenAI tests; links `AzureOpenAIFixture.cs` and the shared gitignored `appsettings.integration.json`; the inner agent runs over a raw (untracked) chat client built from the fixture settings.
- `tests\Andes.Extensions.AI.TestMcpServer\` — stdio MCP console server ("Andes Test MCP": `echo`, `add`, `count_down`) used by the MCP integration tests via `ProjectReference` + `dotnet <dll>`.
- `samples\Andes.Extensions.AI.Demo\` — interactive Spectre.Console chat exercising all four packages over Azure OpenAI Chat Completions (gitignored `appsettings.json`, copy the sample file; `samples\Directory.Build.props` makes samples non-packable).
- `samples\Andes.Extensions.AI.Demo.Responses\` — sibling demo over the **Azure OpenAI Responses API** with stable packages only: plain `OpenAIClient` against the OpenAI-v1-compatible endpoint (`{endpoint}/openai/v1`), `GetResponsesClient().AsIChatClient(deployment)`, `ChatOptions.Reasoning` with `Output = ReasoningOutput.Full` (**not** `Summary` — M.E.AI.OpenAI 10.8.3 maps it to summary verbosity "concise", which gpt-5-series deployments reject; `Full` maps to "detailed"); needs a reasoning-capable deployment and `NoWarn OPENAI001` (the Responses surface is still `[Experimental]` in OpenAI 2.13). The demo renders a dim live tail of `AssistantStatusSnapshot.ReasoningText` under the status header.
- `docs\` — developer documentation (getting-started, architecture, mcp, agents, ui) plus worked examples in `docs\examples\`.
- `releases\` — per-release notes (`v{version}.md`, matching the release-tag convention); a new file is required for every version bump.
- Build infrastructure: `Directory.Build.props` (warnings as errors, C# 14, deterministic builds, XML docs required), `Directory.Packages.props` (**central package management — all versions live here**), `global.json` (SDK pin), `.editorconfig` (style rules; `CA2007` is an error in the library, off in tests via `tests\.editorconfig`), `tests\Directory.Build.props` (test projects are non-packable and exempt from XML docs).

## Build, test, run

```bash
dotnet build                                     # compile (net10.0, warnings are errors)
dotnet test                                      # unit tests + integration tests (integration auto-skips)
dotnet pack Andes.Extensions.AI -c Release       # produce core nupkg + snupkg
dotnet pack Andes.Extensions.AI.Mcp -c Release   # produce MCP satellite nupkg + snupkg
dotnet pack Andes.Extensions.AI.Agent -c Release # produce Agent satellite nupkg + snupkg
dotnet pack Andes.Extensions.AI.UI -c Release    # produce UI satellite nupkg + snupkg
dotnet format                                    # apply .editorconfig formatting
dotnet run --project samples/Andes.Extensions.AI.Demo           # Chat Completions demo
dotnet run --project samples/Andes.Extensions.AI.Demo.Responses # Responses API demo (reasoning-capable deployment)
```

Integration tests run for real only when `tests\Andes.Extensions.AI.Integration.Test\appsettings.integration.json` exists with the `AzureOpenAI` section filled in (copy the `.sample` file). Do not use environment variables for this. The demos read a gitignored `appsettings.json` next to their `Program.cs` (copy `appsettings.sample.json`).

## Conventions

- **Central package management**: never put a `Version` on a `PackageReference`; add/update pins in `Directory.Packages.props`. Use latest **stable** package versions only (no pre-release).
- The library targets **net10.0 only**; C# 14 features are welcome. Honor `.editorconfig` — it enforces the naming rules and `CA2007` as errors.
- `ConfigureAwait(false)` on every `await` in the library (enforced by CA2007-as-error); not required in tests.
- Events and logs must never carry prompt content, tool arguments, or tool results. Tool-argument capture exists only behind `ToolTrackingOptions.IncludeToolArguments` (default `false`).
- Observer callbacks are isolated so a faulty `IChatProgressObserver` cannot corrupt the response stream — the one documented case where an exception is caught and not rethrown.
- Public API changes require XML docs (missing docs fail the build) and a matching update under `docs\`.
- Packaging metadata lives in each package's csproj; `dotnet pack -c Release` must produce the nupkg + snupkg with the README embedded (root README for core, each satellite's own `README.md` for the satellites).
- The four packages version in **lockstep** (all `0.10.0` today); each satellite's `ProjectReference` to core becomes a `>= {version}` NuGet dependency automatically.
- **Release notes**: every version bump (package csprojs or a new release tag) needs `releases/v{version}.md`, authored by `andes-se-technical-writer`, documenting what was added, changed, and fixed relative to the previous release. Every claim must trace to git history or current sources — never invent dates or features.
- Integration tests authenticate with an API key from the gitignored `appsettings.integration.json` by explicit choice, not `DefaultAzureCredential`.
- Microsoft.Extensions.AI and Agent Framework APIs move quickly: check `microsoft-learn`, and the dotnet/extensions sources through `context7`, before relying on memory.

### Where this repository differs from the Andes block

- This is a class library with no HTTP surface or database: the Minimal API, FluentValidation, `DbContext`, and Problem Details rules don't apply.
- Tests run on xUnit 2.9.3 with `Xunit.SkippableFact`; the move to xUnit v3 is its own planned change, so don't fold it into unrelated work. Isolate with the existing hand-rolled fakes (`ScriptedChatClient`, `InMemoryMcpFixture`) — the test projects reference no mocking library. Integration tests use `[SkippableFact]` and must skip cleanly when `appsettings.integration.json` is not configured.
