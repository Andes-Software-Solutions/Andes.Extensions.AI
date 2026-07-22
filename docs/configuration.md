# Configuration

Everything the tool-tracking middleware does is controlled through `ToolTrackingOptions` plus a small set of helpers: tool annotation extensions that tell the classifier what a tool is, a per-request correlation tag, and three `UseToolTracking` registration overloads for DI and non-DI hosts. This document covers every option with its default and an example, template customization and localization, manual tool annotation, and the middleware's logging and privacy posture.

## `ToolTrackingOptions` at a glance

| Member | Type | Default | Purpose |
| --- | --- | --- | --- |
| `EnableInBandStatusUpdates` | `bool` | `true` | Inject `ActivityStatusContent` items into root streaming responses. |
| `EnableFinalUsageUpdate` | `bool` | `true` | Append a final synthetic update carrying the request's total usage to streaming responses. |
| `Templates` | `ActivityStatusTemplates` | see below | Display strings for status headers/subheaders (get-only; mutate its properties). |
| `ArgumentLogging` | `ToolArgumentLogging` | `None` | Whether tool-call arguments are written to structured logs. |
| `IncludeErrorMessages` | `bool` | `false` | Whether failure events carry the exception message in `ChatActivityEvent.ErrorMessage`; off by default because exception messages can echo argument values or user input. `ErrorType` is always populated. |
| `EnableMcpProgress` | `bool` | `true` | Bridge MCP servers' progress notifications into `StatusReported` events automatically. |
| `TimeProvider` | `TimeProvider` | `TimeProvider.System` | Clock for timestamps and durations; override in tests. |
| `DefaultMcpServerName` | `string` | `"MCP"` | Display name for MCP tools whose server name could not be resolved. |
| `McpServerNameResolver` | `Func<AIFunction, string?>?` | `null` | Resolves the MCP server display name for unannotated MCP tools. |
| `AgentNameResolver` | `Func<AIFunction, string?>?` | `null` | Identifies unannotated agent tools and resolves the agent display name. |

> [!NOTE]
> **Options are snapshotted.** `ToolTrackingChatClient` clones the options at construction time, so mutating an options instance after the pipeline is built has no effect on that client.

### `EnableInBandStatusUpdates`

Turn off to keep the response stream pristine (observer-only hosting):

```csharp
builder.Services.AddChatActivityTracking(options =>
{
    options.EnableInBandStatusUpdates = false;
});
```

Out-of-band observer events are unaffected. Nested (joined) requests never receive in-band updates regardless of this setting.

### `EnableFinalUsageUpdate`

Controls only the final `RequestCompleted` update with total usage at the end of a root stream; independent of `EnableInBandStatusUpdates`:

```csharp
options.EnableFinalUsageUpdate = false; // no trailing usage update
```

## Templates — display strings

`Templates` holds six `string.Format`-style patterns (formatted with `CultureInfo.CurrentCulture`). Defaults and placeholders:

| Property | Default | `{0}` is |
| --- | --- | --- |
| `PlainToolHeader` | `Calling Tool(s)` | — |
| `PlainToolSubheader` | `Called {0}` | tool name |
| `AgentHeader` | `Calling {0} Agent` | agent name |
| `AgentSubheader` | `Calling {0}` | nested tool or method name |
| `McpHeader` | `Calling {0} MCP` | MCP server name |
| `McpSubheader` | `Called {0}` | tool name |

Simple rewording:

```csharp
builder.Services.AddChatActivityTracking(options =>
{
    options.Templates.PlainToolHeader = "Working…";
    options.Templates.PlainToolSubheader = "Running {0}";
    options.Templates.AgentHeader = "Delegating to {0}";
});
```

### Full control and localization: `FormatHeader` / `FormatSubheader`

The two delegates receive the in-progress `ChatActivityEvent` (with `ToolKind`, `ToolName`, `SourceName`, `ActivityTag`, …) and **take precedence over the string templates entirely** — including the built-in nested-under-agent rendering rule. Returning `null` suppresses that line.

```csharp
options.Templates.FormatHeader = e => e.ToolKind switch
{
    ToolKind.Agent => string.Format(Resources.CallingAgent, e.SourceName), // localized
    ToolKind.Mcp => string.Format(Resources.CallingMcp, e.SourceName),
    _ => Resources.CallingTools,
};

options.Templates.FormatSubheader = e =>
    e.ToolKind == ToolKind.Agent ? null : string.Format(Resources.CalledTool, e.ToolName);
```

Because the delegates run per event, they can localize per request (e.g. keying off a culture carried in `ActivityTag`).

## Tool annotation

The classifier decides each tool's `ToolKind` and source display name in this order:

1. **Explicit annotations** (the `TrackingToolAnnotations` keys) — always win.
2. **MCP type detection** — `McpClientTool` instances are recognized as `ToolKind.Mcp` automatically.
3. **`AgentNameResolver`** — your heuristic for unannotated agent tools.
4. Otherwise the tool is a plain `ToolKind.Function`.

### MCP tools: `WithTrackingMetadata`

`McpClientTool` does not expose its owning server name publicly, so an unannotated MCP tool is still detected as MCP but renders with `DefaultMcpServerName` (`"Calling MCP MCP"` by default — annotate to avoid this). Three overloads:

```csharp
using Enterprise.AI.Middleware.Tracking;
using ModelContextProtocol.Client;

await using McpClient mcpClient = await McpClient.CreateAsync(transport);
IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();

// Whole list, server name from the client's ServerInfo (Title, falling back to Name):
IList<AITool> tracked = mcpTools.WithTrackingMetadata(mcpClient);

// Single tool, name from the client:
AIFunction one = mcpTools[0].WithTrackingMetadata(mcpClient);

// Single tool, explicit display name:
AIFunction named = mcpTools[0].WithTrackingMetadata("GitHub");

var options = new ChatOptions { Tools = tracked };
```

Each helper returns a wrapper that invokes the original tool unchanged.

### Agent tools: `AsTrackedAIFunction`

Converts a Microsoft Agent Framework `AIAgent` into a function tool (equivalent to `agent.AsAIFunction()`) annotated with `ToolKind.Agent` and the agent's display name (`agent.Name`, falling back to the function name):

```csharp
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Agents.AI;

var researcher = new ChatClientAgent(
    agentClient, // ideally itself a tracked pipeline — nested usage then rolls up
    instructions: "You are a research specialist. Answer concisely.",
    name: "Researcher");

var options = new ChatOptions
{
    Tools = [researcher.AsTrackedAIFunction()],
};
```

Optional parameters: `AIFunctionFactoryOptions? options` to customize the function representation, and `AgentSession? session` to reuse a session across invocations (a new session is created per call when omitted).

### Manual annotation with raw keys

The `TrackingToolAnnotations` keys are public so you can annotate any `AIFunction` without the helpers — useful for home-grown agent abstractions:

```csharp
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

AIFunction agentTool = AIFunctionFactory.Create(
    RunResearchAsync,
    new AIFunctionFactoryOptions
    {
        Name = "researcher_agent",
        AdditionalProperties = new Dictionary<string, object?>
        {
            [TrackingToolAnnotations.ToolKindKey] = ToolKind.Agent,   // or the string "Agent"
            [TrackingToolAnnotations.SourceNameKey] = "Researcher",
        },
    });
```

| Key constant | Value | Read from |
| --- | --- | --- |
| `TrackingToolAnnotations.ToolKindKey` (`enterprise.ai.tracking.toolKind`) | A `ToolKind` value or its string name (case-insensitive) | `AITool.AdditionalProperties` |
| `TrackingToolAnnotations.SourceNameKey` (`enterprise.ai.tracking.sourceName`) | Display name of the tool's source (agent or MCP server) | `AITool.AdditionalProperties` |
| `TrackingToolAnnotations.ActivityReportKey` (`enterprise.ai.tracking.activityReport`) | The attached `ChatActivityReport` | `ChatResponse.AdditionalProperties` (prefer `ChatActivityReport.FromResponse`) |
| `TrackingToolAnnotations.ActivityTagKey` (`enterprise.ai.tracking.activityTag`) | The correlation tag | `ChatOptions.AdditionalProperties` (prefer `WithActivityTag`) |

When an annotated kind lacks a source name, the middleware falls back per kind: MCP → `McpServerNameResolver` then `DefaultMcpServerName`; Agent → `AgentNameResolver` then the function name.

### Resolvers

For fleets of tools you cannot annotate individually:

```csharp
builder.Services.AddChatActivityTracking(options =>
{
    // Name unannotated MCP tools by convention:
    options.McpServerNameResolver = f => f.Name.StartsWith("gh_", StringComparison.Ordinal)
        ? "GitHub"
        : null; // null → fall back to DefaultMcpServerName

    // Detect agents by naming convention; non-null return classifies as ToolKind.Agent:
    options.AgentNameResolver = f => f.Name.EndsWith("_agent", StringComparison.Ordinal)
        ? f.Name[..^"_agent".Length]
        : null; // null → leave classification unchanged
});
```

## Registration: DI vs explicit construction

### With dependency injection

`AddChatActivityTracking` registers the options (via the standard options pattern); `UseToolTracking()` then resolves `IOptions<ToolTrackingOptions>`, `ILoggerFactory`, and **every** registered `IChatActivityObserver` from the container:

```csharp
builder.Services.AddChatActivityTracking(options =>
{
    options.Templates.AgentHeader = "Delegating to {0}";
});
builder.Services.AddSingleton<IChatActivityObserver, SignalRActivityObserver>();
builder.Services.AddSingleton<IChatActivityObserver, MetricsActivityObserver>();

builder.Services.AddChatClient(services => providerClient
    .AsBuilder()
    .UseToolTracking()          // before UseFunctionInvocation — see the warning in Getting Started
    .UseFunctionInvocation()
    .Build(services));
```

With more than one observer, events fan out through a composite that isolates each observer's failures.

### Without dependency injection

Two explicit overloads:

```csharp
// Inline configuration; no observer, no logging:
IChatClient client = new ChatClientBuilder(providerClient)
    .UseToolTracking(options => options.EnableFinalUsageUpdate = false)
    .UseFunctionInvocation()
    .Build();

// Explicit options, observer, and logging:
IChatClient client2 = new ChatClientBuilder(providerClient)
    .UseToolTracking(new ToolTrackingOptions(), myObserver, loggerFactory)
    .UseFunctionInvocation()
    .Build();
```

## `WithActivityTag` — per-request correlation

Stamps an opaque tag on a request that is copied onto **every** `ChatActivityEvent` it produces — the standard way to route observer events back to a specific SignalR connection, tenant, or trace:

```csharp
ChatOptions options = new ChatOptions
{
    Tools = tools,
}.WithActivityTag(Context.ConnectionId); // e.g. inside a SignalR hub method
```

## `EnableMcpProgress`

When an MCP tool is invoked, the middleware adds a per-invocation progress token to the call and converts every progress notification the server sends into a `ChatActivityEventKind.StatusReported` event (header `"Calling {Server} MCP"`, `Progress`/`ProgressTotal` populated) — on both the observer and the in-band stream. Set to `false` to disable the bridge:

```csharp
options.EnableMcpProgress = false;
```

The bridge activates only for tools whose function is an `McpClientTool`, directly or annotated via `WithTrackingMetadata`; MCP tools inside your own `DelegatingAIFunction` wrappers are invoked unchanged (no bridging, no bypassed behavior). Tools can also report status explicitly from any tool body — see `ChatActivityScope.ReportStatus` in [status-events.md](status-events.md#reporting-status-and-progress-from-inside-tools).

## `IncludeErrorMessages` — privacy

Failure events (`ToolCallFailed`, `RequestFailed`) always carry the exception's full type name in `ErrorType`. The exception *message* is withheld by default — messages from validation and argument exceptions routinely quote the offending values, which would leak the very data `ArgumentLogging = None` withholds. Opt in only when your observers and stream consumers are trusted:

```csharp
options.IncludeErrorMessages = true; // ChatActivityEvent.ErrorMessage now carries exception messages
```

## `ArgumentLogging` — privacy

Tool-call arguments frequently contain end-user input and therefore potential PII. The middleware's stance:

- Arguments are **never** placed on `ChatActivityEvent` or `ActivityStatusContent`, under any setting.
- Arguments are **never** logged by default (`ToolArgumentLogging.None`).
- `Redacted` logs argument **names only**; `Full` logs names and values verbatim — enable it only where the log sink is approved for such data.

```csharp
options.ArgumentLogging = ToolArgumentLogging.Redacted; // names only, Debug level
```

## `TimeProvider` — deterministic tests

All timestamps and durations flow through `TimeProvider`, so tests can inject a fake clock (e.g. `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`) and assert exact durations:

```csharp
var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero));
var options = new ToolTrackingOptions { TimeProvider = clock };

IChatClient client = new ChatClientBuilder(testInnerClient)
    .UseToolTracking(options, observer)
    .UseFunctionInvocation()
    .Build();
// advance clock inside a fake tool: clock.Advance(TimeSpan.FromSeconds(3));
```

## Logging reference

Logs are source-generated under the `Enterprise.AI.Middleware.Tracking.ToolTrackingChatClient` category (an `ILoggerFactory` must be supplied or resolvable, otherwise logging is a no-op). Messages carry identifiers, names, durations, and counts — never prompt content or PII; tool arguments appear only per `ArgumentLogging`.

| Event id | Level | Message |
| --- | --- | --- |
| 1 | Debug | Chat request `{RequestId}` started. |
| 2 | Debug | Chat request `{RequestId}` completed in `{DurationMs}` ms; total tokens: `{TotalTokens}`. |
| 3 | Warning | Chat request `{RequestId}` failed after `{DurationMs}` ms with `{ErrorType}`. |
| 4 | Information | Tool call started: `{ToolKind}` '`{ToolName}`' (scope `{ScopeId}`, request `{RequestId}`). |
| 5 | Information | Tool call completed: '`{ToolName}`' in `{DurationMs}` ms (scope `{ScopeId}`). |
| 6 | Error | Tool call failed: '`{ToolName}`' after `{DurationMs}` ms (scope `{ScopeId}`). |
| 7 | Debug | Tool call arguments for '`{ToolName}`': `{Arguments}`. *(only when `ArgumentLogging` ≠ `None`)* |
| 8 | Debug | Usage recorded for scope `{ScopeId}`: input `{InputTokens}`, output `{OutputTokens}`, total `{TotalTokens}` (model `{ModelId}`, provider `{ProviderName}`). |
| 9 | Warning | Activity observer `{ObserverType}` threw; the chat request is unaffected. |

## See also

- [Getting Started](getting-started.md) — the canonical pipeline and ordering warning.
- [Architecture](architecture.md) — how classification and wrapping happen per request.
- [Status Events](status-events.md) — what the templates render, kind by kind.
- [Usage Tracking](usage-tracking.md) — report semantics affected by these options.
