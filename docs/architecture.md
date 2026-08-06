# Architecture

`Andes.Extensions.AI` is a middleware library for [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) `IChatClient` pipelines, targeting **net10.0** and built with **C# 14**. Its first deliverable is **tool tracking**: a single `DelegatingChatClient` (`ToolTrackingChatClient`, registered with `UseToolTracking()`) that observes a chat request end to end and produces two things a production application needs but the raw pipeline does not give you:

1. **Progress events** — "Calling GetWeather Tool" headers, sub-statuses reported from inside the tool, a detected "Reasoning..." status when the model streams reasoning content, completion — delivered *while tools are still executing*, both in-band (as synthetic content in the streamed response) and out-of-band (to `IChatProgressObserver` implementations). Every event reports something the middleware actually observed; request-level statuses like "Starting request" are deliberately left to the application (see [Streaming design](#streaming-design-channel-merge)).
2. **A usage report** — token usage for the main assistant (per model turn when streaming), attributed per tool call (including LLM calls nested inside tools), rolled up into a `ChatUsageReport`.

This document explains why the middleware is shaped the way it is. For hands-on usage, see [Getting started](getting-started.md).

## Pipeline topology and the ordering invariant

`UseToolTracking()` **must be registered before** `UseFunctionInvocation()`. The tracker works by interposing on the tools themselves: it clones the caller's `ChatOptions` (the original instance is never mutated) and wraps every `AIFunction` in an internal `TrackingAIFunction : DelegatingAIFunction`, which preserves the inner function's name, description, and schema. The inner `FunctionInvokingChatClient` then unknowingly executes the wrappers, and each invocation opens a tracking scope, emits progress, and times itself.

```text
caller
  │  GetStreamingResponseAsync(messages, options { Tools = [AIFunction, ...] })
  ▼
┌────────────────────────────────────────────────────────────────┐
│ ToolTrackingChatClient                 (UseToolTracking, OUTER)│
│   clones ChatOptions; wraps each AIFunction in a               │
│   TrackingAIFunction; merges progress into the stream          │
└──────────────────────────────┬─────────────────────────────────┘
                               ▼
┌────────────────────────────────────────────────────────────────┐
│ FunctionInvokingChatClient       (UseFunctionInvocation, INNER)│
│   runs the model/tool loop; executes the TrackingAIFunction    │
│   wrappers as if they were the original tools                  │
└──────────────────────────────┬─────────────────────────────────┘
                               ▼
                provider IChatClient (Azure OpenAI, OpenAI, ...)
```

If the order is reversed, the function-invoking client executes the *unwrapped* tools and the tracker sees nothing but opaque text updates: no scopes, no per-tool usage, no sub-statuses.

Two smaller consequences of sitting outside the loop:

- Non-`AIFunction` tools (declarations the pipeline cannot invoke, hosted tools) pass through unwrapped; see [Known limitations](#known-limitations-v03).
- The tracker resolves `ChatClientMetadata` from the inner client to stamp `ProviderName` on the report and to fall back to the client's default model id when the provider does not report one.

## Streaming design: channel merge

The hard problem in streaming is that **tools execute inside the inner client's enumeration**. `FunctionInvokingChatClient` runs its model/tool loop as part of producing the stream, so a naive pass-through loop (`await foreach` over the inner stream, `yield return` each update) cannot yield anything while a tool is running — exactly the window in which progress events matter most.

`ToolTrackingChatClient.GetStreamingResponseAsync` therefore decouples production from consumption with an unbounded `Channel<ChatResponseUpdate>` configured as multi-writer/single-reader:

```text
                     ┌───────────────────────► IChatProgressObserver
                     │                          (out-of-band, every event)
 ToolTrackingChatClient
   ├── pump task (Task.Run) ── enumerates inner stream ─────┐
   │     · forwards every real update                       │ TryWrite
   │     · records UsageContent as assistant-turn usage     │
   │     · watches FunctionCallContent / FunctionResult     ▼
   ├── TrackingAIFunction wrappers ─────────────► Channel<ChatResponseUpdate>
   │     · ToolInvoking / ToolCompleted / ToolFailed        │ (unbounded,
   ├── ChatProgress ambient reporter ───────────►           │  multi-writer,
   │     · ToolProgress sub-statuses                        │  single-reader)
   └── outer iterator ◄──────── ReadAllAsync ───────────────┘
         after drain: RequestCompleted + UsageReportContent
```

- A **background pump task** enumerates the inner stream and `TryWrite`s every real update into the channel. While enumerating it records each `UsageContent` as assistant-turn usage, emits best-effort headers for `FunctionCallContent` naming tools it did not wrap, announces a single `Reasoning` event the first time it sees `TextReasoningContent` in a model turn (each update is inspected *before* it is written, so the status enters the channel ahead of the update carrying the reasoning content — and the event never carries the reasoning text), and advances the turn counter when it sees `FunctionResultContent` (a completed tool round-trip), re-arming reasoning detection for the next turn.
- **Tool wrappers and the ambient reporter** write synthetic updates into the same channel from whatever thread the function-invocation loop runs them on. Synthetic updates carry a single `ChatProgressContent` item and **no `TextContent`**, so text-accumulation helpers (`update.Text`, `ToChatResponse()`) are unaffected.
- **Bridged MCP progress notifications** (via the [`Andes.Extensions.AI.Mcp` satellite](mcp.md)) write from yet another thread — the MCP client's receive loop — so those `ToolProgress` events can arrive out of order relative to request-path events. The channel guarantees arrival order, not source order; `IChatProgressObserver` implementations must be thread-safe (already their documented contract); and a late notification racing request completion is dropped best-effort — the in-band write to the completed channel is a no-op, though an observer may see one late event.
- The **outer iterator** simply drains the channel with `ReadAllAsync` and yields each update in arrival order. After the channel completes, it builds the report, notifies observers, and appends the final `RequestCompleted` progress update and a `UsageReportContent` update (each independently switchable via options).

**Reasoning is detected, never guessed.** Earlier versions opened every request with synthetic `RequestStarted` ("Starting request") and `Thinking` ("Thinking...") events and re-announced "Thinking..." after each tool round-trip — statuses that asserted more than the middleware could know. Since v0.5 the only request-level status emitted mid-stream is `Reasoning` (the renamed `Thinking`, same underlying value), raised at most once per model turn and only because `TextReasoningContent` was observed on the response. Detection is content-based and therefore provider-agnostic: the OpenAI Responses API streams reasoning summaries as `TextReasoningContent` today, and any future provider producing the same content lights the status up with no middleware changes — while plain Chat Completions never streams reasoning, so chat pipelines simply never see it. Applications that want "Starting request"-style statuses construct them with the public factories (`ChatProgressUpdate.CreateRequestStarted()` / `CreateReasoning()`, stamped with the well-known `ChatProgressUpdate.ExternalScopeId`) and interleave them via `ToResponseUpdate()`, which produces the exact synthetic shape the middleware emits — see [Getting started](getting-started.md#emit-request-level-statuses-yourself).

**Cancellation and failure.** The pump runs under a CTS linked to the consumer's token. The `finally` around the drain loop cancels that CTS — a no-op on normal completion, but it stops the inner stream promptly if the consumer abandons the iterator early — and then awaits the pump task so it is always observed. If the inner stream throws, the pump completes the channel with that exception, so the failure surfaces to the consumer through the drain loop exactly as it would have without the middleware.

**Accounting always lands.** Tokens consumed before a fault, cancellation, or abandonment were still billed, so the middleware never discards them: whenever a request ends without draining to completion (both call styles), observers receive a `ChatProgressKind.RequestFailed` event followed by their once-per-request `OnRequestCompleted` call with the (possibly partial) report, and a nested parent scope still receives the rollup. In-band synthetic updates are yielded on the success path only — on failure there is no stream left to write to.

The non-streaming path (`GetResponseAsync`) needs no channel: there is nowhere to interleave synthetic updates, so progress goes to observers only, and the report is attached to the response (see below). Reasoning detection mirrors the streaming path post-hoc: model turns are indistinguishable in an aggregated response, so if any response message contains `TextReasoningContent`, at most one `Reasoning` event is raised for the whole request — observers only, like every other non-streaming progress event.

## The ambient scope tree

Progress and usage attribution both hang off a per-request **scope tree**, flowed through an `AsyncLocal` (internal `AmbientScope`, nodes are internal `ToolScope`):

- Each request creates a **root scope** (depth 0). Before starting work, the tracker makes it ambient — in streaming, it is set just before `Task.Run` so the captured `ExecutionContext` carries it into the pump and from there into the function-invocation loop.
- Each tool invocation opens a **child scope** (depth = parent + 1). The parent is *the scope ambient at invoke time*, which makes both concurrent tool invocations and nested tools safe without any bookkeeping in user code: parallel invocations each become independent children of the root, and a tool invoked from inside another tool becomes a grandchild.
- `ChatProgress.Report(...)` inside a tool resolves the ambient scope and attaches the sub-status to *that* tool's scope, one level deeper than its header — no plumbing through tool signatures.
- A **nested tracked pipeline** — a second `UseToolTracking()` pipeline run inside a tool, as an agent exposed as a function would — captures the ambient scope as its parent on entry, and on completion rolls its report's `TotalUsage` up into that tool's scope. This is the mechanism the [`Andes.Extensions.AI.Agent` satellite](agents.md) rides on — its `WithTracking(trackUsage: false)` opt-out exists precisely because this rollup already happens for self-tracked agents — and it works with no package beyond the core (covered by `NestedUsageAttributionTests`).
- **Opening a child scope is public API** (v0.3): `ChatProgress.BeginToolScope(descriptor, owner)` opens a real child scope on the ambient tracker and returns a disposable `ChatProgressToolScope` handle. The nested operation renders as its own child activity — a `ToolInvoking`/`ToolCompleted` pair carrying `ParentScopeId`/`Depth`, in-band and to observers — and lands as a child `ToolCallUsage` in the report. The [MCP](mcp.md#nested-mcp-tools) and [Agent](agents.md#nested-agents) satellite wrappers call it in `InvokeCoreAsync` with `owner: this`, which is how an agent or MCP tool invoked *inside* another tool gets its own card; any tool author can call it directly (a `null` owner always opens) to give a sub-operation its own child activity. `Fail()` before disposal records a failure — with no exception details, per the privacy posture. The method is static-only by design, deliberately absent from `IChatProgressReporter`: a captured reporter may run off the original async flow (the MCP receive loop, for example), where pushing an ambient scope would have no effect.
- **Owner-identity dedup keeps the single-level case unchanged.** `TrackingAIFunction` records the unwrapped function it delegates to as the scope's owner; `BeginToolScope` returns an inactive no-op handle when the ambient scope was already opened for the caller — checked by reference identity plus the same `GetService` probe chain the classifiers use, so a user's `DelegatingAIFunction` around a satellite wrapper still deduplicates. A tool the tracker wrapped itself therefore opens exactly one scope, byte-for-byte the pre-v0.3 behavior. The model's function-call id is trusted only when `FunctionInvokingChatClient.CurrentContext.Function` *is* the owner (a nested function-invoking loop, as when an inner agent's own loop calls the tool); a wrapped function invoked directly inside a tool body gets a `null` `CallId`.

Scopes carry the `ToolDescriptor`, the model's `CallId` (correlated via `FunctionInvokingChatClient.CurrentContext`), duration, success flag, attributed usage, and children. Every `ChatProgressUpdate` exposes `ScopeId`/`ParentScopeId`/`Depth`, so consumers can reconstruct the tree without holding any state beyond the events themselves. `ToolProgress` events additionally carry optional numeric `Progress`/`ProgressTotal` values (v0.2) when the reporter supplied them — a tool calling the `ChatProgress.Report(status, progress, progressTotal)` overload, or a bridged MCP progress notification.

## Usage attribution

Usage flows into the report from exactly three sources:

| # | Source | Attributed to |
|---|--------|---------------|
| 1 | `UsageContent` observed by the pump on the inner stream (or `ChatResponse.Usage` when non-streaming) | The **assistant**, keyed to the current model turn |
| 2 | `ChatProgress.ReportUsage(...)` called inside a tool (for example, from an SDK call the tool makes) | That **tool's scope** |
| 3 | A nested tracked pipeline completing inside a tool | That **tool's scope** (the nested report's `TotalUsage`) |

The sources are unchanged in v0.3, but sources 2 and 3 resolve the *innermost* ambient scope — which can now be a nested scope opened with `ChatProgress.BeginToolScope`, so usage reported inside a child is attributed to the child's `ToolCallUsage`. Totals are invariant either way: a parent's rollup has always been its own usage plus its children's recursive rollups, so `TotalUsage` and every per-call rollup are numerically identical to the flat attribution of earlier versions.

At report time each scope's own usage is combined with its children's rollups (a `ToolCallUsage.Usage` of `null` means nothing was attributed), and the request total is assistant usage plus the top-level tool rollups. All arithmetic uses nullable-aware addition (`UsageMath`): an absent token count means "not reported", never zero, and provider-specific `AdditionalCounts` are summed by key.

The report shape:

```text
ChatUsageReport
├── AssistantUsage   UsageDetails          — model turns only, tools excluded
├── Turns            AssistantTurnUsage[]  — per-iteration breakdown (streaming only)
├── ToolCalls        ToolCallUsage[]       — top-level invocations; each has
│                                            CallId, ToolName, Kind, Source,
│                                            Usage (rollup), Children, Duration, Succeeded
├── TotalUsage       UsageDetails          — AssistantUsage + tool rollups
├── ModelId / ProviderName / ResponseId    — from the response, falling back to
│                                            the inner client's ChatClientMetadata
└── Duration         TimeSpan              — wall clock for the whole request
```

Delivery differs by call style. Streaming: the report arrives as the final `UsageReportContent` update and via `IChatProgressObserver.OnRequestCompleted`. Non-streaming: `Turns` is empty (providers report one aggregate) and the report is attached to `ChatResponse.AdditionalProperties` under `ToolTrackingChatClient.UsageReportPropertyName` (`"andes.ai.usage_report"`).

## Privacy posture

Progress events and reports **never carry prompt content, tool arguments, or tool results**. The only opt-in is `ToolTrackingOptions.IncludeToolArguments` (default `false`), which populates `ChatProgressUpdate.Arguments` on `ToolInvoking` events with *stringified* argument values only — nothing else changes, and results remain excluded even then. Sub-status text passed to `ChatProgress.Report` is documented as display text and must not carry prompt content or tool results.

## Known limitations (v0.3)

- **Synthetic content does not serialize.** `ChatProgressContent` and `UsageReportContent` are not part of the `AIJsonUtilities` polymorphic serialization contract for `AIContent`. Call `StripProgressContent()` on responses (or individual messages) before persisting them into conversation history or serializing them.
- **Non-`AIFunction` tools get best-effort headers only.** Tool declarations the pipeline cannot invoke are recognized purely by observing `FunctionCallContent` on the stream: they receive a `ToolInvoking` event (kind `ToolKind.Unknown`, parented to the root) but no completion event, duration, or usage attribution.
- **`FunctionInvokingChatClient.AdditionalTools` are invisible.** The tracker only wraps tools found on `ChatOptions.Tools`; tools injected by the inner client's own configuration are executed unwrapped.
- **Turn boundaries are a heuristic.** Streamed usage entries are keyed by an iteration counter that advances when a `FunctionResultContent` is observed. Providers that echo function results unusually, or interleave turns, may attribute usage to a neighboring turn — `AssistantUsage` and `TotalUsage` are unaffected either way.
- **A never-disposed `ChatProgressToolScope` leaves its scope open.** Dispose the handle on the same async flow that opened it (the `using` pattern); an undisposed handle emits no completion event, and the scope lands in the report as succeeded with no duration.
- **Recursive self-invocation stays flat.** A wrapped function invoked from within its own run presents the same owner as the tracker's own delegation, so the owner-identity dedup suppresses the child scope and the recursive call surfaces flat on the enclosing scope.

## Satellite packages

MCP tools ("Calling {Server} MCP") shipped in v0.2 as the satellite package **`Andes.Extensions.AI.Mcp`** — and as more than classification: `UseMcpToolClassification()` recognizes `McpClientTool` instances (raw, wrapped, or inside user delegating chains) as `ToolKind.McpTool` with the server name as `Source`, and `WithTracking(...)` additionally bridges the server's `notifications/progress` into `ToolProgress` updates with numeric `Progress`/`ProgressTotal` values. See [MCP tool tracking](mcp.md) for the classification precedence, the progress bridge's design, and its ordering caveats.

Microsoft Agent Framework agents as tools ("Calling {Agent} Agent") shipped as the satellite package **`Andes.Extensions.AI.Agent`** — and as more than classification, because the framework's `AsAIFunction()` exposes neither the agent behind the function nor the run's `AgentResponse.Usage`: `WithTracking(...)` wraps the agent-as-function so `UseAgentToolClassification()` can recognize it (including inside user delegating chains) as `ToolKind.Agent` with the agent's name as `Source`, attributes each run's usage to the calling tool's scope (opt out with `trackUsage: false` for self-tracked agents, whose nested pipelines already roll up), and can optionally report the agent's own function calls as progress statuses. The tracking mechanics underneath — scope nesting, ambient reporting, nested-pipeline usage rollup — needed no changes, exactly as the nested-pipeline tests had demonstrated. See [Agent tool tracking](agents.md) for the classification precedence, the run-time ambient design (the inverse of the MCP bridge's capture-at-invocation), and the double-count matrix.

In v0.3 both satellite wrappers additionally open a child scope (`ChatProgress.BeginToolScope`, [above](#the-ambient-scope-tree)) when invoked inside another tracked tool, so nested agents and MCP tools render as their own child activities instead of flat sub-statuses — see [Nested agents](agents.md#nested-agents) and [Nested MCP tools](mcp.md#nested-mcp-tools).

A serializable UI status contract shipped as the satellite package **`Andes.Extensions.AI.UI`** — not a new tracking capability, but a translation layer: `ToUiEventsAsync()`/`ToStatusSnapshotsAsync()` project the in-band `ChatProgressContent`/`UsageReportContent` stream into flat `AssistantUiEvent` deltas and folded `AssistantStatusSnapshot` trees (an `AssistantActivity` per function, MCP tool, or agent, with nested `Children` for tools invoked inside another — the same scope-tree shape this document describes above, made serializable). Every activity carries a clean `DisplayName` and a separate `Kind` badge rather than a composed header string, so a UI label never repeats the kind word (a server named "Andes Test MCP" is `DisplayName: "Andes Test MCP"` plus `Kind: McpTool`, never "Andes Test MCP MCP"). A `System.Text.Json` source-generated context and a byte-for-byte matching TypeScript file ship together, so a Blazor client and a hand-rolled SPA fold the identical JSON into the identical tree. See [UI status contract](ui.md) for the two DTO layers, the mapper, the reducer, and the shipped TypeScript file.

## References

The design is grounded in the official Microsoft.Extensions.AI documentation:

- [The IChatClient interface — pipelines and custom middleware](https://learn.microsoft.com/dotnet/ai/ichatclient)
- [Microsoft.Extensions.AI libraries overview](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai)
- [`ChatClientBuilder.Use`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.chatclientbuilder.use)
- [`FunctionInvokingChatClient`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.functioninvokingchatclient) and [`FunctionInvokingChatClient.CurrentContext`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.functioninvokingchatclient.currentcontext)
- [`FunctionInvocationContext`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.functioninvocationcontext)
- [`DelegatingAIFunction`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.delegatingaifunction)
- [`UsageDetails`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.usagedetails)
- [Access ambient data from within AIFunction invocations](https://learn.microsoft.com/dotnet/ai/how-to/access-data-in-functions)
