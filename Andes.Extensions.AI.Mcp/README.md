# Andes.Extensions.AI.Mcp

Model Context Protocol (MCP) support for [Andes.Extensions.AI](https://www.nuget.org/packages/Andes.Extensions.AI) tool tracking. Adds two things on top of the core middleware:

- **Classification** — `McpClientTool` instances are recognized as `ToolKind.McpTool` and render with the `"Calling {Server} MCP"` header in progress updates and usage reports.
- **Progress bridging** — MCP `notifications/progress` sent by the server while a tool runs become `ChatProgressKind.ToolProgress` updates, with the server's message (or a synthesized `"{progress}/{total}"`) and numeric `Progress`/`ProgressTotal` values.

## Install

```bash
dotnet add package Andes.Extensions.AI.Mcp
```

## Quickstart

```csharp
using Andes.Extensions.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

McpClient mcpClient = await McpClient.CreateAsync(transport);
IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();

IChatClient client = innerClient
    .AsBuilder()
    .UseToolTracking(options => options.UseMcpToolClassification())
    .UseFunctionInvocation() // tracking must be registered before function invocation
    .Build();

var chatOptions = new ChatOptions
{
    Tools = mcpTools.WithTracking(mcpClient),
};

await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync("prompt", chatOptions))
{
    foreach (ChatProgressContent progress in update.Contents.OfType<ChatProgressContent>())
    {
        // e.g. "Calling GitHub MCP" (header), then "step 2 of 5" with Progress=2, ProgressTotal=5
        Console.WriteLine($"{progress.Progress.Message} ({progress.Progress.Progress}/{progress.Progress.ProgressTotal})");
    }
}
```

`WithTracking` carries the server display name (`ServerInfo.Title ?? ServerInfo.Name`, or an explicit string) and enables the progress bridge; pass `enableProgress: false` to keep the header but skip bridging. Raw `McpClientTool`s that skip `WithTracking` are still classified as MCP (falling back to `UseMcpToolClassification`'s default server name) but receive no progress bridge — the same applies to MCP tools hidden inside your own `DelegatingAIFunction` wrappers, which this package never unwraps or bypasses.

Nested calls render as their own activity: when a `WithTracking`-wrapped tool runs inside an agent, or is invoked directly inside another tool's body, the wrapper opens a child tracking scope — the call streams as a child activity under the enclosing tool, appears as a child `ToolCallUsage` in the `ChatUsageReport`, and bridged progress notifications bind to that child scope. A tool the tracking middleware wrapped itself still opens exactly one scope, and report totals are unchanged either way.

## Notes

- MCP servers report progress as single-precision floats; fractional values may show float→double widening artifacts (`33.3f` → `33.29999923706055`). Integer step counts are unaffected.
- Progress notifications arrive on the MCP client's receive loop and are delivered best-effort: a notification racing the end of the request may be dropped.
- Privacy posture matches the core package: progress events never carry prompt content, tool arguments, or tool results.

Full documentation lives in the [repository docs](https://github.com/RorroRojas3/Enterprise.AI/tree/main/docs).
