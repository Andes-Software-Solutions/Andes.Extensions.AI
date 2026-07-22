# Status Events Reference

The tool-tracking middleware reports activity on two channels: **out-of-band** `ChatActivityEvent` records delivered synchronously to every `IChatActivityObserver`, and **in-band** `ActivityStatusContent` items injected into the root streaming response. This document is the reference for both — when each event kind fires, every field, the JSON wire format, an SSE passthrough recipe, and the default display strings per tool kind. Neither channel ever carries prompt content, tool arguments, or tool results, so both are safe to forward to client applications.

## Event kinds

`ChatActivityEventKind` identifies the lifecycle stage. Not every kind reaches both channels:

| Kind | Fires when | Observer | In-band (streaming) |
| --- | --- | --- | --- |
| `RequestStarted` | A top-level chat request starts. Nested (joined) requests do not fire it. | Yes | No |
| `ToolCallStarted` | A tracked tool begins executing — published at that moment, not after the tool returns. | Yes | Yes¹ |
| `ToolCallCompleted` | A tracked tool returned normally. Carries `Duration`. | Yes | Yes¹ |
| `ToolCallFailed` | A tracked tool threw. Carries `Duration` and `ErrorType` (`ErrorMessage` only when `IncludeErrorMessages` is enabled). | Yes | Yes¹ |
| `UsageReported` | The provider reported token usage for the current scope (each streaming `UsageContent`, or `ChatResponse.Usage` for non-streaming — including from nested tracked pipelines). Carries `Usage`, `ModelId`, `ProviderName`. | Yes | No |
| `RequestCompleted` | The top-level request completed successfully. Carries `Duration` and total `Usage`. | Yes | Yes, as the final synthetic update² |
| `RequestFailed` | The top-level request threw, was canceled, or its stream was abandoned. Carries `Duration` and `ErrorType` (`ErrorMessage` only when `IncludeErrorMessages` is enabled). | Yes | No |
| `StatusReported` | A tool reported custom status or progress while executing — explicitly via `ChatActivityScope.ReportStatus(...)`, or automatically from an MCP server's progress notifications (see [MCP progress](#reporting-status-and-progress-from-inside-tools)). Carries `Subheader` (the message), the scope's contextual `Header`, and optionally `Progress`/`ProgressTotal`. | Yes | Yes¹ |

¹ Only into the **root** stream, and only while `ToolTrackingOptions.EnableInBandStatusUpdates` is `true` (the default). Nested streams never receive in-band injection. Non-streaming requests have no in-band channel at all.

² Controlled independently by `ToolTrackingOptions.EnableFinalUsageUpdate` (default `true`). The final update carries the request's total usage and is always the last update in the stream.

## `ChatActivityEvent` fields

`ChatActivityEvent` is an immutable record published to `IChatActivityObserver.OnActivityEvent` in the order the activities occurred.

| Field | Type | Meaning |
| --- | --- | --- |
| `Kind` | `ChatActivityEventKind` | Lifecycle stage (required). |
| `RequestId` | `string` | Identifier of the top-level chat request this event belongs to (required). |
| `ScopeId` | `string` | Identifier of the activity scope that produced the event (required). |
| `ParentScopeId` | `string?` | Parent scope id, or `null` for the root scope. |
| `ToolKind` | `ToolKind?` | `Function`, `Agent`, or `Mcp`; `null` when the event is not tool-related. |
| `ToolName` | `string?` | Name of the tool involved; `null` when not tool-related. |
| `ToolCallId` | `string?` | Provider-assigned call id (matches `ToolCallContent.CallId`), when available. |
| `SourceName` | `string?` | Agent name for agent tools, MCP server name for MCP tools; `null` for plain functions. |
| `Header` | `string?` | Human-readable status header formatted from the templates, or `null` when suppressed. |
| `Subheader` | `string?` | Human-readable status subheader, or `null` when suppressed. |
| `Timestamp` | `DateTimeOffset` | When the activity occurred (required). |
| `Duration` | `TimeSpan?` | Elapsed time; populated on completion and failure events. |
| `Usage` | `UsageDetails?` | Token usage; populated on `UsageReported` and terminal request events. |
| `Progress` | `double?` | Numeric progress so far on `StatusReported` events (a percentage or completed-item count), or `null` when no numeric progress was supplied. |
| `ProgressTotal` | `double?` | Total progress required (the denominator for `Progress`), when known. |
| `ModelId` | `string?` | Model that produced the usage, when known. |
| `ProviderName` | `string?` | Provider that served the request (from the inner client's `ChatClientMetadata`), when known. |
| `ErrorType` | `string?` | Full exception type name on failure events. |
| `ErrorMessage` | `string?` | Exception message on failure events — populated only when `ToolTrackingOptions.IncludeErrorMessages` is `true` (default `false`), because exception messages can echo argument values or user input. |
| `ActivityTag` | `string?` | Caller-supplied correlation tag set via `ChatOptions.WithActivityTag(...)`; copied onto every event of the request. |

## `ActivityStatusContent` fields

`ActivityStatusContent` derives from `AIContent` and appears inside `ChatResponseUpdate.Contents` alongside the model's own content. Filter with `update.Contents.OfType<ActivityStatusContent>()`.

| Field | Type | Meaning |
| --- | --- | --- |
| `Kind` | `ChatActivityEventKind` | Lifecycle stage this update represents. |
| `ToolKind` | `ToolKind?` | Kind of tool involved, or `null` when not tool-related. |
| `Header` | `string?` | Status header, e.g. `"Calling Tool(s)"`. |
| `Subheader` | `string?` | Status subheader, e.g. `"Called search_document"`. |
| `ToolCallId` | `string?` | Provider-assigned tool call id, when available. |
| `ScopeId` | `string` | Scope that produced the update. |
| `ParentScopeId` | `string?` | Parent scope id, or `null` for the root scope. |
| `Timestamp` | `DateTimeOffset` | When the underlying activity occurred. |
| `Usage` | `UsageDetails?` | Token usage; populated on the final `RequestCompleted` update. |
| `Progress` | `double?` | Numeric progress so far on `StatusReported` updates, or `null`. |
| `ProgressTotal` | `double?` | Total progress required, when known. |

## Reporting status and progress from inside tools

Any tool body can publish a custom status line to both channels with `ChatActivityScope.ReportStatus`:

```csharp
AIFunction tool = AIFunctionFactory.Create(async (string query) =>
{
    ChatActivityScope.ReportStatus("Searching archives", progress: 2, progressTotal: 5);
    return await SearchAsync(query);
}, "search_archives");
```

The resulting `StatusReported` event uses the message as its `Subheader` and derives its `Header` from the executing scope: a tool running under an agent renders under `"Calling {Agent} Agent"`, an MCP tool under `"Calling {Server} MCP"`, and a plain tool under the `"Calling Tool(s)"` header. The call is a no-op when no tracked request is ambient, so tools remain usable outside tracked pipelines. This works at any nesting depth — a tool inside an agent's own tracked pipeline reports into the root request's stream and observer.

**MCP progress notifications** are bridged automatically (opt out with `ToolTrackingOptions.EnableMcpProgress = false`): when an MCP tool is invoked, the request carries a progress token and every `notifications/progress` the server sends becomes a `StatusReported` event with `Progress`/`ProgressTotal` populated and the server's `Message` (or `"{progress}/{total}"`) as the subheader. Two caveats:

- Notifications are dispatched asynchronously on the MCP client's receive loop — on a different thread than the request path, with no guaranteed order, so observers must be thread-safe. For a tool that returns almost instantly, trailing notifications can arrive after `ToolCallCompleted`, be dropped by the MCP client once the call completes, or be dropped by the middleware if the whole request already completed (best-effort: a notification racing completion may rarely arrive after `OnRequestCompleted`). Long-running tools (the intended use for progress) are unaffected.
- The bridge activates only for tools that are `McpClientTool` instances, directly or annotated via `WithTrackingMetadata`. An MCP tool wrapped in your own `DelegatingAIFunction` is invoked unchanged — custom behavior is never bypassed — but without progress bridging.

## JSON serialization

`AIContent` uses polymorphic serialization with a `$type` discriminator. Hosts must register `ActivityStatusContent` on their serializer options once, or serialization of updates containing status items will fail:

```csharp
using System.Text.Json;
using Enterprise.AI.Middleware;
using Microsoft.Extensions.AI;

JsonSerializerOptions jsonOptions = new(AIJsonUtilities.DefaultOptions);
jsonOptions.AddActivityStatusContent();
```

The discriminator id is the constant `EnterpriseAIJsonUtilities.ActivityStatusContentTypeId` (`"enterprise.ai.activityStatus"`). A serialized `ToolCallStarted` item looks like this (null properties are omitted):

```json
{
  "$type": "enterprise.ai.activityStatus",
  "kind": "ToolCallStarted",
  "toolKind": "Mcp",
  "header": "Calling GitHub MCP",
  "subheader": "Called search_issues",
  "toolCallId": "call_abc123",
  "scopeId": "d0f7f0b1c1a24a2e9d3b7c5e8f6a4b21",
  "parentScopeId": "5a1c9e7d3b2f4c8a9e6d1b0f7c3a5e42",
  "timestamp": "2026-07-21T12:00:00+00:00",
  "usage": {
    "inputTokenCount": 12,
    "outputTokenCount": 3,
    "totalTokenCount": 15
  }
}
```

And the final usage update:

```json
{
  "$type": "enterprise.ai.activityStatus",
  "kind": "RequestCompleted",
  "scopeId": "5a1c9e7d3b2f4c8a9e6d1b0f7c3a5e42",
  "timestamp": "2026-07-21T12:00:09+00:00",
  "usage": {
    "inputTokenCount": 512,
    "outputTokenCount": 128,
    "totalTokenCount": 640
  }
}
```

Registration is symmetric: the same options deserialize the `$type`-tagged JSON back into `ActivityStatusContent`, so a .NET client on the other end of the wire can round-trip updates.

## SSE passthrough recipe

Because status arrives in-band, an SSE endpoint needs no extra plumbing — serialize each update as it streams:

```csharp
using System.Text.Json;
using Enterprise.AI.Middleware;
using Microsoft.Extensions.AI;

app.MapGet("/chat/stream", async (
    HttpContext http,
    string prompt,
    IChatClient chatClient,
    CancellationToken cancellationToken) =>
{
    http.Response.ContentType = "text/event-stream";

    JsonSerializerOptions jsonOptions = new(AIJsonUtilities.DefaultOptions);
    jsonOptions.AddActivityStatusContent();

    await foreach (ChatResponseUpdate update in chatClient.GetStreamingResponseAsync(
        [new ChatMessage(ChatRole.User, prompt)],
        cancellationToken: cancellationToken))
    {
        string json = JsonSerializer.Serialize(update, jsonOptions);
        await http.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await http.Response.Body.FlushAsync(cancellationToken);
    }
});
```

The browser distinguishes status frames from text frames by the `$type` discriminator on the content items. Tool status frames arrive *while the tool is still running*, so a UI can show "Calling GitHub MCP…" in real time.

## Keep status out of the conversation history

`ActivityStatusContent` items are synthetic — they are for the user's eyes, not the model's. If you accumulate a streamed response and echo it back into the conversation history, strip them first with `ChatOptionsTrackingExtensions.RemoveActivityContent`:

```csharp
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

List<ChatResponseUpdate> updates = [];
await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(messages, options))
{
    updates.Add(update);
}

ChatResponse response = updates.ToChatResponse();
List<ChatMessage> history = [.. messages, .. response.Messages];
history.RemoveActivityContent(); // removes every ActivityStatusContent item in place
```

## Default display strings

Headers and subheaders are rendered from `ActivityStatusTemplates` (`ToolTrackingOptions.Templates`). The six string templates and their placeholders:

| Template | Default | `{0}` is |
| --- | --- | --- |
| `PlainToolHeader` | `Calling Tool(s)` | — (no placeholder) |
| `PlainToolSubheader` | `Called {0}` | tool name |
| `AgentHeader` | `Calling {0} Agent` | agent name |
| `AgentSubheader` | `Calling {0}` | nested tool or method name |
| `McpHeader` | `Calling {0} MCP` | MCP server name |
| `McpSubheader` | `Called {0}` | tool name |

What a `ToolCallStarted` event renders as, per tool kind (top level, not nested under an agent):

| Tool kind | Header | Subheader |
| --- | --- | --- |
| `Function` (plain `AIFunction`) | `Calling Tool(s)` | `Called {toolName}` — e.g. `Called search_document` |
| `Agent` (agent exposed as tool) | `Calling {agentName} Agent` — e.g. `Calling Researcher Agent` | *(none — `null` at start)* |
| `Mcp` (MCP tool) | `Calling {serverName} MCP` — e.g. `Calling GitHub MCP` | `Called {toolName}` — e.g. `Called search_issues` |

### Rendering under an agent

Any activity nested inside an agent tool — regardless of the nested tool's own kind — renders under the **enclosing agent's** header, using the agent templates:

- Header: `AgentHeader` formatted with the nearest enclosing agent's name → `Calling Researcher Agent`
- Subheader: `AgentSubheader` formatted with the nested tool's name → `Calling lookup_database`

So a full sequence for "main model delegates to the Researcher agent, which calls `lookup_database`" produces these `ToolCallStarted` status strings:

```text
1. Header: "Calling Researcher Agent"   Subheader: (none)                       ← agent tool starts
2. Header: "Calling Researcher Agent"   Subheader: "Calling lookup_database"    ← nested tool under the agent
```

A UI can group on `Header` (or better, on `ParentScopeId`) to show the agent's nested activity indented beneath it.

To reword, localize, or suppress any of these lines — including overriding the nested-under-agent rule — set the `FormatHeader` / `FormatSubheader` delegates, which take precedence over the string templates entirely. See [Configuration](configuration.md#templates--display-strings).

## See also

- [Getting Started](getting-started.md) — consuming these events end to end.
- [Architecture](architecture.md) — why in-band status is timely (the channel merge design).
- [Usage Tracking](usage-tracking.md) — the `UsageReported` pipeline and the final report.
- [Configuration](configuration.md) — template customization and toggles.
