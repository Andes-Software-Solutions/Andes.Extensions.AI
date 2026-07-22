# Getting Started with Enterprise.AI.Middleware

Enterprise.AI.Middleware is a NuGet package of composable [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) `IChatClient` middlewares for enterprise applications. Version 1.0.0 ships one middleware, `ToolTrackingChatClient`, which answers two questions your chat UI and your finance team keep asking: *"what is the assistant doing right now?"* and *"what did that request actually cost?"* It tracks every tool invocation — plain functions, Microsoft Agent Framework agents exposed as tools, and MCP tools — and surfaces hierarchical status updates plus granular token usage both **in-band** (as `ActivityStatusContent` items inside the streaming response) and **out-of-band** (through `IChatActivityObserver`, ideal for SignalR or SSE hosts).

## Requirements

- .NET 8.0 or .NET 10.0 (the package targets `net8.0` and `net10.0`).
- A `Microsoft.Extensions.AI` chat pipeline (the package builds on `Microsoft.Extensions.AI` 10.8.1, `Microsoft.Agents.AI` 1.14.0, and `ModelContextProtocol.Core` 1.4.1).

## Install

```bash
dotnet add package Enterprise.AI.Middleware
```

## Build the pipeline

The middleware is a standard `DelegatingChatClient` that you register on a `ChatClientBuilder`.

> [!WARNING]
> **Ordering is mandatory: register `UseToolTracking()` *before* `UseFunctionInvocation()`.**
> In `ChatClientBuilder`, the first middleware registered is the **outermost**. The tracker must sit outside the `FunctionInvokingChatClient` so it can wrap the tool list *before* function invocation executes it. If you reverse the order, tools are invoked unwrapped and no tool activity is tracked.

With dependency injection (recommended for hosts):

```csharp
using Azure.AI.OpenAI;
using Azure.Identity;
using Enterprise.AI.Middleware;
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

IChatClient providerClient = new AzureOpenAIClient(
        new Uri(builder.Configuration["AzureOpenAI:Endpoint"]!),
        new DefaultAzureCredential())
    .GetChatClient(builder.Configuration["AzureOpenAI:Deployment"]!)
    .AsIChatClient();

// Registers ToolTrackingOptions; add any number of IChatActivityObserver singletons.
builder.Services.AddChatActivityTracking();
builder.Services.AddSingleton<IChatActivityObserver, SignalRActivityObserver>();

builder.Services.AddChatClient(services => providerClient
    .AsBuilder()
    .UseToolTracking()          // 1. outermost: tracks tools + usage
    .UseFunctionInvocation()    // 2. executes the (already wrapped) tools
    .Build(services));
```

Without dependency injection:

```csharp
using Enterprise.AI.Middleware;
using Microsoft.Extensions.AI;

IChatClient client = new ChatClientBuilder(providerClient)
    .UseToolTracking()
    .UseFunctionInvocation()
    .Build();
```

All configuration is optional — the defaults emit in-band status updates and a final usage update. See [Configuration](configuration.md) for every knob.

## Consume in-band status from a stream

While streaming, the middleware injects `ActivityStatusContent` items into `ChatResponseUpdate.Contents` the moment a tool starts — not after it finishes — alongside the model's own content. Filter for them with `OfType<ActivityStatusContent>()`:

```csharp
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

var options = new ChatOptions
{
    Tools = [AIFunctionFactory.Create(GetWeather)],
};

await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
    [new ChatMessage(ChatRole.User, "What's the weather in Amsterdam?")],
    options))
{
    foreach (ActivityStatusContent status in update.Contents.OfType<ActivityStatusContent>())
    {
        // e.g. [ToolCallStarted] Calling Tool(s) — Called GetWeather
        Console.WriteLine($"[{status.Kind}] {status.Header} — {status.Subheader}");

        if (status.Kind == ChatActivityEventKind.RequestCompleted)
        {
            Console.WriteLine($"Total tokens: {status.Usage?.TotalTokenCount}");
        }
    }

    Console.Write(update.Text);
}
```

Two things to know when you forward these updates to a browser or echo them back into history:

- To serialize updates as JSON (e.g. over SSE), register the content type once: `new JsonSerializerOptions(AIJsonUtilities.DefaultOptions).AddActivityStatusContent()`. See [Status Events](status-events.md#json-serialization) for the wire format and a full SSE recipe.
- Before appending a streamed assistant response back into the conversation history, strip the synthetic items with `messages.RemoveActivityContent()` so they are never sent to the model. See [Status Events](status-events.md#keep-status-out-of-the-conversation-history).

## Observe events out-of-band

Implement `IChatActivityObserver` to receive every activity event and the final report, independent of the response stream. Observers are called synchronously on the request path, so keep them fast and non-blocking; exceptions they throw are caught and logged and never break the chat request.

A minimal observer that forwards status to a SignalR hub:

```csharp
using Enterprise.AI.Middleware.Tracking;
using Microsoft.AspNetCore.SignalR;

public sealed class SignalRActivityObserver : IChatActivityObserver
{
    private readonly IHubContext<ChatHub> _hubContext;

    public SignalRActivityObserver(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public void OnActivityEvent(ChatActivityEvent activityEvent)
    {
        // WithActivityTag stamped the SignalR connection id on the request (see below),
        // so the event can be routed back to the right client. Fire-and-forget keeps the
        // observer non-blocking.
        if (activityEvent.ActivityTag is { } connectionId)
        {
            _ = _hubContext.Clients.Client(connectionId).SendAsync("activity", new
            {
                kind = activityEvent.Kind.ToString(),
                header = activityEvent.Header,
                subheader = activityEvent.Subheader,
                scopeId = activityEvent.ScopeId,
                parentScopeId = activityEvent.ParentScopeId,
            });
        }
    }

    public void OnRequestCompleted(ChatActivityReport report)
    {
        // Exactly once per top-level request — streaming or not, success or failure.
        _ = _hubContext.Clients.All.SendAsync("usage", new
        {
            requestId = report.RequestId,
            totalTokens = report.TotalUsage.TotalTokenCount,
            durationMs = report.Duration.TotalMilliseconds,
        });
    }
}
```

Stamp the correlation tag per request so the observer knows where to route events:

```csharp
ChatOptions options = new ChatOptions
{
    Tools = tools,
}.WithActivityTag(connectionId); // copied onto every ChatActivityEvent
```

Register the observer as a singleton (shown in the pipeline snippet above). Any number of observers can be registered; each is isolated from the others' failures.

## Read the usage report

`ChatActivityReport` aggregates the whole request: a scope tree with per-scope token usage (`OwnUsage` vs `TotalUsage`), wall-clock durations, and per-model rollups. It is delivered three ways:

1. **Observer** — `OnRequestCompleted(report)` fires for both streaming and non-streaming requests. This is the canonical delivery path.
2. **Non-streaming response** — the same report instance is attached to `ChatResponse.AdditionalProperties`; read it with `ChatActivityReport.FromResponse`:

   ```csharp
   ChatResponse response = await client.GetResponseAsync(messages, options);

   ChatActivityReport? report = ChatActivityReport.FromResponse(response);
   if (report is not null)
   {
       Console.WriteLine($"Request {report.RequestId} took {report.Duration.TotalSeconds:F1}s");
       foreach (ModelUsageBreakdown model in report.UsageByModel)
       {
           Console.WriteLine($"  {model.ProviderName}/{model.ModelId}: {model.TotalTokenCount} tokens");
       }
   }
   ```

   `FromResponse` returns `null` when the response did not pass through the tracking middleware.
3. **Streaming, in-band** — a final synthetic update with `Kind == ChatActivityEventKind.RequestCompleted` carries the request's total usage (opt out via `ToolTrackingOptions.EnableFinalUsageUpdate`).

Null token counts throughout the report mean *the provider did not report a value* — never zero. See [Usage Tracking](usage-tracking.md) for the full semantics.

## Report progress from inside tools

Long-running tools can push custom status lines into both channels while they execute — no extra wiring, at any nesting depth:

```csharp
using Enterprise.AI.Middleware.Tracking;

AIFunction tool = AIFunctionFactory.Create(async (string query) =>
{
    ChatActivityScope.ReportStatus("Searching archives", progress: 2, progressTotal: 5);
    return await SearchAsync(query);
}, "search_archives");
```

Each call produces a `StatusReported` event/update whose header matches the executing context (`"Calling Tool(s)"`, `"Calling {Agent} Agent"`, or `"Calling {Server} MCP"`) with your message as the subheader. Outside a tracked request the call is a harmless no-op.

**MCP tools get this for free**: progress notifications sent by an MCP server are bridged into the same `StatusReported` events automatically, including numeric `Progress`/`ProgressTotal` for progress bars (opt out with `ToolTrackingOptions.EnableMcpProgress = false`). See [Status Events](status-events.md#reporting-status-and-progress-from-inside-tools) for details and caveats.

## Running the library's tests

Unit tests (`tests/Enterprise.AI.Middleware.Tests`) run with plain `dotnet test` and need no external services — they use a scripted chat client and an in-process MCP server. The integration tests (`tests/Enterprise.AI.Middleware.IntegrationTests`) exercise real Azure OpenAI, MCP (via the in-repo stdio server `tests/Enterprise.AI.Middleware.TestMcpServer`), and Microsoft Agent Framework wiring; they automatically skip unless these environment variables are set:

| Variable | Example |
| --- | --- |
| `AZURE_OPENAI_ENDPOINT` | `https://my-resource.openai.azure.com` |
| `AZURE_OPENAI_API_KEY` | an API key for the resource |
| `AZURE_OPENAI_DEPLOYMENT` | `gpt-4o-mini` |

## Where to next

- [Architecture](architecture.md) — how the middleware works: pipeline position, the channel merge design, and the ambient scope tree.
- [Status Events](status-events.md) — event and content reference, JSON wire format, SSE recipe, and default display strings.
- [Usage Tracking](usage-tracking.md) — what gets counted where, `OwnUsage` vs `TotalUsage`, per-model rollups, and known limits.
- [Configuration](configuration.md) — every option, template customization, tool annotation helpers, and DI patterns.
