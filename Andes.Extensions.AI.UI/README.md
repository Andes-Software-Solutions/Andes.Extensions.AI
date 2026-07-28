# Andes.Extensions.AI.UI

UI status contract for [Andes.Extensions.AI](https://www.nuget.org/packages/Andes.Extensions.AI) tool tracking. Turns the tracked chat stream into a serializable, cross-language shape a UI can render — the same contract in C# (console, Blazor WebAssembly) and TypeScript (any SPA). Adds two things on top of the core middleware:

- **A serializable status contract** — flat per-flush `AssistantUiEvent` deltas and a folded `AssistantStatusSnapshot` (the assistant's status line plus a hierarchy of `AssistantActivity` cards — functions, MCP tools, and agents, each with sub-statuses, nested children, and token usage). Each activity carries a clean `DisplayName` plus a separate `Kind` badge, so the kind word is never repeated in the label.
- **A mapper and reducer** — `ToUiEventsAsync()`/`ToStatusSnapshotsAsync()` project the in-band `ChatProgressContent`/`UsageReportContent` stream into the contract; `AssistantStatusReducer` folds events into snapshots. A matching TypeScript `foldAssistantEvents` ships in the package (`typescript/andes-assistant-ui.ts`) so a SPA reconstructs the same tree.

## Install

```bash
dotnet add package Andes.Extensions.AI.UI
```

## Quickstart

```csharp
using Andes.Extensions.AI;
using Microsoft.Extensions.AI;

IChatClient client = innerClient
    .AsBuilder()
    .UseToolTracking()
    .UseFunctionInvocation() // tracking must be registered before function invocation
    .Build();

// Stream immutable snapshots and bind them to the UI (Blazor, console, ...).
await foreach (AssistantStatusSnapshot snapshot in client
    .GetStreamingResponseAsync("prompt", chatOptions)
    .ToStatusSnapshotsAsync())
{
    Console.WriteLine(snapshot.AssistantStatus);
    foreach (AssistantActivity activity in snapshot.Activities)
    {
        // e.g. "Andes Test MCP" [McpTool] — name and kind are separate, never "Andes Test MCP MCP"
        Console.WriteLine($"{activity.DisplayName} [{activity.Kind}] — {activity.State}");
    }
}
```

For an HTTP surface, stream `ToUiEventsAsync()` instead and serialize each event with `AssistantUiJsonContext` (camelCase, string enums, nulls omitted) over server-sent events; the browser folds them with `foldAssistantEvents` from the shipped `.ts` file.

## Notes

- `DisplayName` is the raw function/server/agent name with no "Calling" prefix and no kind word appended; render it once and show `Kind` as a badge. The contract carries no pre-composed header strings, so labels localize cleanly.
- `AssistantUiJsonContext` matches the TypeScript interface byte-for-byte: camelCase keys, string enum values (`"McpTool"`, `"Agent"`, …), and omitted `null`s.
- Progress values from MCP servers are single-precision floats widened to `double`; format with a rounding specifier such as `"0.#"` before display.
- Privacy posture matches the core package: events and snapshots never carry prompt content, tool arguments, or tool results — only headers, statuses, names, and token counts.

Full documentation lives in the [repository docs](https://github.com/RorroRojas3/Enterprise.AI/tree/main/docs).
