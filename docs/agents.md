# Agent Tool Tracking

`Andes.Extensions.AI.Agent` adds [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/) support to the core [`Andes.Extensions.AI`](getting-started.md) tool-tracking middleware. The framework already lets any `AIAgent` act as a tool — [`AsAIFunction()`](https://learn.microsoft.com/dotnet/api/microsoft.agents.ai.aiagentextensions.asaifunction) turns an agent into an `AIFunction` another model can call — but that function is a plain product of `AIFunctionFactory.Create`: it exposes neither the agent behind it (nothing to discover via `GetService`) nor the run's `AgentResponse.Usage` (it returns only the response text). Without this package, an agent-as-tool classifies as an ordinary `ToolKind.Function` and reports zero token usage. This satellite package supplies both:

1. **Classification** — agents wrapped with `WithTracking()` are recognized as `ToolKind.Agent` and render with the core's `"Calling {Agent} Agent"` header in progress events and usage reports (the header and the `ToolKind.Agent` value have existed in core since v0.2, reserved for exactly this). The trailing `Agent` is dropped when the agent's name already ends with it, so `"Research Agent"` renders as `"Calling Research Agent"`, not `"Calling Research Agent Agent"`.
2. **Usage capture** — each run's `AgentResponse.Usage` is attributed to the calling tool's scope, so the agent's token consumption lands in the `ChatUsageReport` even when its pipeline cannot be instrumented.

A third capability is opt-in: reporting the agent's own function calls as progress statuses — see [Seeing the agent's own function calls](#seeing-the-agents-own-function-calls).

This guide covers installation, how classification and usage capture work, the double-count interaction with nested tracked pipelines, [nested agents](#nested-agents) (agents inside agents or inside tool bodies, rendering as child activities since v0.3), and the package's deliberate limits. For the core middleware's design, see [Architecture](architecture.md); for core usage, see [Getting started](getting-started.md).

## Prerequisites and installation

- .NET SDK **10.0** or later (the package targets `net10.0`).
- The core pipeline from [Getting started](getting-started.md) — `UseToolTracking()` before `UseFunctionInvocation()`.

```shell
dotnet add package Andes.Extensions.AI.Agent
```

Installing the package brings in the core `Andes.Extensions.AI` package (>= 0.3.0) and [`Microsoft.Agents.AI`](https://www.nuget.org/packages/Microsoft.Agents.AI) (>= 1.15.0, stable).

## Quickstart

Two calls on top of the core pipeline: `UseAgentToolClassification()` on the options, and `WithTracking()` on the agent:

```csharp
using Andes.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

AIAgent weatherAgent = weatherChatClient.AsAIAgent(
    instructions: "You answer questions about the weather.",
    name: "Weather Agent",
    description: "Answers questions about the weather.",
    tools: [AIFunctionFactory.Create(GetWeather)]);

IChatClient client = innerClient
    .AsBuilder()
    .UseToolTracking(options => options.UseAgentToolClassification())
    .UseFunctionInvocation() // tracking before function invocation (core invariant)
    .Build();

var chatOptions = new ChatOptions { Tools = [weatherAgent.WithTracking()] };
```

A typical rendering while the outer model delegates to the agent:

```text
[RequestStarted] Starting request
[Thinking] Thinking...
  [ToolInvoking] Calling Weather Agent
    [ToolProgress] Calling GetWeather Tool
    [ToolProgress] Extracting...
  [ToolCompleted] Weather_Agent completed
[Thinking] Thinking...
[RequestCompleted] Request completed
```

The `Calling GetWeather Tool` line appears only with `reportFunctionCalls: true`; the `Extracting...` line is a status the agent's own tool reported through `ChatProgress.Report(...)` and needs no configuration at all — see [Seeing the agent's own function calls](#seeing-the-agents-own-function-calls). Note `Weather_Agent` in the completion event: the framework derives the function name from the agent's name by collapsing runs of non-alphanumeric characters to `_`, and completion events carry the tool `Name` while headers carry the `DisplayName`.

**Name your agents.** The function name, the display-name fallback, and the usage report's `Source` all derive from `AIAgent.Name`; an unnamed agent falls back to `AIFunctionFactory`'s default function name and to `agent.Id` as `Source`, unless `functionOptions` supplies metadata.

`WithTracking` accepts the same `functionOptions` and `session` parameters as the framework's `AsAIFunction` (forwarded verbatim — including the framework's caveat that a session-bound function must not be invoked concurrently), plus two tracking parameters: `trackUsage` (default `true`) and `reportFunctionCalls` (default `false`).

## How classification works

`UseAgentToolClassification()` installs a `ToolTrackingOptions.ToolClassifier` that recognizes agent tools by probing `AIFunction.GetService<AIAgent>()`. The probe is answered by the `WithTracking` wrapper, which exposes the **original** agent — never an internal decorator, so probing for concrete types such as `ChatClientAgent` works too — and traverses `DelegatingAIFunction` chains, so your own delegating wrapper around a tracked agent still classifies. A classified tool renders with the core's default `"Calling {DisplayName} Agent"` header and appears in usage reports with `Kind = ToolKind.Agent` and `Source` set to the agent's name, or its `Id` for unnamed agents (reports carry `Source`, not `DisplayName`).

The header display name is resolved in precedence order:

| Precedence | Display name comes from | Applies when |
| --- | --- | --- |
| 1 | The `displayNameResolver` callback, when it returns non-whitespace | A resolver was passed to `UseAgentToolClassification` |
| 2 | `AIAgent.Name` | The agent has a name |
| 3 | The function name | Unnamed agents |

Unlike the MCP satellite, there is no `defaultServerName`-style fallback parameter. MCP needs one because a raw `McpClientTool` never knows its server — but every tool this classifier can recognize is a `WithTracking` wrapper that carries its agent, so the agent identity is always at hand.

Non-agent tools are delegated to whatever classifier was configured **before** `UseAgentToolClassification` was called, or to `ToolDescriptor.CreateDefault` when there is none — the same composition contract as [`UseMcpToolClassification`](mcp.md#how-classification-works), so the two satellites stack in either order:

```csharp
.UseToolTracking(options =>
{
    options.UseMcpToolClassification();
    options.UseAgentToolClassification(); // agents short-circuit; MCP tools fall through to the MCP classifier
})
```

## How usage capture works

With `trackUsage: true` (the default), `WithTracking` decorates the agent with an internal `DelegatingAIAgent` that reports `AgentResponse.Usage` through `ChatProgress.ReportUsage(...)` after each successful run, attributing it to the calling tool's scope in the `ChatUsageReport`.

The report resolves the ambient scope **at run time** — the exact inverse of the [MCP progress bridge's capture-at-invocation design](mcp.md#how-the-progress-bridge-works), and both are correct for the same underlying reason:

| Satellite | Where the callback runs | Ambient `AsyncLocal` scope present? | Design |
| --- | --- | --- | --- |
| MCP (`McpProgressBridge`) | The MCP client's receive loop | No | Capture the reporter at invocation time |
| Agent (`UsageReportingAIAgent`) | In-process, on the caller's async flow | Yes | Resolve the ambient scope at run time |

The agent runs inside the tracked invocation, so the scope the core middleware pushes around the tool call is present throughout the run and `ChatProgress.ReportUsage` finds it naturally. Capturing at decoration time would find nothing — the decorator is built at startup, where no request scope exists.

The boundaries of usage capture:

- **Faulted runs report nothing**; the exception propagates untouched.
- **Outside a tracked pipeline the report is a safe no-op** — a wrapped agent behaves normally when invoked directly, in unit tests, or in an untracked pipeline.
- **The report is whatever the agent surfaces.** An agent implementation that leaves `AgentResponse.Usage` empty attributes nothing.
- **Streaming runs are unreachable today** — `AsAIFunction()` always runs the agent non-streaming — but the decorator implements the streaming path anyway (per-update `UsageContent` reports sum on the tool's scope), so it stays correct if that ever changes.

## Avoid double counting

When the inner agent's own chat pipeline uses `UseToolTracking()`, the nested pipeline already rolls its report's `TotalUsage` up into the calling tool's scope automatically — core behavior since v0.2, see [Architecture: the ambient scope tree](architecture.md#the-ambient-scope-tree). With `trackUsage: true` on top of that, the same tokens are attributed **twice**: a 100-token inner run shows up as 200 on the tool's scope (pinned by the unit test `WithTracking_NestedTrackedPipelineWithTrackUsageTrue_DoubleCounts`). Pass `trackUsage: false` for self-tracked agents:

| Inner agent's pipeline | `trackUsage` | Attributed to the tool's scope |
| --- | --- | --- |
| Untracked | `true` (default) | `AgentResponse.Usage` — correct |
| Untracked | `false` | Nothing |
| Uses `UseToolTracking()` | `true` (default) | Nested rollup **+** `AgentResponse.Usage` — double-counted |
| Uses `UseToolTracking()` | `false` | Nested rollup only — correct |

```csharp
var chatOptions = new ChatOptions { Tools = [selfTrackedAgent.WithTracking(trackUsage: false)] };
```

The rule of thumb: attribute usage in exactly one place. `true` is the right default because typical agents-as-tools — hosted agents, or agents over a plain provider client — have untracked pipelines and would otherwise attribute nothing at all.

## Seeing the agent's own function calls

The outer model only ever sees the agent's final text — [by design](https://learn.microsoft.com/agent-framework/journey/agents-as-tools), the inner agent's tool calls are invisible to it. Progress events can see more, through two independent paths:

1. **Automatic — statuses reported by the agent's tools.** The agent runs in-process on the caller's async flow, so `ChatProgress.Report("Extracting...")` inside one of the agent's function tools resolves the ambient scope exactly as it would in a top-level tool, and the status surfaces as `ToolProgress` beneath the `"Calling {Agent} Agent"` header — no configuration, no bridging.
2. **Opt-in — `reportFunctionCalls: true`.** `WithTracking` installs Agent Framework [function-invocation middleware](https://learn.microsoft.com/agent-framework/agents/middleware/) (via `AIAgentBuilder.Use`) that reports a `"Calling {Function} Tool"` status each time the agent invokes one of its function tools — function names only, never arguments or results.

The opt-in has a constraint, which is why it defaults to `false`: function-invocation middleware requires an agent whose pipeline performs **local** function invocation, such as a `ChatClientAgent` over an `IChatClient`. Hosted, service-side agents (Foundry agents, for example) run their tools server-side, and the framework throws `InvalidOperationException` when the middleware cannot find a `FunctionInvokingChatClient` to intercept.

Both paths surface **sub-statuses on the agent's scope** — plain function tools the agent invokes get no scope of their own. A tool that is itself a `WithTracking`-wrapped agent is different: it opens its own child scope and renders as a nested activity — see [Nested agents](#nested-agents).

## Nested agents

An agent does not have to be a top-level tool. Since v0.3, a `WithTracking` wrapper invoked while another tool's scope is ambient opens its **own child scope** (via the core's [`ChatProgress.BeginToolScope`](architecture.md#the-ambient-scope-tree)): the nested run renders live as its own child activity — a `ToolInvoking` header carrying `ParentScopeId`/`Depth`, its own sub-statuses, completion, and duration — and lands as a child `ToolCallUsage` (with its own usage, duration, and `Succeeded` flag) under the enclosing call in the `ChatUsageReport`. Previously such a run surfaced only flat: sub-statuses and usage on the enclosing tool's scope. Two shapes reach this path:

**A — an agent as a tool of another agent.** The inner agent is registered as a tool of the outer agent, whose own function-invocation loop calls the wrapped function. Because `FunctionInvokingChatClient.CurrentContext` then describes the wrapper itself, the child scope is correlated with the **inner loop's function-call id** — `CallId` on the child's events is the inner model's call id.

```csharp
AIFunction packing = packingAgent.WithTracking();
AIAgent research = researchClient.AsAIAgent(
    instructions: "You research travel topics. Consult the Packing Agent tool for packing advice.",
    name: "Research Agent",
    tools: [AIFunctionFactory.Create(SearchNotes), packing]);

var chatOptions = new ChatOptions { Tools = [research.WithTracking(reportFunctionCalls: true)] };
```

```text
  [ToolInvoking] Calling Research Agent
    [ToolProgress] Calling SearchNotes Tool
    [ToolProgress] Searching notes…
    [ToolProgress] Calling Packing_Agent Tool
    [ToolInvoking] Calling Packing Agent          ← child scope, CallId = inner loop's call id
      [ToolProgress] Checking essentials…
    [ToolCompleted] Packing Agent completed
  [ToolCompleted] Research Agent completed
```

With `reportFunctionCalls: true` on the parent, **both** lines appear for the nested agent: the `"Calling Packing_Agent Tool"` sub-status (the middleware reporting the outer agent's function call, on the parent's scope) and the child card (the wrapper's own scope). They are complementary, not duplicates — turn off `reportFunctionCalls` to keep only the card.

**B — an agent invoked directly inside a tool body.** User code calls the wrapped function itself from inside a plain function tool. There is no inner invocation loop for the wrapper, and the enclosing tool's `CurrentContext` would mis-correlate the child, so the child's `CallId` is **`null`** — the tree wiring (`ParentScopeId`/`Depth`) and usage attribution are unaffected.

```csharp
AIFunction planTrip = AIFunctionFactory.Create(
    async (string city, CancellationToken cancellationToken) =>
    {
        object? advice = await packing.InvokeAsync(
            new AIFunctionArguments { ["query"] = $"What should I pack for {city}?" },
            cancellationToken);
        return $"Trip plan for {city}. Packing advice: {advice}";
    },
    "PlanTrip");
```

The mechanics, in both shapes:

- **Exactly one scope per invocation.** When the tracking middleware itself wrapped the function — the normal top-level registration on `ChatOptions.Tools` — the scope the tracker opened records the wrapper as its owner, and the wrapper's own `BeginToolScope` call is an inactive no-op (an owner-identity check that also traverses user `DelegatingAIFunction` chains via the `GetService` probe). Top-level behavior is byte-for-byte what it was before v0.3.
- **The [`trackUsage` matrix](#avoid-double-counting) is unchanged — it now applies at the child scope.** With the default `trackUsage: true` and an untracked inner pipeline, `AgentResponse.Usage` is attributed to the **child's** `ToolCallUsage`; with `trackUsage: false` and an untracked pipeline the child appears with `Usage = null`; a self-tracked inner pipeline rolls up into the child. Report totals are numerically invariant either way: the parent's rollup has always included its children, so `TotalUsage` and the parent's `Usage` match the old flat attribution exactly.
- **A failed nested run marks the child failed.** The child scope emits `ToolFailed` and its `ToolCallUsage.Succeeded` is `false`, while the enclosing tool decides for itself whether to catch and succeed. No exception details are recorded — the privacy invariant is unchanged.
- **Recursive self-invocation stays flat.** The same wrapped function invoked from within its own run is indistinguishable from the tracker's own delegation (same owner), so no child scope opens — a [known limitation](architecture.md#known-limitations-v03).

## Wrappers and limits

Classification requires the `WithTracking` wrapper. With `UseAgentToolClassification()` installed, the combinations are:

| Tool shape | Classification | Usage capture |
| --- | --- | --- |
| `agent.WithTracking()` | `ToolKind.Agent` | `AgentResponse.Usage` per run |
| `agent.WithTracking(trackUsage: false)` | `ToolKind.Agent` | Nested rollup only, when the agent's pipeline is tracked |
| Your own `DelegatingAIFunction` around a tracked wrapper | `ToolKind.Agent` (the probe traverses chains) | Preserved — your wrapper is never unwrapped or bypassed |
| Plain `agent.AsAIFunction()` | `ToolKind.Function` | None |

The last row is the framework limitation this package exists for: the function `AsAIFunction()` builds exposes neither its agent nor its usage, so there is nothing for the middleware to discover. The unit test `UseAgentToolClassification_PlainAsAIFunction_ClassifiesAsFunction` pins this — if a future framework release starts exposing the agent, the test fails and these docs get revisited.

## Privacy posture

Unchanged from the core: progress events and reports **never carry prompt content, tool arguments, or tool results**. `reportFunctionCalls` statuses carry function names only; the sole opt-in remains `ToolTrackingOptions.IncludeToolArguments` (default `false`), exactly as documented in [Architecture](architecture.md#privacy-posture).

## Test against real agents

Two test projects exercise the package against the real Agent Framework — the agent plumbing is never mocked:

- `tests\Andes.Extensions.AI.Agent.Unit.Test` — no network: inner agents are real `ChatClientAgent`s built with `scriptedChatClient.AsAIAgent(...)` over the linked `ScriptedChatClient` fake, driven end to end through the real tracked pipeline. The 28 tests cover classification and header rendering, usage attribution in both double-count directions, function-call reporting, the in-process ambient flow that carries tool statuses out of the agent, and nested child scopes in both shapes (`NestedAgentScopeTests`: agent-inside-agent with the inner call id, direct invocation with a `null` call id, single-scope dedup, child failure, and the `trackUsage: false` child).
- `tests\Andes.Extensions.AI.Agent.Integration.Test` — drives a real Azure OpenAI deployment with an agent as a tracked tool, end to end. Tests are `[SkippableFact]` and skip cleanly when configuration is missing. The project **links the same gitignored `appsettings.integration.json`** as the other integration projects — configure it once (copy the `.sample` file, fill in the `AzureOpenAI` section) and every integration project picks it up. Never environment variables.

```shell
dotnet test
```

## References

- [Microsoft Agent Framework — using an agent as a function tool](https://learn.microsoft.com/agent-framework/agents/tools/#using-an-agent-as-a-function-tool) and [agents as tools](https://learn.microsoft.com/agent-framework/journey/agents-as-tools)
- [`AIAgentExtensions.AsAIFunction`](https://learn.microsoft.com/dotnet/api/microsoft.agents.ai.aiagentextensions.asaifunction)
- [Agent Framework middleware](https://learn.microsoft.com/agent-framework/agents/middleware/) and the [`Microsoft.Agents.AI` package](https://www.nuget.org/packages/Microsoft.Agents.AI)
- [`DelegatingAIFunction`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.delegatingaifunction) and [`AITool.GetService`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.aitool.getservice)
