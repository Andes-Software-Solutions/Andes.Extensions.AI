# UI Status Contract

`Andes.Extensions.AI.UI` adds a serializable, cross-language status contract on top of the core [`Andes.Extensions.AI`](getting-started.md) tool-tracking middleware. The core middleware already streams everything a UI needs — request-level statuses, tool headers, sub-statuses, and a final usage report — in-band as `ChatProgressContent`/`UsageReportContent` items inside the `ChatResponseUpdate` stream. But that stream is shaped for a .NET consumer holding a reference to `Microsoft.Extensions.AI` types, not for an API boundary: an ASP.NET endpoint streaming JSON to a browser, or a Blazor WebAssembly component, needs a shape it can serialize and a TypeScript type it can share. This satellite package supplies both:

1. **A serializable contract** — flat per-flush `AssistantUiEvent` deltas and a folded `AssistantStatusSnapshot` (the request's status line plus a hierarchy of `AssistantActivity` cards — functions, MCP tools, and agents, each with sub-statuses, nested children, and token usage). Every activity carries a clean `DisplayName` plus a separate `Kind` badge — there is no pre-composed "Calling … MCP/Agent/Tool" string anywhere in the contract, so the kind word is never repeated. See [Clean names, not composed headers](#clean-names-not-composed-headers).
2. **A mapper and a reducer** — `ToUiEventsAsync()`/`ToStatusSnapshotsAsync()` project the tracked stream into the contract; `AssistantStatusReducer` folds events into snapshots on the .NET side. A byte-for-byte matching TypeScript file (`typescript/andes-assistant-ui.ts`) ships in the package, with its own `foldAssistantEvents` reducer, so a SPA reconstructs the identical tree from the identical JSON.

This guide covers installation, the two DTO layers, the mapper and reducer, the shipped TypeScript file, and the package's privacy posture. For the core middleware's design, see [Architecture](architecture.md); for core usage, see [Getting started](getting-started.md). For a runnable end-to-end example with three consumer surfaces sharing this contract, see [Example: the UI contract, three ways](examples/ui-contract.md).

## Prerequisites and installation

- .NET SDK **10.0** or later (the package targets `net10.0`).
- The core pipeline from [Getting started](getting-started.md) — `UseToolTracking()` before `UseFunctionInvocation()`.

```shell
dotnet add package Andes.Extensions.AI.UI
```

Installing the package brings in the core `Andes.Extensions.AI` package (>= 0.11.0) and `Microsoft.Extensions.AI.Abstractions` — nothing else. The package does not reference the [MCP](mcp.md) or [Agent](agents.md) satellites; it doesn't need to, because `ToolKind` (the `Unknown`/`Function`/`McpTool`/`Agent` badge every activity carries) already lives in core, shared by every satellite.

## Quickstart

Build the tracked pipeline exactly as in [Getting started](getting-started.md), then stream `AssistantStatusSnapshot` instead of parsing `ChatResponseUpdate` yourself:

```csharp
using Andes.Extensions.AI;
using Microsoft.Extensions.AI;

IChatClient client = innerClient
    .AsBuilder()
    .UseToolTracking()
    .UseFunctionInvocation() // tracking before function invocation (core invariant)
    .Build();

// Stream immutable snapshots and bind them to the UI (Blazor, console, ...).
await foreach (AssistantStatusSnapshot snapshot in client
    .GetStreamingResponseAsync("prompt", chatOptions)
    .ToStatusSnapshotsAsync())
{
    Console.WriteLine(snapshot.AssistantStatus);
    foreach (AssistantActivity activity in snapshot.Activities)
    {
        // e.g. "get_forecast" [McpTool] — the MCP tool name; the server ("Andes Test MCP") is activity.Source
        Console.WriteLine($"{activity.DisplayName} [{activity.Kind}] — {activity.State}");
    }
}
```

For an HTTP surface, stream `ToUiEventsAsync()` instead and serialize each event with `AssistantUiJsonContext` over server-sent events; the browser folds them with `foldAssistantEvents` from the shipped `.ts` file — see the [full example](examples/ui-contract.md).

## Two DTO layers: events and snapshots

The contract is deliberately split into two shapes, mirroring a common streaming-UI pattern (deltas over the wire, state in memory):

| Layer | Type | Shape | Use it for |
| --- | --- | --- | --- |
| Wire | `AssistantUiEvent` | Flat, one per stream flush, discriminated by `Kind` | Sending over HTTP (SSE, WebSocket) — small, order-sensitive, cheap to serialize |
| Render | `AssistantStatusSnapshot` | Nested, immutable, one per fold | Binding to a UI — `Activities` is already a tree, ready to render without any client-side bookkeeping beyond calling the reducer |

### `AssistantUiEvent` — the wire shape

`AssistantUiEvent` is a flat record with one `AssistantUiEventKind` discriminator and every field any kind might need; unused fields are `null` (and, once serialized through `AssistantUiJsonContext`, omitted):

```text
AssistantUiEventKind
├── Status              — Message is the new request-level status line ("Reasoning…")
├── ActivityStarted     — ScopeId/ParentScopeId/Depth/ToolKind/DisplayName/Source describe the new card
├── ActivityProgress    — ScopeId targets the owning activity; Message/Progress/ProgressTotal are the sub-status
├── ActivityCompleted   — ScopeId targets the activity; DurationSeconds is set
├── ActivityFailed      — same as ActivityCompleted, but the activity failed
├── TextDelta           — Text is a chunk of the assistant's answer
├── ReasoningDelta      — Text is a chunk of the model's reasoning summary
└── Finished            — Usage carries the final token totals; DurationSeconds is the whole request

Every kind above may additionally carry Metadata — an application-supplied
IReadOnlyDictionary<string, string>?, present or absent independent of Kind.
```

`ScopeId`/`ParentScopeId`/`Depth` are carried over unchanged from the core's `ChatProgressUpdate`, so the same tree-reconstruction rules from [Getting started](getting-started.md#consume-streaming-progress) and the [Progress Board example](examples/progress-board.md#the-progress-board-hierarchy-from-the-event-contract) apply here — this contract just makes them serializable.

### `Metadata` — an application-owned side channel

`AssistantUiEvent.Metadata` (`IReadOnlyDictionary<string, string>?`, since 0.8.0, [#14](https://github.com/Andes-Software-Solutions/Andes.Extensions.AI/issues/14)) is the one field on the wire shape the package itself never populates and never interprets — it's a slot for whatever correlation data the application producing the stream wants to hand its own consumer alongside a specific event. The mapper (`ToUiEventsAsync`, `ToUiEvent`, and `ToSnapshot(ChatUsageReport)`) always leaves it `null`; an application attaches values by rewriting the events it re-streams:

```csharp
await foreach (AssistantUiEvent uiEvent in client
    .GetStreamingResponseAsync(prompt, chatOptions, cancellationToken)
    .ToUiEventsAsync(cancellationToken))
{
    AssistantUiEvent toSend = uiEvent.Kind == AssistantUiEventKind.Finished
        ? uiEvent with { Metadata = new Dictionary<string, string> { ["messageId"] = persistedMessageId } }
        : uiEvent;
    yield return toSend; // e.g. from an async-iterator method backing the SSE endpoint
}
```

A motivating case: an API that streams `ToUiEventsAsync()` over SSE and persists each turn's answer as a transcript document wants to hand the client the persisted message id on the `Finished` frame — so the UI can offer per-message actions (rating, a permalink) without a transcript refetch.

`Metadata`'s dictionary **keys travel verbatim** through `AssistantUiJsonContext` — the camelCase naming policy applies to property names, not to dictionary keys, so `"MessageId"` and `"conversation-id"` serialize exactly as written, ordinal case-sensitive on the way back in. Like every other nullable field, a `null` bag is omitted from the JSON; an *empty* bag (`Metadata = new Dictionary<string, string>()`) is not `null`, so it serializes as `"metadata":{}` — but see [The reducer](#the-reducer-assistantstatusreducer): both the C# and TypeScript folds treat `null` and empty bags identically as no-ops, and the reducer itself never produces an empty bag.

Every request-level kind collapses to `Status` with the message passed through — the middleware's detected `Reasoning`, its closing `ReasoningCompleted` ("Reasoning completed", whose `DurationSeconds` carries the elapsed reasoning time — `ToUiEvent` maps `Duration` generically, so no special-casing was needed), and final `RequestCompleted`, and equally any update the application constructs itself with `ChatProgressUpdate.CreateCustom(...)` and prepends via `ToResponseUpdate()` ([Getting started](getting-started.md#emit-request-level-statuses-yourself)); the mapper does not care who emitted it. The detected `Reasoning` progress event is no exception — it still collapses to a `Status` line ("Reasoning…"), because the progress event never carries the reasoning text; the *text* arrives separately as `ReasoningDelta` events, sourced from the in-band `TextReasoningContent` itself. Note that the middleware no longer opens requests with a synthetic status of its own (since core v0.5), so `AssistantStatus` stays `null` until the first request-level event arrives — a UI that wants a status line the instant the request starts prepends its own, exactly as both sample apps do.

### `AssistantStatusSnapshot` — the render shape

`AssistantStatusSnapshot` is the folded result: an immutable value with the current `AssistantStatus` line, the overall `Phase` (`ActivityState.Running`/`Completed`/`Failed`), the answer `Text` accumulated so far, the model's `ReasoningText` accumulated so far (when the provider streams reasoning summaries — verbatim concatenation across the whole request, including tool round-trips), the final `Usage`, and — the interesting part — `Activities`, an already-nested `IReadOnlyList<AssistantActivity>`:

```csharp
public sealed record AssistantActivity
{
    public required string ScopeId { get; init; }
    public required string DisplayName { get; init; }
    public ToolKind Kind { get; init; }
    public string? Source { get; init; }
    public ActivityState State { get; init; } = ActivityState.Running;
    public double? DurationSeconds { get; init; }
    public IReadOnlyList<SubStatus> SubStatuses { get; init; } = [];
    public IReadOnlyList<AssistantActivity> Children { get; init; } = [];
    public UsageSummary? Usage { get; init; }
}
```

`Children` is what makes the tree recursive, and as of v0.3 it fills **live**: the [MCP](mcp.md#nested-mcp-tools) and [Agent](agents.md#nested-agents) satellite wrappers open a real child scope when invoked inside another tool, so a nested agent or MCP tool streams in as a child `AssistantActivity` under the enclosing card while it runs — as do a nested tracked pipeline's tool calls and any child scope a tool opens itself with `ChatProgress.BeginToolScope` (see [Architecture: the ambient scope tree](architecture.md#the-ambient-scope-tree)). This package needed zero changes for that: the reducer has always attached activities by `ParentScopeId`, whoever opens the scope. A UI renders children with the same component, one level deeper, with no special-casing.

`SubStatus` (`Message`, `Progress`, `ProgressTotal`) and `UsageSummary` (`InputTokens`, `OutputTokens`, `TotalTokens`, all nullable) are small, flat leaf records — `UsageSummary` is `Microsoft.Extensions.AI.UsageDetails` flattened to primitives so it serializes without pulling that type's shape into the wire contract.

`AssistantStatusSnapshot.Metadata` (`IReadOnlyDictionary<string, string>?`, since 0.8.0) is the accumulated counterpart of the per-event `Metadata` field above: when the snapshot comes from `AssistantStatusReducer` (or the TypeScript `foldAssistantEvents`), it's the running merge of every folded event's `Metadata` bag, last write per key winning — see [The reducer](#the-reducer-assistantstatusreducer) for the merge rule. A snapshot built by `ToSnapshot(ChatUsageReport)` leaves it `null`, same as every other field the mapper never populates.

## The mapper: `ChatResponseUiExtensions`

Four static members turn the tracked, in-band stream into the contract:

| Member | Input | Output |
| --- | --- | --- |
| `ToUiEventsAsync(this IAsyncEnumerable<ChatResponseUpdate>, CancellationToken)` | The tracked streaming response | `IAsyncEnumerable<AssistantUiEvent>` — one event per `ChatProgressContent`, one `ReasoningDelta` per `TextReasoningContent` with non-empty text (encrypted-only reasoning items carry nothing renderable and are skipped), one `TextDelta` per non-empty `update.Text`, one final `Finished` from `UsageReportContent` |
| `ToStatusSnapshotsAsync(this IAsyncEnumerable<ChatResponseUpdate>, CancellationToken)` | The tracked streaming response | `IAsyncEnumerable<AssistantStatusSnapshot>` — `ToUiEventsAsync` piped through a private `AssistantStatusReducer`, one snapshot per event |
| `ToUiEvent(this ChatProgressUpdate)` | A single core progress update | The equivalent `AssistantUiEvent` |
| `ToUsageSummary(this UsageDetails)` | A core usage value | The flattened `UsageSummary` |
| `ToSnapshot(this ChatUsageReport)` | A completed usage report (for example the non-streaming `ChatResponse.AdditionalProperties` report) | A `Completed`-phase snapshot built directly from the report's `ToolCalls` tree — useful when all you have is the final report, not the live stream. It carries no `ReasoningText` (or `Text`): reports contain no model content |

The mapper is where the clean-name design lives: `ToUiEvent` derives `DisplayName` from the raw tool name and source — never from `update.Message`, which is the *composed* header text ("Calling GetWeather Tool", "Calling Andes Test MCP"). The rule is kind-conditional (v0.7): for `ToolKind.McpTool` the `DisplayName` is the raw MCP **tool** name (`update.ToolName`, e.g. `get_forecast`) because the server name already travels in `Source` and one server exposes many tools; for every other kind it stays source-first (`update.ToolSource ?? update.ToolName` — an agent's source is its display-worthy name, and plain functions rarely have a source). `ToSnapshot`'s `ToActivity` helper applies the same rule from a `ToolCallUsage`. It also recurses `ToolCallUsage.Children`, so nested activities — including the v0.3 satellite child scopes — arrive with their own per-node `Usage`, matching the live tree's shape. See [Clean names, not composed headers](#clean-names-not-composed-headers) for why this matters.

**The mapper never populates `Metadata`.** `ToUiEventsAsync`, `ToUiEvent`, and `ToSnapshot(ChatUsageReport)` all leave `AssistantUiEvent.Metadata` (and, transitively, `AssistantStatusSnapshot.Metadata`) `null` on every event and every snapshot they produce — the core progress pipeline has no concept of application-supplied metadata to project from. Attaching values is entirely the calling application's job, done by rewriting the events it re-streams (see [`Metadata` — an application-owned side channel](#metadata--an-application-owned-side-channel)).

## The reducer: `AssistantStatusReducer`

`AssistantStatusReducer` is a small stateful fold: construct one per request, and call `Apply(AssistantUiEvent)` for every event in order — it returns the resulting `AssistantStatusSnapshot` each time:

```csharp
var reducer = new AssistantStatusReducer();
await foreach (AssistantUiEvent uiEvent in events)
{
    AssistantStatusSnapshot snapshot = reducer.Apply(uiEvent);
    Render(snapshot);
}
```

It rebuilds the activity tree the same way the [Progress Board example](examples/progress-board.md#the-progress-board-hierarchy-from-the-event-contract) does: `ActivityStarted` opens a scope and either attaches it under `ParentScopeId` (if that scope is already known) or adds it as a root — a top-level activity's parent is the request root, which has no card. `ActivityProgress` appends a `SubStatus` to the owning scope; `ActivityCompleted`/`ActivityFailed` set `State` and `DurationSeconds`. `TextDelta` accumulates `Text`; `ReasoningDelta` accumulates `ReasoningText`; `Finished` sets `Phase = Completed` and `Usage`.

**`Metadata` merges before any of that kind-specific handling runs** (since 0.8.0): `Apply` folds `uiEvent.Metadata` into the accumulated `AssistantStatusSnapshot.Metadata` first, then falls into the `switch` on `Kind` — so *every* event contributes its metadata, including a `Kind` this version of the reducer doesn't recognize (a future kind added to the wire contract before the reducer is updated to handle it). The merge is a plain dictionary overlay: last write per key wins across the whole request, and a `null` or empty `Metadata` bag is a no-op that leaves the accumulated bag exactly as it was (reusing the same instance — no copy, no allocation). The reducer's own copy-on-write means an earlier snapshot's `Metadata` is never mutated by a later merge; the TypeScript `foldAssistantEvents` gets the same guarantee from an object spread (`{ ...snapshot.metadata, ...event.metadata }`) in its own `mergeMetadata` helper, applied before its `switch`'s `default` arm can even be reached. The two folds are deliberately kept in lockstep on this rule.

`ToStatusSnapshotsAsync` already wraps this for you over a live stream — reach for `AssistantStatusReducer` directly only when you're consuming events from somewhere else (deserialized off the wire, replayed from storage, or, in a Blazor WebAssembly app, received over SignalR or fetched as SSE).

**A reducer is single-consumer and stateful.** Feed it the events of exactly one request, in order; the core stream and the SSE encoding described in the [example](examples/ui-contract.md) already preserve that order, so a new reducer per request is all a consumer needs — no locking.

## The TypeScript file

`typescript/andes-assistant-ui.ts` ships inside the NuGet package (`PackagePath: typescript\`) as a 1:1 mirror of the C# types and the JSON `AssistantUiJsonContext` produces:

- `ToolKind`, `AssistantUiEventKind`, and `ActivityState` as string-literal unions (`"Unknown" | "Function" | "McpTool" | "Agent"`, and so on) — matching the string enum values `AssistantUiJsonContext` serializes.
- `UsageSummary`, `SubStatus`, `AssistantActivity`, `AssistantStatusSnapshot`, and `AssistantUiEvent` interfaces with camelCase properties, each optional member marked `?` (mirroring the C# nullable members that `AssistantUiJsonContext` omits when `null`).
- `metadata?: Record<string, string>` (since 0.8.0) on both `AssistantUiEvent` and `AssistantStatusSnapshot`, last in each interface — the mirror of `AssistantUiEvent.Metadata`/`AssistantStatusSnapshot.Metadata`. The contract itself never sets it.
- `createInitialSnapshot()` — the empty starting snapshot (`{ phase: "Running", activities: [] }`) to fold events into.
- `foldAssistantEvents(snapshot, event)` — the TypeScript counterpart of `AssistantStatusReducer.Apply`, immutable and pure: it returns a new `AssistantStatusSnapshot` rather than mutating the one you pass in, which is what makes it a natural fit for a React/Vue/Svelte render loop. Its internal `mergeMetadata` helper runs before the `kind` switch, exactly like the C# reducer — see [The reducer](#the-reducer-assistantstatusreducer).

```typescript
import { createInitialSnapshot, foldAssistantEvents, type AssistantUiEvent } from "./andes-assistant-ui";

let snapshot = createInitialSnapshot();
for (const event of events as AssistantUiEvent[]) {
  snapshot = foldAssistantEvents(snapshot, event);
  render(snapshot);
}
```

Because the file ships in the package rather than as a separately versioned npm dependency, copy it into your frontend project (or reference it from the extracted nupkg's `typescript/` folder) and keep it alongside the `Andes.Extensions.AI.UI` version your backend uses — the two are meant to travel together. See the [full SPA example](examples/ui-contract.md#c-the-typescript-spa-consumer) for consuming it over server-sent events end to end.

## Clean names, not composed headers

This is the whole reason the contract exists, so it's worth stating plainly: **every activity carries a clean `DisplayName` and a separate `Kind` badge — never a pre-composed header string.**

The core middleware's progress headers are composed text meant for a plain-text console or log line: `ToolTrackingOptions.HeaderFormatter`'s default produces `"Calling {DisplayName} Tool"` for functions, `"Calling {Source} MCP"` for MCP tools, and `"Calling {DisplayName} Agent"` for agents — and, as of core v0.2, that formatter already avoids doubling the kind word when the name ends with it (`"Andes Test MCP"` renders as `"Calling Andes Test MCP"`, not `"Calling Andes Test MCP MCP"`; `"Research Agent"` renders as `"Calling Research Agent"`). That fix helps *console and log output*, but it's still one composed English string — not something a UI can restyle, badge, or localize.

The UI contract sidesteps the composition problem entirely instead of patching it further: `AssistantActivity.DisplayName` (and `AssistantUiEvent.DisplayName`) is always the *raw* name — the function's registered name, the MCP tool's name, or the agent's name — with no "Calling" prefix and no kind word appended, ever. `Kind` (the core `ToolKind`: `Function`, `McpTool`, or `Agent`) travels alongside it as its own field, meant to render as a badge or icon rather than be concatenated into the label. For MCP activities the tool and the server are two separate fields (v0.7, [#12](https://github.com/Andes-Software-Solutions/Andes.Extensions.AI/issues/12)): a tool named `get_forecast` on a server named "Andes Test MCP" renders as `DisplayName: "get_forecast"`, `Source: "Andes Test MCP"`, `Kind: McpTool` — so two tools from the same server stay distinguishable, the server name isn't duplicated across both fields, and the string "MCP" never appears twice no matter how the server was named. The name ships raw and unformatted (including any model-facing renames) so clients apply their own presentation conventions. Because there's no English text baked into the field, a UI can localize the `Kind` badge (a fixed, small enum) independently of the `DisplayName` (arbitrary, unlocalized, developer-supplied text) — something a composed header string could never support.

## Privacy posture

Unchanged from the core: events and snapshots **never carry prompt content, tool arguments, or tool results** — only headers-turned-names, statuses, sub-status text, activity metadata (`ScopeId`, `Kind`, `Source`, timing), and token counts. Model *outputs* are the deliberate exception, with a clear boundary: the answer text (`TextDelta`/`Text`) and the reasoning summary (`ReasoningDelta`/`ReasoningText`) are sourced only from the in-band content the tracked stream already carries — never from progress metadata, which stays text-free at the core layer. Everything else the mapper reads comes from `ChatProgressUpdate`/`ChatUsageReport`, which already enforce the posture (the sole opt-in there remains `ToolTrackingOptions.IncludeToolArguments`, default `false`); this package adds no new opt-in and cannot re-introduce content the core never emitted. See [Architecture: Privacy posture](architecture.md#privacy-posture).

The `Metadata` bag (since 0.8.0) is a deliberate, narrow exception to "this package decides what's on the wire": it's **application-owned**, not package-owned. The package never populates it, never reads it, and never interprets it — it's a `null` field until an application attaches its own values by rewriting events. Its privacy posture is therefore whatever the attaching API gives it: an application that puts PII or secrets into `Metadata` has made that choice itself, the same way it would if it added a custom HTTP header to the same response. The package's own fields — everything else on `AssistantUiEvent` and `AssistantStatusSnapshot` — keep the guarantee above regardless of what an application puts in `Metadata`.

## References

- [Getting started](getting-started.md) — the core pipeline, `ChatProgress.Report`, and the streamed `ChatProgressContent`/`UsageReportContent` this package projects
- [Architecture](architecture.md) — the channel merge, the ambient scope tree, and the usage report shape this package mirrors
- [MCP tool tracking](mcp.md) and [Agent tool tracking](agents.md) — the satellites that populate `ToolKind.McpTool`/`ToolKind.Agent` and the composed headers this package's `DisplayName`/`Kind` split replaces for UI rendering
- [Example: the UI contract, three ways](examples/ui-contract.md) — a runnable SSE producer, Blazor WebAssembly consumer, and TypeScript SPA sharing this contract
- [`System.Text.Json` source generation](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation) — the basis for `AssistantUiJsonContext`
- [Server-sent events (MDN)](https://developer.mozilla.org/en-US/docs/Web/API/Server-sent_events) and [ASP.NET Core Blazor hosting models](https://learn.microsoft.com/aspnet/core/blazor/hosting-models)
