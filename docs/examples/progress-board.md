# Example: The Progress Board — every tool kind in one stream

This example wires **all three tool kinds** into one tracked `IChatClient` pipeline — a plain function tool (`ToolKind.Function`), MCP tools via [`Andes.Extensions.AI.Mcp`](../mcp.md) (`ToolKind.McpTool`), and a Microsoft Agent Framework agent-as-tool via [`Andes.Extensions.AI.Agent`](../agents.md) (`ToolKind.Agent`) — and shows how the merged stream propagates to a UI. Two things make it more than a bigger [quickstart](../getting-started.md):

1. **The app talks to the UI before the pipeline does.** The streaming service yields its own status messages through the same `IAsyncEnumerable` *before* `GetStreamingResponseAsync` is ever called, so "Connecting to tools…"-style lines and the pipeline's in-band events reach the consumer through one channel.
2. **A `ProgressBoard` folds the event stream into a hierarchy of boxes.** Built purely on the public `ChatProgressUpdate` contract, it turns each tool call into a separate box — a title (the header, e.g. "Calling Andes Test MCP") plus subtitle lines (MCP numeric progress, the agent's inner tool statuses, function-reported statuses) — the shape a real UI would render as cards.

The five listings form a runnable console application. The MCP leg uses the in-repo stdio test server `tests\Andes.Extensions.AI.TestMcpServer` ("Andes Test MCP", with `echo`, `add`, and `count_down`, which streams one progress notification per step), so nothing external is needed beyond a model provider: the reader supplies `CreateProviderClient()` (any `IChatClient` — see [Getting started](../getting-started.md#end-to-end-with-azure-openai) for an Azure OpenAI version), and to run the MCP leg, build the test server and place its dll in (or point the stdio arguments at) the app's output directory, exactly as `Program.cs` does — a `ProjectReference` to the server project is the simplest way, the same technique the MCP integration tests use.

## The pipeline

Everything assembles in `Program.cs`: the three tools, the tracked pipeline, and the streaming loop.

```csharp
using Andes.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ProgressBoardExample;

// ── 1. Provider clients (any IChatClient: Azure OpenAI, OpenAI, Ollama, ...) ─────────────
IChatClient innerClient = CreateProviderClient();      // the main assistant's model
IChatClient researchClient = CreateProviderClient();   // the inner agent's model

// ── 2. A local function tool that reports its own sub-statuses ───────────────────────────
AIFunction forecast = AIFunctionFactory.Create(
    (string city) =>
    {
        ChatProgress.Report("Contacting the forecast service…");
        ChatProgress.Report("Crunching the numbers…", progress: 2, progressTotal: 3);
        return $"Sunny in {city} all week.";
    },
    "GetForecast");

// ── 3. MCP tools from the in-repo test server ("Andes Test MCP") ─────────────────────────
McpClient mcpClient = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
{
    Command = "dotnet",
    Arguments = [Path.Combine(AppContext.BaseDirectory, "Andes.Extensions.AI.TestMcpServer.dll")],
    Name = "Andes Test MCP",
}));
IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();

// ── 4. An agent exposed as a tool, with its own function tools ───────────────────────────
AIAgent researchAgent = researchClient.AsAIAgent(
    instructions: "You research destinations using the SearchDocs tool and summarize the results.",
    name: "Research Agent",
    description: "Researches a destination and returns a short summary.",
    tools:
    [
        AIFunctionFactory.Create(
            (string topic) =>
            {
                ChatProgress.Report("Summarizing…");
                return $"Top attractions for {topic}: the old town, the cable car, the equator line.";
            },
            "SearchDocs"),
    ]);

// ── 5. One tracked pipeline with both satellite classifiers installed ────────────────────
IChatClient client = innerClient
    .AsBuilder()
    .UseToolTracking(options =>
    {
        options.UseMcpToolClassification();
        options.UseAgentToolClassification();
    })
    .UseFunctionInvocation() // tracking before function invocation (core invariant)
    .Build();

var chatOptions = new ChatOptions
{
    Tools =
    [
        forecast,
        .. mcpTools.WithTracking(mcpClient),
        researchAgent.WithTracking(reportFunctionCalls: true),
    ],
};

// ── 6. Stream and render ─────────────────────────────────────────────────────────────────
var service = new TripPlannerService(client, chatOptions);
await foreach (AssistantUiEvent uiEvent in service.StreamAsync(
    "Plan a day in Quito: check the forecast, count down from 3 with the MCP tool, and research the city."))
{
    ConsoleRenderer.Render(uiEvent);
}

static IChatClient CreateProviderClient() =>
    throw new NotImplementedException("Plug in your provider client (Azure OpenAI, OpenAI, Ollama, ...).");
```

Three things to notice:

- **`UseToolTracking()` comes before `UseFunctionInvocation()`** — the [core ordering invariant](../architecture.md#pipeline-topology-and-the-ordering-invariant). The tracker wraps the tools that the `FunctionInvokingChatClient` executes; reversed, it sees nothing.
- **The two satellite classifiers compose.** Each recognizes only its own kind and short-circuits; everything else falls through to whatever was configured before it, ending at the core default (`AIFunction` → `ToolKind.Function`). Because MCP tools and agent tools are disjoint, the order of the two `Use*Classification()` calls does not matter — [the satellites stack either way](../agents.md#how-classification-works).
- **All three tool registrations sit side by side in one `ChatOptions.Tools`**: the plain `forecast` function needs no wrapper at all, the MCP tools get `WithTracking(mcpClient)` (server name + [progress bridging](../mcp.md#how-the-progress-bridge-works)), and the agent gets `WithTracking(reportFunctionCalls: true)` (classification + usage capture + [function-call statuses](../agents.md#seeing-the-agents-own-function-calls)).

## Talking to the UI: the event stream

The UI never touches `ChatResponseUpdate`. The service translates the merged stream into a small app-owned contract — four records:

```csharp
using Andes.Extensions.AI;

namespace ProgressBoardExample;

/// <summary>The base type for everything the streaming service yields to the UI.</summary>
public abstract record AssistantUiEvent;

/// <summary>An app-authored status line, shown before or while the model works.</summary>
public sealed record AssistantStatus(string Message) : AssistantUiEvent;

/// <summary>The progress board changed; re-render the boxes.</summary>
public sealed record BoardChanged(ProgressBoard Board) : AssistantUiEvent;

/// <summary>A chunk of the assistant's answer text.</summary>
public sealed record TextDelta(string Text) : AssistantUiEvent;

/// <summary>The request finished; the final usage report is available.</summary>
public sealed record RequestFinished(ChatUsageReport Report) : AssistantUiEvent;
```

The service's `IAsyncEnumerable<AssistantUiEvent>` is the **single channel to the UI** — and because it is an ordinary iterator, the app can yield any number of its own "Thinking"-style statuses **before the pipeline streaming even starts**. The first two `yield return`s below run before `GetStreamingResponseAsync` is ever called; the pipeline's in-band events then follow through the same channel:

```csharp
using System.Runtime.CompilerServices;
using Andes.Extensions.AI;
using Microsoft.Extensions.AI;

namespace ProgressBoardExample;

/// <summary>
/// Streams a prompt through the tracked pipeline and translates the merged stream into UI events.
/// </summary>
public sealed class TripPlannerService(IChatClient client, ChatOptions chatOptions)
{
    public async IAsyncEnumerable<AssistantUiEvent> StreamAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The UI hears from us before the model does: anything yielded here reaches the consumer
        // through the same IAsyncEnumerable, ahead of the first pipeline update.
        yield return new AssistantStatus("Connecting to tools…");
        yield return new AssistantStatus("Planning your trip…");

        var board = new ProgressBoard();

        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(prompt, chatOptions, cancellationToken))
        {
            foreach (AIContent content in update.Contents)
            {
                switch (content)
                {
                    case ChatProgressContent progress:
                        board.Apply(progress.Progress);
                        yield return new BoardChanged(board);
                        break;

                    case UsageReportContent usage:
                        yield return new RequestFinished(usage.Report);
                        break;
                }
            }

            if (update.Text is { Length: > 0 } text)
            {
                yield return new TextDelta(text);
            }
        }
    }
}
```

The mapping is mechanical:

| Stream content | UI event |
| --- | --- |
| `ChatProgressContent` (synthetic progress) | `board.Apply(...)`, then `BoardChanged` |
| `UsageReportContent` (final report, streaming) | `RequestFinished` |
| Text deltas (`update.Text`) | `TextDelta` |

## The Progress Board: hierarchy from the event contract

The board holds no middleware state — it reconstructs the tool-call tree from fields every `ChatProgressUpdate` carries (`ScopeId`, `ParentScopeId`, `Depth`, and the tool metadata). The semantics it rides on, verified against the core's `ChatProgressUpdate` and `RequestTracker`:

| Event kind | `ScopeId` | `Depth` | Board action |
| --- | --- | --- | --- |
| `RequestStarted` / `Thinking` / `RequestCompleted` | The request root | 0 | Update the top-level assistant status line |
| `ToolInvoking` | A fresh scope | parent + 1 | New box (`Title` = the header; `ToolKind`/`ToolName`/`ToolSource` for badges), parented via `ParentScopeId` — a top-level tool's parent is the request root, which has no box, so it becomes a root |
| `ToolProgress` | **The owning tool's scope** | tool + 1 | Append a subtitle line (`Message`, plus `Progress`/`ProgressTotal` rendered as "2/3") |
| `ToolCompleted` / `ToolFailed` | The tool's scope | tool | Mark the state, set `Duration` |

```csharp
using System.Globalization;
using Andes.Extensions.AI;

namespace ProgressBoardExample;

/// <summary>The lifecycle state of a box.</summary>
public enum BoxState
{
    Running,
    Completed,
    Failed,
}

/// <summary>
/// One tool call rendered as a box: a title (the tool header), subtitle lines (the sub-statuses
/// reported while it ran), and nested child boxes for tools invoked inside it.
/// </summary>
public sealed class ProgressBox
{
    private readonly List<string> _subtitles = [];
    private readonly List<ProgressBox> _children = [];

    /// <summary>The scope identifier this box tracks (from <see cref="ChatProgressUpdate.ScopeId"/>).</summary>
    public required string ScopeId { get; init; }

    /// <summary>The box title — the tool header, e.g. "Calling Andes Test MCP".</summary>
    public required string Title { get; init; }

    /// <summary>The tool category, for a badge or icon.</summary>
    public ToolKind Kind { get; init; }

    /// <summary>The registered tool name, e.g. "count_down" or "Research_Agent".</summary>
    public string? ToolName { get; init; }

    /// <summary>The tool origin — an MCP server or agent name.</summary>
    public string? Source { get; init; }

    /// <summary>Whether the call is still running, completed, or failed.</summary>
    public BoxState State { get; private set; } = BoxState.Running;

    /// <summary>The elapsed time, set when the call completes or fails.</summary>
    public TimeSpan? Duration { get; private set; }

    /// <summary>The most recent subtitle, for compact single-line renderings.</summary>
    public string? Subtitle => _subtitles.Count > 0 ? _subtitles[^1] : null;

    /// <summary>Every subtitle reported so far, oldest first.</summary>
    public IReadOnlyList<string> Subtitles => _subtitles;

    /// <summary>Boxes for tools invoked inside this call.</summary>
    public IReadOnlyList<ProgressBox> Children => _children;

    internal void AddSubtitle(string line) => _subtitles.Add(line);

    internal void AddChild(ProgressBox child) => _children.Add(child);

    internal void Finish(bool failed, TimeSpan? duration)
    {
        State = failed ? BoxState.Failed : BoxState.Completed;
        Duration = duration;
    }
}

/// <summary>
/// Folds the stream of <see cref="ChatProgressUpdate"/> events into a hierarchy of boxes:
/// one top-level status line for the assistant, one box per tool call, subtitles beneath each box.
/// </summary>
public sealed class ProgressBoard
{
    private readonly Dictionary<string, ProgressBox> _byScope = [];
    private readonly List<ProgressBox> _roots = [];

    /// <summary>The request-level status line ("Thinking...", "Request completed").</summary>
    public string? AssistantStatus { get; private set; }

    /// <summary>The top-level boxes, one per tool call the assistant made, in order.</summary>
    public IReadOnlyList<ProgressBox> Roots => _roots;

    /// <summary>Applies one progress event to the board.</summary>
    public void Apply(ChatProgressUpdate update)
    {
        switch (update.Kind)
        {
            case ChatProgressKind.RequestStarted or ChatProgressKind.Thinking or ChatProgressKind.RequestCompleted:
                AssistantStatus = update.Message;
                break;

            case ChatProgressKind.ToolInvoking:
                var box = new ProgressBox
                {
                    ScopeId = update.ScopeId,
                    Title = update.Message,
                    Kind = update.ToolKind,
                    ToolName = update.ToolName,
                    Source = update.ToolSource,
                };
                _byScope[update.ScopeId] = box;

                // The parent of a top-level tool is the request root, which has no box —
                // those become roots. Tools invoked inside another tracked tool nest.
                if (update.ParentScopeId is not null && _byScope.TryGetValue(update.ParentScopeId, out ProgressBox? parent))
                {
                    parent.AddChild(box);
                }
                else
                {
                    _roots.Add(box);
                }

                break;

            case ChatProgressKind.ToolProgress when _byScope.TryGetValue(update.ScopeId, out ProgressBox? owner):
                owner.AddSubtitle(FormatSubtitle(update));
                break;

            case ChatProgressKind.ToolCompleted or ChatProgressKind.ToolFailed
                when _byScope.TryGetValue(update.ScopeId, out ProgressBox? finished):
                finished.Finish(update.Kind == ChatProgressKind.ToolFailed, update.Duration);
                break;
        }
    }

    private static string FormatSubtitle(ChatProgressUpdate update)
    {
        if (update.Progress is { } progress)
        {
            string value = progress.ToString("0.#", CultureInfo.InvariantCulture);
            return update.ProgressTotal is { } total
                ? $"{update.Message} ({value}/{total.ToString("0.#", CultureInfo.InvariantCulture)})"
                : $"{update.Message} ({value})";
        }

        return update.Message;
    }
}
```

In this example, three different mechanisms land as subtitles — and every one arrives as a `ToolProgress` event on its tool's scope, so the board treats them identically:

- **The function tool's own `ChatProgress.Report(...)` calls** — "Contacting the forecast service…", then "Crunching the numbers…" with numeric progress, which `FormatSubtitle` renders as `(2/3)`.
- **The MCP server's bridged `notifications/progress`** — `count_down` reports one notification per step, which the [progress bridge](../mcp.md#how-the-progress-bridge-works) turns into "step 1 of 3 (1/3)", "step 2 of 3 (2/3)", "step 3 of 3 (3/3)" under the server's header.
- **Inside the agent box, both status paths**: the `reportFunctionCalls: true` middleware's "Calling SearchDocs Tool" line, and the inner tool's own "Summarizing…" report, which flows out through the [in-process ambient flow](../agents.md#seeing-the-agents-own-function-calls) with no configuration at all. Both surface as sub-statuses on the agent's scope because `SearchDocs` is a plain function tool — it has no scope of its own. Give the agent a `WithTracking`-wrapped tool instead — [another agent](../agents.md#nested-agents), or an [MCP tool](../mcp.md#nested-mcp-tools) — and that call **does** create a child box (since v0.3): the satellite wrapper opens a real child scope, so a nested `ToolInvoking` arrives with `ParentScopeId` set and the board's existing wiring nests it, no board changes required. The same `Children`/`ParentScopeId` wiring also handles a tool running a [nested tracked pipeline](../architecture.md#the-ambient-scope-tree), whose tool calls open child scopes too.

One deliberate omission: **the board has no locking**, and it does not need any — for in-band consumption. The core's channel pump serializes every event into stream order, and the `await foreach` consumes them one at a time on a single logical flow, so `Apply` is never called concurrently. An out-of-band `IChatProgressObserver` feeding the same board **would** need synchronization: observers are invoked from multiple threads, including the MCP receive loop — see [Ordering and threading](../mcp.md#ordering-and-threading).

## What the user sees

The console renderer is the minimal take on the boxes — a real UI would data-bind the board and re-render only what changed:

```csharp
namespace ProgressBoardExample;

/// <summary>
/// A minimal console take on the boxes a real UI would render. A real UI would data-bind the
/// board and re-render only what changed; reprinting on every event keeps the example honest
/// about *when* updates arrive without any UI framework.
/// </summary>
public static class ConsoleRenderer
{
    public static void Render(AssistantUiEvent uiEvent)
    {
        switch (uiEvent)
        {
            case AssistantStatus status:
                Console.WriteLine($"· {status.Message}");
                break;

            case BoardChanged changed:
                Console.WriteLine();
                if (changed.Board.AssistantStatus is { } line)
                {
                    Console.WriteLine($"· {line}");
                }

                foreach (ProgressBox box in changed.Board.Roots)
                {
                    RenderBox(box, indent: 0);
                }

                break;

            case TextDelta delta:
                Console.Write(delta.Text);
                break;

            case RequestFinished finished:
                Console.WriteLine();
                Console.WriteLine($"— {finished.Report.TotalUsage.TotalTokenCount} tokens total across {finished.Report.ToolCalls.Count} tool calls —");
                break;
        }
    }

    private static void RenderBox(ProgressBox box, int indent)
    {
        string pad = new(' ', indent * 2);
        string state = box.State switch
        {
            BoxState.Completed => $"done in {box.Duration?.TotalSeconds:0.0}s",
            BoxState.Failed => "failed",
            _ => "running",
        };

        Console.WriteLine($"{pad}┌ {box.Title}  [{state}]");
        foreach (string subtitle in box.Subtitles)
        {
            Console.WriteLine($"{pad}│   {subtitle}");
        }

        foreach (ProgressBox child in box.Children)
        {
            RenderBox(child, indent + 1);
        }
    }
}
```

Condensed (the renderer reprints the board on every event; this is the two app-authored statuses, the final board frame, the answer, and the usage line):

```text
· Connecting to tools…
· Planning your trip…

· Thinking...
┌ Calling GetForecast Tool  [done in 0.2s]
│   Contacting the forecast service…
│   Crunching the numbers… (2/3)
┌ Calling Andes Test MCP  [done in 0.8s]
│   step 1 of 3 (1/3)
│   step 2 of 3 (2/3)
│   step 3 of 3 (3/3)
┌ Calling Research Agent  [done in 2.1s]
│   Calling SearchDocs Tool
│   Summarizing…
A day in Quito: sunny all week, countdown complete, and the old town ...
— 1234 tokens total across 3 tool calls —
```

Each box is separate, with its own title and its own subtitles — exactly the UI shape the board models: three cards, one per tool kind, each labeled with its header ("Calling {Name} Tool", "Calling {Server} MCP", "Calling {Agent} Agent") and filled with the sub-statuses that arrived while it ran. And the final line undersells the report: `ChatUsageReport` additionally attributes tokens **per tool call** (`ToolCalls[i].Usage`), including the agent's own consumption, which `WithTracking`'s [usage capture](../agents.md#how-usage-capture-works) attributed to the agent's scope — a per-card token badge is one property access away.

## Notes

- **Strip synthetic content before persisting.** `ChatProgressContent` and `UsageReportContent` do not serialize; call [`StripProgressContent()`](../getting-started.md#strip-synthetic-content-before-persisting-history) on responses before adding them to conversation history.
- **MCP-bridged events can interleave.** Bridged `ToolProgress` notifications arrive from the MCP receive loop and are not ordered relative to request-path events — a notification can land anywhere between the tool's header and its completion. Harmless for the board in-band, but see [Ordering and threading](../mcp.md#ordering-and-threading) before adding observers.
- **The agent's usage lands via `trackUsage: true`** (the default), which is correct here because the inner agent's pipeline is untracked. For a self-tracked agent, pass `trackUsage: false` or the tokens count twice — see [Avoid double counting](../agents.md#avoid-double-counting).
- **Privacy invariant, unchanged.** Everything on the board is headers, statuses, and tool names — never prompt content, arguments, or results. The sole opt-in remains `ToolTrackingOptions.IncludeToolArguments` (default `false`) — see [Privacy posture](../architecture.md#privacy-posture).
- **Non-invocable tools become boxes that never complete.** Tool declarations the pipeline cannot invoke get a [best-effort `ToolInvoking` header](../architecture.md#known-limitations-v03) parented to the root — the board shows them as root boxes stuck in `Running`, with no subtitles, duration, or completion. A production board may want a timeout-based visual state for those.

## References

- [Getting started](../getting-started.md) — the core pipeline, `ChatProgress.Report`, and the usage report
- [MCP tool tracking](../mcp.md) — classification, the progress bridge, ordering and threading
- [Agent tool tracking](../agents.md) — classification, usage capture, `reportFunctionCalls`
- [Architecture](../architecture.md) — the channel merge, the ambient scope tree, known limitations
