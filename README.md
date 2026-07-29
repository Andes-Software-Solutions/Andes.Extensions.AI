# Andes.Extensions.AI

Middleware extensions for [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai): per-request and per-tool **token usage tracking**, and **streaming status propagation** for `IChatClient` pipelines.

Add one line to your pipeline and get:

- **Token usage tracking** — input/output/total tokens for the main assistant, per model turn, and attributed to each tool call (including LLM calls nested inside tools), rolled up into a `ChatUsageReport`.
- **Streaming progress statuses** — synthetic `ChatProgressContent` updates interleaved into the live stream so your UI can show "Calling GetWeather Tool", sub-statuses reported from inside the tool ("Extracting…", "Processing…"), and completion — while the model and tools are still working.
- **Out-of-band observers** — implement `IChatProgressObserver` to receive the same events and the final report without parsing the stream.
- **Privacy by default** — progress events never carry prompt content, tool arguments, or tool results unless explicitly opted in.

## Install

```shell
dotnet add package Andes.Extensions.AI
```

## Quickstart

Register the middleware **before** `UseFunctionInvocation()` — the tracker must wrap the tools that the function-invoking client executes:

```csharp
using Andes.Extensions.AI;
using Microsoft.Extensions.AI;

IChatClient client = innerClient          // any IChatClient (Azure OpenAI, OpenAI, Ollama, ...)
    .AsBuilder()
    .UseToolTracking()
    .UseFunctionInvocation()
    .Build();

AIFunction weather = AIFunctionFactory.Create(
    (string city) =>
    {
        ChatProgress.Report("Extracting...");   // sub-status under "Calling GetWeather Tool"
        return $"Sunny in {city}";
    },
    "GetWeather");

await foreach (var update in client.GetStreamingResponseAsync(
    "What's the weather in Quito?",
    new ChatOptions { Tools = [weather] }))
{
    foreach (var content in update.Contents)
    {
        switch (content)
        {
            case ChatProgressContent progress:
                Console.WriteLine($"[{progress.Progress.Kind}] {progress.Progress.Message}");
                break;
            case UsageReportContent usage:
                Console.WriteLine($"Total tokens: {usage.Report.TotalUsage.TotalTokenCount}");
                break;
        }
    }

    Console.Write(update.Text);
}
```

Tools that are themselves LLM-backed (for example, agents exposed as functions) can attribute their own usage to the calling scope with `ChatProgress.ReportUsage(...)` — or simply run their own `UseToolTracking()` pipeline, whose total rolls up automatically.

Before persisting responses into conversation history, remove the synthetic content:

```csharp
ChatResponse response = updates.ToChatResponse().StripProgressContent();
```

## MCP tools

First-class MCP support ships as a satellite package, [Andes.Extensions.AI.Mcp](https://www.nuget.org/packages/Andes.Extensions.AI.Mcp), so the core stays dependency-lean:

```shell
dotnet add package Andes.Extensions.AI.Mcp
```

`McpClientTool` instances classify as `ToolKind.McpTool` and render as `"Calling {Server} MCP"`, and the server's progress notifications are bridged into `ToolProgress` updates with numeric `Progress`/`ProgressTotal` values:

```csharp
IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();

IChatClient client = innerClient
    .AsBuilder()
    .UseToolTracking(options => options.UseMcpToolClassification())
    .UseFunctionInvocation()
    .Build();

var chatOptions = new ChatOptions { Tools = mcpTools.WithTracking(mcpClient) };
```

See [MCP support](docs/mcp.md) for details.

## Agent tools

Microsoft Agent Framework agents run as tracked tools through their own satellite package, [Andes.Extensions.AI.Agent](https://www.nuget.org/packages/Andes.Extensions.AI.Agent):

```shell
dotnet add package Andes.Extensions.AI.Agent
```

Agents wrapped with `WithTracking()` classify as `ToolKind.Agent` and render as `"Calling {Agent} Agent"`, and each run's `AgentResponse.Usage` is attributed to the calling tool's scope — a plain `agent.AsAIFunction()` exposes neither:

```csharp
AIAgent weatherAgent = weatherChatClient.AsAIAgent(
    instructions: "You answer questions about the weather.",
    name: "Weather Agent",
    tools: [AIFunctionFactory.Create(GetWeather)]);

IChatClient client = innerClient
    .AsBuilder()
    .UseToolTracking(options => options.UseAgentToolClassification())
    .UseFunctionInvocation()
    .Build();

var chatOptions = new ChatOptions { Tools = [weatherAgent.WithTracking()] };
```

Agents nest (v0.3): a `WithTracking`-wrapped agent used as a tool of another agent — or invoked directly inside a tool body — opens its own child scope, rendering live as a child activity card with its own statuses, duration, and token usage in the report:

```text
✓ Research Agent  agent  6.0s · 1,317 tok
  ├── Calling SearchNotes Tool
  ├── Searching notes…
  ├── Calling Packing_Agent Tool
  └── ✓ Packing Agent  agent  2.4s · 504 tok
      └── Checking essentials…
```

The same applies to nested MCP tools, and any tool can give a sub-operation its own child card with `ChatProgress.BeginToolScope(new ToolDescriptor { ... })`.

See [Agent support](docs/agents.md) for details.

## UI

A serializable status contract for streaming progress to a UI ships as its own satellite package, [Andes.Extensions.AI.UI](https://www.nuget.org/packages/Andes.Extensions.AI.UI) — a matching C# and TypeScript shape, so a Blazor app and a SPA render the same activity tree from the same JSON:

```shell
dotnet add package Andes.Extensions.AI.UI
```

Stream `AssistantStatusSnapshot` instead of parsing `ChatResponseUpdate` yourself. Each activity carries a clean `DisplayName` plus a separate `Kind` badge — never a composed "Calling … MCP/Agent/Tool" string, so the kind word is never repeated:

```csharp
await foreach (AssistantStatusSnapshot snapshot in client
    .GetStreamingResponseAsync("prompt", chatOptions)
    .ToStatusSnapshotsAsync())
{
    foreach (AssistantActivity activity in snapshot.Activities)
    {
        Console.WriteLine($"{activity.DisplayName} [{activity.Kind}] — {activity.State}");
    }
}
```

For an HTTP surface, stream `ToUiEventsAsync()` instead and serialize each event with the package's `AssistantUiJsonContext` over server-sent events; a browser or Blazor client folds them with the shipped TypeScript `foldAssistantEvents` or the C# `AssistantStatusReducer`. See [UI support](docs/ui.md) for details.

## Samples

`samples/Andes.Extensions.AI.Demo` is an interactive console chat that exercises all four packages in one tracked pipeline and renders live activity — function/MCP/agent cards, progress bars, token usage — Claude-Code-style with Spectre.Console:

```bash
cp samples/Andes.Extensions.AI.Demo/appsettings.sample.json samples/Andes.Extensions.AI.Demo/appsettings.json
# fill in the AzureOpenAI section, then:
dotnet run --project samples/Andes.Extensions.AI.Demo
```

See the [sample README](samples/Andes.Extensions.AI.Demo/README.md) for what each file demonstrates.

## Documentation

- [Getting started](docs/getting-started.md)
- [Architecture](docs/architecture.md)
- [MCP support](docs/mcp.md)
- [Agent support](docs/agents.md)
- [UI support](docs/ui.md)
- [Example: the Progress Board — every tool kind in one stream](docs/examples/progress-board.md)
- [Example: the UI contract, three ways](docs/examples/ui-contract.md)
- [Sample: the interactive demo console app](samples/Andes.Extensions.AI.Demo/README.md)
- [Release notes](releases/)

## License

MIT
