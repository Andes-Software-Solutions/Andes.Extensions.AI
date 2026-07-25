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

First-class MCP support ships as a satellite package so the core stays dependency-lean:

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

Microsoft Agent Framework agents run as tracked tools through their own satellite package:

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

See [Agent support](docs/agents.md) for details.

## Documentation

- [Getting started](docs/getting-started.md)
- [Architecture](docs/architecture.md)
- [MCP support](docs/mcp.md)
- [Agent support](docs/agents.md)
- [Example: the Progress Board — every tool kind in one stream](docs/examples/progress-board.md)

## License

MIT
