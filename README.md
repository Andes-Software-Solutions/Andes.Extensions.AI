# Enterprise.AI.Middleware

Composable **[Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai)** `IChatClient` middlewares for enterprise applications, shipped as a single NuGet package.

The first middleware, **tool tracking**, answers two questions every AI assistant UI eventually asks:

1. **"What is the assistant doing right now?"** — hierarchical, human-readable status updates streamed the moment they happen: `Calling Tool(s)` → `Called search_document`, `Calling Researcher Agent` → `Calling lookup_database`, `Calling GitHub MCP` → `Called search_issues`.
2. **"What did it cost?"** — granular token usage (input / output / total) per request *and per tool call*, with the model id and provider name (Azure OpenAI, Amazon Bedrock, Google Vertex AI, …) — including LLM calls made *inside* agents that are exposed as tools.

It works with any `IChatClient` and understands all three kinds of tools:

| Tool kind | How it's built | Default status header | Default subheader |
| --- | --- | --- | --- |
| Plain function | `AIFunctionFactory.Create(...)` | `Calling Tool(s)` | `Called {tool}` |
| Agent as tool ([Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/)) | `agent.AsTrackedAIFunction()` | `Calling {Agent} Agent` | `Calling {method}` (from the agent's own nested activity) |
| MCP tool ([MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)) | `mcpTools.WithTrackingMetadata(mcpClient)` | `Calling {Server} MCP` | `Called {tool}` |

Everything is surfaced on **two channels at once**:

- **In-band** — `ActivityStatusContent` (a custom `AIContent`) injected into the `ChatResponseUpdate` stream, so any streaming consumer renders live status inline, plus a final update carrying the request's total usage.
- **Out-of-band** — an injectable `IChatActivityObserver` receiving every `ChatActivityEvent` and the final `ChatActivityReport`, ready to fan out over SignalR or SSE.

Tools can push their own progress into both channels while they run — explicitly via `ChatActivityScope.ReportStatus("Indexing", progress: 3, progressTotal: 12)` from any tool body, and automatically for MCP tools, whose server progress notifications are bridged into the same status events with numeric progress for progress bars.

## Install

```bash
dotnet add package Enterprise.AI.Middleware
```

Targets **net8.0** and **net10.0**. Built on Microsoft.Extensions.AI 10.8, Microsoft.Agents.AI 1.14, and the official MCP C# SDK (`ModelContextProtocol.Core` 1.4).

## Quick start

> **Ordering matters:** register `UseToolTracking()` **before** `UseFunctionInvocation()`. The tracker must wrap the tool list that the function-invoking client executes.

```csharp
using Enterprise.AI.Middleware;
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

IChatClient client = new ChatClientBuilder(providerClient) // e.g. AzureOpenAIClient...AsIChatClient()
    .UseToolTracking()
    .UseFunctionInvocation()
    .Build(serviceProvider);

await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(messages, options))
{
    foreach (ActivityStatusContent status in update.Contents.OfType<ActivityStatusContent>())
    {
        Console.WriteLine($"[{status.Kind}] {status.Header} — {status.Subheader}");
    }

    Console.Write(update.Text);
}
```

With dependency injection:

```csharp
builder.Services.AddChatActivityTracking(options =>
{
    options.Templates.AgentHeader = "Delegating to {0}";
});
builder.Services.AddSingleton<IChatActivityObserver, SignalRActivityObserver>();
builder.Services.AddChatClient(services => providerClient
    .AsBuilder()
    .UseToolTracking()
    .UseFunctionInvocation()
    .Build(services));
```

Registering agents and MCP tools for tracking:

```csharp
// Microsoft Agent Framework agent as a tool — tracked as "Calling Researcher Agent"
AIFunction researcherTool = researcherAgent.AsTrackedAIFunction();

// MCP tools — tracked as "Calling GitHub MCP" (server name comes from the client)
IList<AITool> mcpTools = (await mcpClient.ListToolsAsync()).WithTrackingMetadata(mcpClient);

var chatOptions = new ChatOptions { Tools = [researcherTool, .. mcpTools] };
```

Reading the usage report:

```csharp
ChatResponse response = await client.GetResponseAsync(messages, chatOptions);
ChatActivityReport report = ChatActivityReport.FromResponse(response)!;

Console.WriteLine($"Total: {report.TotalUsage.TotalTokenCount} tokens in {report.Duration.TotalSeconds:F1}s");
foreach (ModelUsageBreakdown model in report.UsageByModel)
{
    Console.WriteLine($"  {model.ProviderName}/{model.ModelId}: {model.InputTokenCount} in / {model.OutputTokenCount} out");
}
foreach (ActivityScopeReport tool in report.Root.Children)
{
    Console.WriteLine($"  {tool.ToolKind} {tool.ToolName}: {tool.TotalUsage.TotalTokenCount} tokens in {tool.Duration.TotalMilliseconds:F0} ms");
}
```

Nested attribution: when an agent tool runs its own tracked pipeline, its LLM calls are automatically attributed to the agent's tool-call scope — `report.Root.Children[agent].TotalUsage` tells you exactly what that delegation cost.

## Documentation

| Doc | Covers |
| --- | --- |
| [Getting started](docs/getting-started.md) | Install, pipeline setup, consuming both status surfaces |
| [Architecture](docs/architecture.md) | Pipeline topology, channel merge design, ambient scope tree, guarantees |
| [Status events](docs/status-events.md) | `ChatActivityEvent` / `ActivityStatusContent` reference, JSON shape, SSE recipe |
| [Usage tracking](docs/usage-tracking.md) | What is counted where, `OwnUsage` vs `TotalUsage`, attribution rules, limits |
| [Configuration](docs/configuration.md) | Every option, templates and localization, annotation helpers, logging privacy |

## Tests

```bash
dotnet test                                   # unit tests — no network needed
```

Integration tests run against Azure OpenAI and auto-skip unless these environment variables are set:

```bash
AZURE_OPENAI_ENDPOINT     # https://<resource>.openai.azure.com
AZURE_OPENAI_API_KEY      # resource API key
AZURE_OPENAI_DEPLOYMENT   # chat model deployment name, e.g. gpt-4o-mini
```

The MCP integration scenario spins up the in-repo `tests/Enterprise.AI.Middleware.TestMcpServer` over stdio — no Node.js or external servers required.

## Repository layout

```
Enterprise.AI.Middleware/          the NuGet package source
tests/
  Enterprise.AI.Middleware.Tests/            unit tests (scripted IChatClient, in-process MCP)
  Enterprise.AI.Middleware.IntegrationTests/ Azure OpenAI + MCP + Agent Framework, env-gated
  Enterprise.AI.Middleware.TestMcpServer/    stdio MCP server used by the integration tests
docs/                              developer documentation
```

## License

MIT
