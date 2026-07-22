# Architecture

This document explains how `ToolTrackingChatClient` works internally: where it sits in the `Microsoft.Extensions.AI` pipeline, how it wraps tools, why streaming status updates arrive the moment a tool starts, and how the ambient `AsyncLocal` scope tree attributes nested agent activity to the right place. Understanding these mechanics explains the library's ordering requirement and its delivery guarantees.

## Pipeline position

`ToolTrackingChatClient` is a `DelegatingChatClient`. It must be the **outermost** tool-related middleware:

```text
Host application
   │  GetResponseAsync / GetStreamingResponseAsync
   ▼
ToolTrackingChatClient        ← wraps ChatOptions.Tools, owns the request's activity scope
   │
   ▼
FunctionInvokingChatClient    ← runs the tool loop, invoking the (already wrapped) tools
   │
   ▼
Provider IChatClient          ← e.g. Azure OpenAI; supplies ChatClientMetadata and usage
```

> [!WARNING]
> In `ChatClientBuilder`, the **first middleware registered is the outermost**. Always register `UseToolTracking()` before `UseFunctionInvocation()`:
>
> ```csharp
> IChatClient client = new ChatClientBuilder(providerClient)
>     .UseToolTracking()
>     .UseFunctionInvocation()
>     .Build(services);
> ```
>
> The tracker intercepts `ChatOptions.Tools` on the way in and substitutes instrumented wrappers; the `FunctionInvokingChatClient` then executes those wrappers. Reversed, function invocation runs the raw tools and the tracker sees nothing.

Provider identity flows upward, not downward: the middleware asks its inner client for `ChatClientMetadata` (once, cached) and stamps `ProviderName` and the default model id onto usage events, so reports name the real provider (e.g. Azure OpenAI) without any configuration.

## Per-request flow

Every call to `GetResponseAsync` or `GetStreamingResponseAsync` starts with the same preparation:

1. **Tool wrapping.** Each `AIFunction` in `ChatOptions.Tools` (that is not already wrapped) is classified and wrapped in an internal `TrackedAIFunction` — a `DelegatingAIFunction` that leaves invocation behavior untouched but opens a tool-call scope around `InvokeCoreAsync`. The original `ChatOptions` is cloned; the caller's instance is never mutated. Classification precedence:
   1. Explicit annotations under the `TrackingToolAnnotations` keys (written by the `WithTrackingMetadata` / `AsTrackedAIFunction` helpers, or by hand) always win.
   2. MCP tools are recognized by type (`McpClientTool` via `AIFunction.GetService`).
   3. `ToolTrackingOptions.AgentNameResolver`, if set, may identify the function as an agent.
   4. Otherwise the tool is a plain `ToolKind.Function`.
2. **Root or nested?** The middleware checks the ambient `AsyncLocal` flow (`ChatActivityScope`). If none exists, this is a **root** request: a fresh `ActivityContext` is created (request id, root scope, observer wiring) and a `RequestStarted` event is published. If a tracked flow is already ambient — for example an agent tool running its own tracked pipeline — the request **joins it**: a silent nested model-call scope is opened under the enclosing tool-call scope, no `RequestStarted` fires, and no in-band updates are injected into the nested stream.
3. **Tool invocation.** When the `FunctionInvokingChatClient` invokes a wrapped tool, `TrackedAIFunction` opens a tool-call scope under the current ambient scope, publishes `ToolCallStarted` (with formatted header/subheader and the provider's tool-call id), sets the ambient scope to itself for the duration of the call, and publishes `ToolCallCompleted` or `ToolCallFailed` with the measured duration when it finishes.
4. **Completion.** At the root, the context is completed exactly once: durations are finalized, the scope tree is rolled up into a `ChatActivityReport`, the terminal `RequestCompleted`/`RequestFailed` event fires, and the observer's `OnRequestCompleted` is called. Non-streaming responses additionally get the report attached to `ChatResponse.AdditionalProperties`.

## The channel merge design (streaming)

Streaming is where the design earns its keep. A naive implementation would wrap the inner stream in an `async` iterator and inject status updates between inner updates — but tool calls happen *inside* the inner stream's tool loop, while the pipeline is awaiting the next inner update. Status injected "between updates" would only surface after the tool finished, defeating the purpose.

Instead, the middleware merges two producers into one FIFO channel:

```text
                        ┌──────────────────────────────┐
 inner stream ────────► │ pump task                    │
 (provider updates,     │  · records UsageContent      │───┐
  incl. the tool loop)  │  · forwards every update     │   │
                        └──────────────────────────────┘   │   single unbounded
                                                           ▼   FIFO channel
 TrackedAIFunction ───► ActivityContext ── status ───► ▓▓▓▓▓▓▓▓ ───► consumer loop
 (ToolCallStarted /        updates                                   (yield return
  Completed / Failed)                                                 to the caller)
```

- A **pump task** enumerates the inner stream, records any `UsageContent` it sees, and writes each update to the channel.
- **Tool lifecycle events** are written to the *same channel* directly from the tool wrapper at the moment they happen — while the pump is still blocked inside the inner stream waiting for the tool result. That is why a `ToolCallStarted` status is observable by the caller *while the tool is still executing* (the test suite pins this down with a deliberately blocked tool).
- The **consumer loop** — the only part the caller's `await foreach` touches — just drains the channel in order and yields.

This design also sidesteps an `AsyncLocal` pitfall: `ExecutionContext` changes made inside an `async` iterator do not reliably flow across `yield return` boundaries to the consumer. Because the ambient scope is set and restored inside the pump task (a plain `async` method) and inside the tool wrapper — never across a `yield` — the `AsyncLocal` state stays contained and cannot leak into the caller's context.

The pump's lifetime is tied to the consumer's: if the caller abandons the enumeration early, the consumer's `finally` block cancels the pump, treats the abandonment as a cancellation failure, and still completes the request (a `RequestFailed` event and a final report are delivered). When the stream completes normally and `EnableFinalUsageUpdate` is on, one last synthetic update carrying the request's total usage is yielded after the channel drains.

## The ambient scope tree

Scopes form a tree per top-level request, maintained through a **static `AsyncLocal`** (`ChatActivityScope`). Inside a tool invocation the ambient scope is that tool's scope; inside a nested model call it is the nested scope. The public surface is read-only:

```csharp
ActivityScope? scope = ChatActivityScope.Current; // null outside a tracked request
// scope.Id, scope.ParentId, scope.ToolKind, scope.Name, scope.Depth
```

Because the ambient flow is static, the two middleware instances involved in a nested-agent setup can be — and usually are — **completely different objects** built into different pipelines. The inner pipeline still joins the outer request purely through the ambient flow; no shared wiring is required.

A worked example — a main pipeline exposes a Microsoft Agent Framework agent as a tool (via `AsTrackedAIFunction()`), and the agent's own pipeline is also tracked:

```text
Root request scope (depth 0)                              ← main pipeline, RequestStarted
└── Tool call: researcher_agent  (ToolKind.Agent, depth 1) ← "Calling Researcher Agent"
    └── Model call scope (depth 2)                         ← agent's tracked pipeline joined ambient request
        └── Tool call: lookup_database (depth 3)           ← "Calling Researcher Agent" / "Calling lookup_database"
```

Consequences of joining the ambient request:

- The nested pipeline's LLM usage lands under the **agent's tool-call scope** in the report, so the agent's true cost (its own model calls plus its tools) rolls up in one place. See [Usage Tracking](usage-tracking.md#attribution-for-nested-agents).
- The nested stream receives **no in-band injection** — only the root stream carries `ActivityStatusContent`, so the end user sees one coherent stream.
- Status strings for anything nested under an agent render under the agent's header ("Calling Researcher Agent" / "Calling lookup_database"). See [Status Events](status-events.md#rendering-under-an-agent).

## Ordering and delivery guarantees

- **Causal order.** Events on the request's execution path are published in the order the activities occurred: `RequestStarted` precedes any `ToolCallStarted`, a tool's `Started` precedes its `Completed`/`Failed`, and the terminal request event comes last. In-band status updates preserve the same order relative to the model's own updates through the single FIFO channel. The one exception is `StatusReported` events bridged from MCP progress notifications: they are dispatched from the MCP client's receive loop — on a different thread, concurrently with request-path events, and in no guaranteed order relative to them — so observers must be thread-safe.
- **Exactly-once completion.** `OnRequestCompleted` fires exactly once per top-level request — streaming or non-streaming, success, failure, cancellation, or abandoned enumeration; late callers get the same report instance, and no event (including a late MCP progress notification, which is dropped) is delivered after it. The report attached to a non-streaming response is the *same instance* the observer received.
- **Observer isolation.** Observers are invoked synchronously but defensively: any exception an observer throws is caught and logged (event id 9) and never affects the chat request. With multiple registered observers, a composite fans events out and isolates each observer, so one faulty observer cannot starve the others.
- **Failure transparency.** Tool exceptions still produce `ToolCallFailed` (with `ErrorType` and duration; `ErrorMessage` only when `ToolTrackingOptions.IncludeErrorMessages` is enabled) and then propagate normally — tracking never swallows or alters errors. Likewise a failed root request produces `RequestFailed` plus the final report before the exception reaches the caller.
- **Zero prompt exposure.** Events, status contents, and logs carry identifiers, names, durations, and token counts — never prompt content, tool arguments, or tool results (arguments can be opted into *logs only* via `ArgumentLogging`; see [Configuration](configuration.md#argumentlogging--privacy)).

## See also

- [Getting Started](getting-started.md) — install and the canonical pipeline.
- [Status Events](status-events.md) — full event/content reference and wire format.
- [Usage Tracking](usage-tracking.md) — report structure and attribution rules.
- [Configuration](configuration.md) — options, templates, and DI.
