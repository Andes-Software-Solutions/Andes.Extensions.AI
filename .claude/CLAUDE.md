# CLAUDE.md

Project memory for **Enterprise.AI** — a C#/.NET solution that ships **`Enterprise.AI.Middleware`**, a NuGet package of composable **Microsoft.Extensions.AI `IChatClient` middlewares**. It loads automatically every session and governs how Claude Code works in this repository.

## What this project is

`Enterprise.AI.Middleware` (PackageId `Enterprise.AI.Middleware`, multi-target `net8.0;net10.0`) provides production middlewares for `IChatClient` pipelines. The first middleware is **tool tracking** (`ToolTrackingChatClient` + `UseToolTracking()`):

- Tracks every tool invocation made by the assistant — plain `AIFunction` tools, **Microsoft Agent Framework agents exposed as tools** (`agent.AsTrackedAIFunction()`), and **MCP tools** (`McpClientTool`, annotated via `WithTrackingMetadata`).
- Emits hierarchical status updates ("Calling Tool(s)" / "Calling {Agent} Agent" / "Calling {Server} MCP" with subheaders) **in-band** as `ActivityStatusContent` items in the streaming response and **out-of-band** to `IChatActivityObserver` implementations.
- Records granular token usage (input/output/total, model id, provider name from `ChatClientMetadata`) per request and per tool-call scope, including LLM calls nested inside agent tools (AsyncLocal ambient scope tree), rolled up into a `ChatActivityReport`.

Key invariant: **`UseToolTracking()` must be registered before `UseFunctionInvocation()`** — the tracker wraps the tools that the `FunctionInvokingChatClient` executes.

## Solution layout

- `Enterprise.AI.slnx` — solution (XML format).
- `Enterprise.AI.Middleware\` — the package source. Public surface in `Tracking\`; implementation details in `Tracking\Internal\` (visible to the unit tests via `InternalsVisibleTo`).
- `tests\Enterprise.AI.Middleware.Tests\` — unit tests; no network. A `ScriptedChatClient` fake drives the real `FunctionInvokingChatClient`, and an in-process MCP client/server pair (stream transport) supplies genuine `McpClientTool` instances.
- `tests\Enterprise.AI.Middleware.IntegrationTests\` — Azure OpenAI tests, auto-skip unless `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_API_KEY`, and `AZURE_OPENAI_DEPLOYMENT` are set.
- `tests\Enterprise.AI.Middleware.TestMcpServer\` — stdio MCP server used by the integration tests (no Node.js dependency).
- `docs\` — developer documentation (getting-started, architecture, status-events, usage-tracking, configuration).
- Build infrastructure: `Directory.Build.props` (warnings as errors, C# 14, deterministic builds), `Directory.Packages.props` (**central package management — all versions live here**), `global.json` (SDK pin), `.editorconfig` (style rules; `CA2007` is an error in the library, off in tests).

## Project-specific conventions

- **Central package management**: never put a `Version` on a `PackageReference`; add/update pins in `Directory.Packages.props`.
- The library must compile on **both** `net8.0` and `net10.0` — avoid net10-only APIs (C# 14 language features are fine).
- `ConfigureAwait(false)` on every `await` in the library (enforced by CA2007-as-error); not required in tests.
- Events and logs must never carry prompt content, tool arguments, or tool results. Tool-argument logging exists only behind `ToolTrackingOptions.ArgumentLogging` (default `None`).
- Public API changes require XML docs (missing docs fail the build) and a matching update under `docs\`.
- Packaging metadata lives in `Enterprise.AI.Middleware.csproj`; `dotnet pack -c Release` must produce the nupkg + snupkg with the README embedded.

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

- `try`/`catch` around `await`s; never silently swallow exceptions.
- Validate arguments at public entry points (`ArgumentNullException.ThrowIfNull`).
- Return errors as Problem Details (RFC 9457) in any future HTTP surface.

**Logging & security**

- Inject `ILogger<T>`/`ILoggerFactory`; use structured logging (source-generated `[LoggerMessage]` in this repo).
- **Never log PII or secrets.**
- Prefer `DefaultAzureCredential` + Azure Key Vault / Managed Identity over secrets in code or config (integration tests use an API key from environment variables by explicit choice).

**Documentation** (see the `csharp-docs` skill)

- XML doc comments on all public APIs: `<summary>` starts with a present-tense, third-person verb; document `<param>`, `<returns>`, and `<exception>`; use `<see langword>` for keywords, `<inheritdoc/>` for overrides, and `<example>` with `<code language="csharp">`.

**Testing** (see the `csharp-xunit` skill)

- xUnit; tests live under `tests\`; name tests `MethodName_Scenario_ExpectedBehavior`.
- Follow Arrange-Act-Assert structure but do **not** write `// Arrange` / `// Act` / `// Assert` comments.
- Data-driven tests with `[Theory]` + `[InlineData]` / `[MemberData]`; isolate with NSubstitute or hand-rolled fakes (`ScriptedChatClient`); run with `dotnet test`.
- Integration tests use `[SkippableFact]` and must skip cleanly when the environment is not configured.

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
- **When a new feature is implemented, or implementation details need documenting**, delegate to the `se-technical-writer` subagent to author or update Markdown docs under `docs/`.

## Common commands

```bash
dotnet build                                  # compile (net8.0 + net10.0, warnings are errors)
dotnet test                                   # unit tests + integration tests (integration auto-skips)
dotnet pack Enterprise.AI.Middleware -c Release   # produce nupkg + snupkg
dotnet format                                 # apply .editorconfig formatting
```

Integration tests run for real only when `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_API_KEY`, and `AZURE_OPENAI_DEPLOYMENT` are set.
