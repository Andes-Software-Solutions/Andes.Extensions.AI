# Getting Started

`Andes.Extensions.AI` adds **per-tool token usage tracking** and **streaming progress statuses** to any [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) `IChatClient` pipeline with one builder call. This guide walks from installation to a full Azure OpenAI sample. For the design behind it, see [Architecture](architecture.md).

## Prerequisites and installation

- .NET SDK **10.0** or later (the library targets `net10.0`).
- Any `IChatClient` implementation — Azure OpenAI, OpenAI, Ollama, or a fake for tests.

```shell
dotnet add package Andes.Extensions.AI
```

## Build the pipeline

> **Ordering invariant:** register `UseToolTracking()` **before** `UseFunctionInvocation()`. The tracker wraps the tools that the `FunctionInvokingChatClient` executes; if the order is reversed, tool calls are invisible to it.

Without dependency injection:

```csharp
using Andes.Extensions.AI;
using Microsoft.Extensions.AI;

IChatClient client = innerClient        // any IChatClient
    .AsBuilder()
    .UseToolTracking()
    .UseFunctionInvocation()
    .Build();
```

With dependency injection, pass the service provider to `ChatClientBuilder.Build(IServiceProvider)`. Every `IChatProgressObserver` registered in the container is picked up automatically and appended to `ToolTrackingOptions.Observers`:

```csharp
using Andes.Extensions.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton<IChatProgressObserver, ConsoleProgressObserver>();
using ServiceProvider provider = services.BuildServiceProvider();

IChatClient client = innerClient
    .AsBuilder()
    .UseToolTracking()
    .UseFunctionInvocation()
    .Build(provider);
```

In a hosted app, `services.AddChatClient(...)` returns a `ChatClientBuilder`, so the same two calls chain directly onto the registration and the host's provider is used at build time.

An observer receives every progress event out-of-band plus the final report, without parsing the stream:

```csharp
using Andes.Extensions.AI;

public sealed class ConsoleProgressObserver : IChatProgressObserver
{
    public void OnProgress(ChatProgressUpdate update)
    {
        Console.WriteLine($"[{update.Kind}] {update.Message}");
    }

    public void OnRequestCompleted(ChatUsageReport report)
    {
        Console.WriteLine($"Done: {report.TotalUsage.TotalTokenCount} tokens in {report.Duration.TotalSeconds:F1}s");
    }
}
```

Observers are invoked synchronously while the request runs: keep them fast and non-throwing (exceptions are swallowed so a faulty observer cannot corrupt the response).

Options are configured through the optional callback:

```csharp
.UseToolTracking(options =>
{
    options.IncludeToolArguments = true;               // opt-in; default false
    options.HeaderFormatter = d => $"Running {d.DisplayName}";
})
```

## Consume streaming progress

Progress arrives in-band as `ChatProgressContent` items inside the streamed updates, and the final report as a `UsageReportContent` item in the last update. Synthetic updates carry no `TextContent`, so `update.Text` accumulation is unaffected:

```csharp
await foreach (var update in client.GetStreamingResponseAsync(
    "What's the weather in Quito?",
    new ChatOptions { Tools = [weather] }))
{
    foreach (var content in update.Contents)
    {
        switch (content)
        {
            case ChatProgressContent { Progress: var progress }:
                var indent = new string(' ', progress.Depth * 2);
                Console.WriteLine($"{indent}[{progress.Kind}] {progress.Message}");
                break;

            case UsageReportContent { Report: var report }:
                Console.WriteLine($"Total tokens: {report.TotalUsage.TotalTokenCount}");
                break;
        }
    }

    Console.Write(update.Text);
}
```

Each `ChatProgressUpdate` carries the fields a UI needs to render a hierarchy without holding extra state:

- **`Kind`** — `RequestStarted`, `Thinking`, `ToolInvoking` (the header, e.g. "Calling GetWeather Tool"), `ToolProgress` (a sub-status), `ToolCompleted`, `ToolFailed`, `RequestCompleted`, and `RequestFailed` (out-of-band only: raised to observers when the request faults or is canceled, followed by `OnRequestCompleted` with a possibly partial report).
- **`ScopeId` / `ParentScopeId`** — `ToolProgress` events share the `ScopeId` of their owning tool call, so group sub-statuses under the header with the matching `ScopeId`; `ParentScopeId` links nested tool calls to their parent.
- **`Depth`** — 0 for request-level events, 1 for tool headers, 2 for sub-statuses under a header, deeper for nested tools; ideal for indentation.
- **`ToolName` / `ToolKind` / `ToolSource` / `CallId` / `Duration`** — for richer rendering and correlation with the model's function calls.
- **`Progress` / `ProgressTotal`** — optional numeric progress (`double?`) on `ToolProgress` events whose reporter supplied values, via the `ChatProgress.Report(status, progress, progressTotal)` overload or a bridged MCP progress notification (see [MCP tools](#mcp-tools)).

A typical rendering of the events for one tool call:

```text
[RequestStarted] Starting request
[Thinking] Thinking...
  [ToolInvoking] Calling GetWeather Tool
    [ToolProgress] Extracting...
  [ToolCompleted] GetWeather completed
[Thinking] Thinking...
[RequestCompleted] Request completed
```

## Report from inside a tool

Tools report sub-statuses and attribute token usage through the static `ChatProgress` ambient reporter — no changes to tool signatures:

```csharp
using Andes.Extensions.AI;
using Microsoft.Extensions.AI;

AIFunction weather = AIFunctionFactory.Create(
    (string city) =>
    {
        ChatProgress.Report("Extracting...");     // sub-status under the tool's header
        ChatProgress.ReportUsage(new UsageDetails { TotalTokenCount = 42 });  // e.g. from a nested SDK call
        return $"Sunny in {city}";
    },
    "GetWeather");
```

Both calls are **safe no-ops outside a tracked request**, so tools stay usable in untracked pipelines and plain unit tests. `ChatProgress.IsActive` tells you whether tracking is live.

Tools doing measurable work can attach numeric values to a sub-status with the `Report(status, progress, progressTotal)` overload; consumers read them back from `ChatProgressUpdate.Progress`/`ProgressTotal` on the resulting `ToolProgress` events:

```csharp
for (int i = 1; i <= pages.Count; i++)
{
    ChatProgress.Report($"Processing page {i} of {pages.Count}", i, pages.Count);
}
```

Like the other members, the overload is a safe no-op outside a tracked request, and `IChatProgressReporter` supplies it as a default interface member that forwards to `Report(status)`, so existing reporter implementations keep compiling unchanged.

Services that prefer an injectable dependency over statics can take an `IChatProgressReporter` and obtain it from `ChatProgress.Current`, which returns a bound reporter inside a tracked request and a no-op reporter otherwise:

```csharp
IChatProgressReporter reporter = ChatProgress.Current;
reporter.Report("Processing...");
```

Tools that run their own `UseToolTracking()` pipeline internally (for example, an agent exposed as a function) do not need `ReportUsage` at all: the nested pipeline's `TotalUsage` rolls up into the calling tool's scope automatically. If such a self-tracked agent is wrapped with the [`Andes.Extensions.AI.Agent` package](agents.md)'s `WithTracking`, pass `trackUsage: false` so its own usage capture does not attribute the same tokens twice.

## MCP tools

Model Context Protocol tools get first-class tracking through the satellite package:

```shell
dotnet add package Andes.Extensions.AI.Mcp
```

`UseMcpToolClassification()` renders MCP tools with the "Calling {Server} MCP" header and reports them as `ToolKind.McpTool`; `WithTracking(...)` carries the server's display name and bridges the server's progress notifications into `ToolProgress` updates with numeric `Progress`/`ProgressTotal` values:

```csharp
using Andes.Extensions.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

McpClient mcpClient = await McpClient.CreateAsync(transport);
IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();

IChatClient client = innerClient
    .AsBuilder()
    .UseToolTracking(options => options.UseMcpToolClassification())
    .UseFunctionInvocation() // tracking before function invocation (core invariant)
    .Build();

var chatOptions = new ChatOptions { Tools = mcpTools.WithTracking(mcpClient) };
```

See [MCP tool tracking](mcp.md) for the server-name precedence, how the progress bridge works, its ordering and threading caveats, and the `enableProgress` opt-out.

## Read the usage report

**Streaming** — the report is the last update's `UsageReportContent`, as shown above, and is also delivered to observers via `OnRequestCompleted`.

**Non-streaming** — `Turns` is empty (providers report one aggregate) and the report is attached to the response:

```csharp
ChatResponse response = await client.GetResponseAsync("prompt", new ChatOptions { Tools = [weather] });

if (response.AdditionalProperties?.TryGetValue(
        ToolTrackingChatClient.UsageReportPropertyName, out ChatUsageReport? report) is true)
{
    Console.WriteLine($"Assistant: {report.AssistantUsage.TotalTokenCount} tokens");
    foreach (ToolCallUsage call in report.ToolCalls)
    {
        Console.WriteLine($"  {call.ToolName}: {call.Usage?.TotalTokenCount ?? 0} tokens, " +
                          $"{call.Duration.TotalMilliseconds:F0} ms, succeeded={call.Succeeded}");
    }

    Console.WriteLine($"Total: {report.TotalUsage.TotalTokenCount} tokens ({report.ModelId}, {report.ProviderName})");
}
```

`ToolCallUsage.Usage` is `null` when nothing was attributed to that call; `Children` holds tool calls nested inside it, already included in the parent's rollup.

## Strip synthetic content before persisting history

`ChatProgressContent` and `UsageReportContent` are not part of the standard `AIContent` serialization contract. Remove them before persisting or serializing responses:

```csharp
ChatResponse response = updates.ToChatResponse().StripProgressContent();
```

`StripProgressContent()` is also available on individual `ChatMessage` instances. On a `ChatResponse` it additionally removes the `ChatUsageReport` attached under `ToolTrackingChatClient.UsageReportPropertyName`, so a stripped response round-trips through standard serialization. Non-streaming responses never contain synthetic content in their messages, but stripping them still clears the attached report before persistence.

## Options reference

All options live on `ToolTrackingOptions`, configured via the `UseToolTracking(options => ...)` callback.

| Option | Type | Default | Description |
| --- | --- | --- | --- |
| `EmitProgressContent` | `bool` | `true` | Emit synthetic `ChatProgressContent` updates into the streamed response. Observers are notified regardless of this setting. |
| `EmitUsageReportContent` | `bool` | `true` | Emit a final synthetic update carrying `UsageReportContent` at the end of the stream. |
| `AttachReportToResponse` | `bool` | `true` | For non-streaming calls, attach the `ChatUsageReport` to `ChatResponse.AdditionalProperties` under `ToolTrackingChatClient.UsageReportPropertyName`. |
| `IncludeToolArguments` | `bool` | `false` | Include stringified tool arguments in `ChatProgressUpdate.Arguments` on `ToolInvoking` events. Off by default: progress events never carry prompt content, tool arguments, or tool results unless explicitly opted in. |
| `Observers` | `IList<IChatProgressObserver>` | empty | Out-of-band observers notified of every progress event and request completion. DI-registered observers are appended automatically by `UseToolTracking`. |
| `ToolClassifier` | `Func<AITool, ToolDescriptor>?` | `null` | Classifies a tool into a `ToolDescriptor`. Default: `AIFunction` tools become `ToolKind.Function`, everything else `ToolKind.Unknown` — exposed as `ToolDescriptor.CreateDefault` for custom classifiers to fall back on. The [`Andes.Extensions.AI.Mcp` package](mcp.md) installs an MCP-aware classifier via `UseMcpToolClassification()`; the [`Andes.Extensions.AI.Agent` package](agents.md) installs an agent-aware classifier via `UseAgentToolClassification()`. |
| `HeaderFormatter` | `Func<ToolDescriptor, string>?` | `null` | Formats tool headers. Default: "Calling {DisplayName} Tool" for functions, "Calling {Source} MCP" for [MCP tools](mcp.md), "Calling {DisplayName} Agent" for [agent tools](agents.md). |
| `TimeProvider` | `TimeProvider` | `TimeProvider.System` | Clock for timestamps and durations; replace with a fake in tests for deterministic timing. |

## End-to-end with Azure OpenAI

This mirrors the integration test setup. Configuration lives in an **`appsettings.integration.json` file next to the executable** — deliberately not environment variables. The real file is gitignored so credentials never land in the repository; a tracked `appsettings.integration.sample.json` documents the shape:

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "ApiKey": "<your-api-key>",
    "Deployment": "<your-deployment-name>"
  }
}
```

Copy the sample, fill it in, and make sure it is copied to the output directory (the integration test project does this conditionally so builds work without it):

```xml
<None Include="appsettings.integration.json" Condition="Exists('appsettings.integration.json')" CopyToOutputDirectory="PreserveNewest" />
```

Required packages: `Azure.AI.OpenAI`, `Microsoft.Extensions.AI.OpenAI` (for `AsIChatClient()`), `Microsoft.Extensions.Configuration.Json`, and `Microsoft.Extensions.Configuration.Binder`.

```csharp
using Andes.Extensions.AI;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.integration.json", optional: true)
    .Build();

var settings = configuration.GetSection("AzureOpenAI").Get<AzureOpenAISettings>();
if (settings is not { Endpoint: not null, ApiKey: not null, Deployment: not null })
{
    Console.WriteLine("Copy appsettings.integration.sample.json to appsettings.integration.json and fill in the AzureOpenAI section.");
    return;
}

IChatClient client = new AzureOpenAIClient(new Uri(settings.Endpoint), new AzureKeyCredential(settings.ApiKey))
    .GetChatClient(settings.Deployment)
    .AsIChatClient()
    .AsBuilder()
    .UseToolTracking()
    .UseFunctionInvocation()
    .Build();

AIFunction time = AIFunctionFactory.Create(
    () =>
    {
        ChatProgress.Report("Formatting time...");
        return DateTimeOffset.UtcNow.ToString("O");
    },
    "GetCurrentTime",
    "Gets the current time in UTC.");

await foreach (var update in client.GetStreamingResponseAsync(
    "Call the GetCurrentTime tool, then tell me what time it returned.",
    new ChatOptions { Tools = [time] }))
{
    foreach (var content in update.Contents)
    {
        switch (content)
        {
            case ChatProgressContent { Progress: var progress }:
                Console.WriteLine($"{new string(' ', progress.Depth * 2)}[{progress.Kind}] {progress.Message}");
                break;

            case UsageReportContent { Report: var report }:
                Console.WriteLine(
                    $"Total: {report.TotalUsage.TotalTokenCount} tokens " +
                    $"({report.ModelId}, {report.Duration.TotalSeconds:F1}s)");
                break;
        }
    }

    Console.Write(update.Text);
}

public sealed class AzureOpenAISettings
{
    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    public string? Deployment { get; set; }
}
```

## Run the tests

```shell
dotnet test
```

Unit tests (`tests\Andes.Extensions.AI.Unit.Test`) need no network: a scripted fake drives the real `FunctionInvokingChatClient`. Integration tests (`tests\Andes.Extensions.AI.Integration.Test`) call Azure OpenAI for real, but **skip cleanly** when `appsettings.integration.json` is missing or incomplete — so `dotnet test` always passes out of the box. To run them for real, copy `appsettings.integration.sample.json` to `appsettings.integration.json` in the integration test project and fill in the `AzureOpenAI` section. The file is gitignored; do not commit it.

The [MCP package](mcp.md) adds three more projects: `tests\Andes.Extensions.AI.Mcp.Unit.Test` (also no network — an in-memory pipe fixture hosts a real MCP client/server pair in-process), `tests\Andes.Extensions.AI.TestMcpServer` (a runnable stdio server, "Andes Test MCP", with `echo`/`add`/`count_down` tools), and `tests\Andes.Extensions.AI.Mcp.Integration.Test`, which drives Azure OpenAI against that stdio server and **links the same gitignored `appsettings.integration.json`** — configure the file once and both integration projects use it.
