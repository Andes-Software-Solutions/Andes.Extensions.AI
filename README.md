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

## Documentation

- [Getting started](docs/getting-started.md)
- [Architecture](docs/architecture.md)

## Roadmap

First-class classification for **MCP tools** (`Calling {Server} MCP`) and **Microsoft Agent Framework agents as tools** (`Calling {Agent} Agent`) — the `ToolClassifier` / `HeaderFormatter` hooks and `ToolKind` reserve the space today.

## License

MIT
