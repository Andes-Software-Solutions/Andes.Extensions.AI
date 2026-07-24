# MCP Tool Tracking

`Andes.Extensions.AI.Mcp` adds [Model Context Protocol](https://modelcontextprotocol.io/) support to the core [`Andes.Extensions.AI`](getting-started.md) tool-tracking middleware. The core middleware can already wrap and time any `AIFunction` — including MCP tools, which are `AIFunction`s — but two things are out of its reach: `McpClientTool` does not expose its owning server's name publicly (so headers cannot read "Calling GitHub MCP"), and MCP servers push `notifications/progress` while a tool runs, on a channel the middleware never sees. This satellite package supplies both:

1. **Classification** — `McpClientTool` instances are recognized as `ToolKind.McpTool` and render with the core's `"Calling {Server} MCP"` header in progress events and usage reports.
2. **Progress bridging** — each `notifications/progress` the server sends during a tool call becomes a `ChatProgressKind.ToolProgress` update under that tool's header, carrying the server's message and numeric `Progress`/`ProgressTotal` values.

This guide covers installation, how classification and the progress bridge work, and their deliberate limits. For the core middleware's design, see [Architecture](architecture.md); for core usage, see [Getting started](getting-started.md).

## Prerequisites and installation

- .NET SDK **10.0** or later (the package targets `net10.0`).
- The core pipeline from [Getting started](getting-started.md) — `UseToolTracking()` before `UseFunctionInvocation()`.

```shell
dotnet add package Andes.Extensions.AI.Mcp
```

Installing the package brings in the core `Andes.Extensions.AI` package (>= 0.2.0) and [`ModelContextProtocol.Core`](https://www.nuget.org/packages/ModelContextProtocol.Core) (>= 1.4.1). Apps that build MCP clients or servers with the full `ModelContextProtocol` package are unaffected — the satellite only needs the Core types.

## Quickstart

Two calls on top of the core pipeline: `UseMcpToolClassification()` on the options, and `WithTracking(...)` on the tools:

```csharp
using Andes.Extensions.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

McpClient mcpClient = await McpClient.CreateAsync(transport);   // any IClientTransport: stdio, HTTP, ...
IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();

IChatClient client = innerClient
    .AsBuilder()
    .UseToolTracking(options => options.UseMcpToolClassification())
    .UseFunctionInvocation() // tracking before function invocation (core invariant)
    .Build();

var chatOptions = new ChatOptions { Tools = mcpTools.WithTracking(mcpClient) };

await foreach (var update in client.GetStreamingResponseAsync("prompt", chatOptions))
{
    foreach (var progress in update.Contents.OfType<ChatProgressContent>())
    {
        var indent = new string(' ', progress.Progress.Depth * 2);
        Console.WriteLine($"{indent}[{progress.Progress.Kind}] {progress.Progress.Message}");
    }

    Console.Write(update.Text);
}
```

A typical rendering while a progress-reporting MCP tool runs:

```text
[RequestStarted] Starting request
[Thinking] Thinking...
  [ToolInvoking] Calling GitHub MCP
    [ToolProgress] step 1 of 3
    [ToolProgress] step 2 of 3
    [ToolProgress] step 3 of 3
  [ToolCompleted] search_issues completed
[Thinking] Thinking...
[RequestCompleted] Request completed
```

`WithTracking` has three overloads: per tool taking the `McpClient` (server name from `ServerInfo.Title ?? ServerInfo.Name`), per tool taking an explicit `serverName` string, and over an `IEnumerable<McpClientTool>` (the shape `ListToolsAsync()` returns), which produces an `IList<AITool>` ready to assign to `ChatOptions.Tools`.

## How classification works

`UseMcpToolClassification()` installs a `ToolTrackingOptions.ToolClassifier` that recognizes MCP tools by probing `AIFunction.GetService(typeof(McpClientTool))`. The probe traverses `DelegatingAIFunction` chains, so a tool classifies as `ToolKind.McpTool` whether it is a raw `McpClientTool`, a `WithTracking` wrapper, or your own delegating wrapper around either. Classified tools render with the core's default `"Calling {Source} MCP"` header and appear in usage reports with `Kind = ToolKind.McpTool` and `Source` set to the server name.

The server name (`Source`) is resolved in precedence order:

| Precedence | Server name comes from | Applies when |
| --- | --- | --- |
| 1 | The `WithTracking` wrapper — `ServerInfo.Title ?? ServerInfo.Name` from the client, or the explicit `serverName` string | The tool was wrapped with `WithTracking` |
| 2 | The `serverNameResolver` callback, when it returns non-`null` | The tool was not wrapped |
| 3 | `defaultServerName` (`"MCP"` unless overridden) | Everything else |

Non-MCP tools are delegated to whatever classifier was configured **before** `UseMcpToolClassification` was called, or to `ToolDescriptor.CreateDefault` (new public API in core v0.2: the built-in classification — `AIFunction` becomes `ToolKind.Function`, everything else `ToolKind.Unknown` — exposed for custom classifiers to fall back on) when there is none. To compose, assign your own classifier first, then call `UseMcpToolClassification`:

```csharp
.UseToolTracking(options =>
{
    options.ToolClassifier = tool => tool.Name.StartsWith("legacy_", StringComparison.Ordinal)
        ? new ToolDescriptor { Name = tool.Name, Kind = ToolKind.Function, Source = "Legacy" }
        : ToolDescriptor.CreateDefault(tool);

    options.UseMcpToolClassification(); // MCP tools short-circuit; everything else falls through to yours
})
```

## How the progress bridge works

When a tracked invocation of a `WithTracking` wrapper begins, the wrapper captures the ambient reporter (`ChatProgress.Current`) and invokes the underlying tool through `mcpTool.WithProgress(...)` with a bridge that forwards each notification to that captured reporter:

```text
MCP server ── notifications/progress ──► MCP client receive loop
                                           │ McpProgressBridge (IProgress<ProgressNotificationValue>)
                                           │   reporter captured at invocation time
                                           ▼
                              IChatProgressReporter bound to the tool's scope
                                           │
                                           ▼
                       ChatProgressKind.ToolProgress under the tool's header
                       (in-band ChatProgressContent + out-of-band observers)
```

Two aspects of this design are deliberate and worth understanding:

- **The reporter is captured at invocation time, never resolved ambiently at report time.** MCP progress notifications are dispatched on the MCP client's **receive loop** — a thread where the request's `AsyncLocal` tracking flow is absent. Resolving `ChatProgress.Current` from inside the notification callback would always find the no-op reporter. The wrapper therefore captures the bound reporter inside the tracking scope the core middleware pushes around the invocation, which is the only point where the ambient flow is present.
- **`WithProgress` is applied per invocation.** The `IProgress<>` must capture *that invocation's* reporter, so a cached progress-enabled tool cannot exist; each tracked call wraps the tool freshly.

Each `notifications/progress` becomes a `ToolProgress` update under the tool's header. `Message` is the server-supplied message when present; otherwise the bridge synthesizes an invariant-culture `"{progress}/{total}"` (or the progress value alone when the server sent no total). The numeric values land in `Progress`/`ProgressTotal` either way — see [Numeric progress values](#numeric-progress-values).

Exceptions never escape the bridge's `Report` — a catch-all guards it. An exception thrown on the receive loop would be swallowed by the MCP session into its own (typically absent) logger, silently killing the notification with no trace anywhere; the guard keeps a faulty report from ever taking that path.

Bridging is independent of classification: a `WithTracking` wrapper in a pipeline **without** `UseMcpToolClassification` still bridges progress — the tool just classifies as a plain `ToolKind.Function` and renders with the `"Calling {Name} Tool"` header instead.

### Ordering and threading

Bridged `ToolProgress` events arrive on a different thread than the request path and are **not ordered** relative to request-path events: a notification can interleave anywhere between the tool's `ToolInvoking` header and its `ToolCompleted` event. `IChatProgressObserver` implementations must be thread-safe — already the documented observer contract. Notifications are also fire-and-forget on the server side and race the tool result, so a late notification arriving as the request completes is dropped best-effort: the in-band write to the completed channel is a no-op, though an observer may still see one late event. Real progress-reporting tools are long-running, so the race is invisible in practice.

## Numeric progress values

Core v0.2 adds two optional fields to `ChatProgressUpdate`, populated only on `ToolProgress` events whose reporter supplied values:

- **`Progress`** (`double?`) — the amount of work completed so far.
- **`ProgressTotal`** (`double?`) — the total amount of work required, the denominator for `Progress`.

The bridge fills them from the MCP notification's `progress` and `total`. Regular (non-MCP) tools can supply them too, through the new `ChatProgress.Report(status, progress, progressTotal)` overload — see [Getting started](getting-started.md#report-from-inside-a-tool). `IChatProgressReporter` gains a matching method as a default interface member that forwards to `Report(status)`, so existing reporter implementations keep compiling unchanged.

> **Float widening.** MCP servers report progress as single-precision floats. Fractional values may show float-to-double widening artifacts — `33.3f` arrives as `33.29999923706055` — so format for display (for example `progress.Progress?.ToString("0.#")`) rather than printing the raw value. Integer step counts (`2` of `5`) are unaffected.

## Opt out with `enableProgress: false`

Every `WithTracking` overload accepts `enableProgress` (default `true`). Passing `false` keeps the server-name metadata — the tool still classifies as `ToolKind.McpTool` with the wrapper's server name — but skips bridging entirely: no `WithProgress` call, no `ToolProgress` updates from the server.

```csharp
var chatOptions = new ChatOptions { Tools = mcpTools.WithTracking(mcpClient, enableProgress: false) };
```

## Wrappers are never unwrapped

The bridge only ever applies to the exact `McpClientTool` a `WithTracking` wrapper was constructed over — there is no deep unwrapping. A user's own `DelegatingAIFunction` around an MCP tool still **classifies** as MCP (the `GetService` probe traverses delegating chains) but gets **no bridge**: your wrapper's behavior is never bypassed or reordered by this package. With `UseMcpToolClassification()` installed, the combinations are:

| Tool shape | Classification | Server name | Progress bridge |
| --- | --- | --- | --- |
| `WithTracking(client)` / `WithTracking(serverName)` | `ToolKind.McpTool` | The wrapper's server name | Yes |
| `WithTracking(..., enableProgress: false)` | `ToolKind.McpTool` | The wrapper's server name | No |
| Raw `McpClientTool` (no wrapper) | `ToolKind.McpTool` | `serverNameResolver`, else `defaultServerName` | No |
| Your own `DelegatingAIFunction` around an MCP tool | `ToolKind.McpTool` | `serverNameResolver`, else `defaultServerName` | No |

## Privacy posture

Unchanged from the core: progress events and reports **never carry prompt content, tool arguments, or tool results**. Bridged events carry only the server's progress message (or the synthesized numeric fallback) and the numeric values; the sole opt-in remains `ToolTrackingOptions.IncludeToolArguments` (default `false`), exactly as documented in [Architecture](architecture.md#privacy-posture).

## Test against the in-repo servers

Three test projects exercise the package against real MCP plumbing — the protocol is never mocked:

- `tests\Andes.Extensions.AI.Mcp.Unit.Test` — no network or child processes: the `Infrastructure\InMemoryMcpFixture` hosts a genuine MCP client/server pair over in-process pipe streams, so tests classify and invoke real `McpClientTool` instances and observe real bridged notifications (including the synthesized-message fallback and the completion race).
- `tests\Andes.Extensions.AI.TestMcpServer` — a runnable stdio MCP server ("Andes Test MCP") with three deterministic tools: `echo`, `add`, and `count_down`, which reports one progress notification per step. Useful for manual experiments as well as the integration tests.
- `tests\Andes.Extensions.AI.Mcp.Integration.Test` — drives a real Azure OpenAI deployment against the stdio test server, end to end through the tracked pipeline. Tests are `[SkippableFact]` and skip cleanly when configuration is missing. The project **links the same gitignored `appsettings.integration.json`** as `tests\Andes.Extensions.AI.Integration.Test` — configure it once (copy the `.sample` file, fill in the `AzureOpenAI` section) and both integration projects pick it up. Never environment variables.

```shell
dotnet test
```

## References

- [MCP specification — progress notifications](https://modelcontextprotocol.io/specification/2025-06-18/basic/utilities/progress)
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) and the [`ModelContextProtocol.Core` package](https://www.nuget.org/packages/ModelContextProtocol.Core)
- [Get started with .NET AI and the Model Context Protocol](https://learn.microsoft.com/dotnet/ai/get-started-mcp)
- [`DelegatingAIFunction`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.delegatingaifunction) and [`AITool.GetService`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.aitool.getservice)
