# Andes.Extensions.AI Demo

An interactive, Claude-Code-style console chat that exercises all four packages — [`Andes.Extensions.AI`](../../README.md), `Andes.Extensions.AI.Mcp`, `Andes.Extensions.AI.Agent`, and `Andes.Extensions.AI.UI` — in a single tracked `IChatClient` pipeline. Every turn streams through `ToStatusSnapshotsAsync()` and is rendered live with [Spectre.Console](https://spectreconsole.net/): a status header, an activity tree with per-kind badges and progress bars, the streamed answer, and a token-usage footer. The project is intentionally not packable (`samples/Directory.Build.props` sets `IsPackable=false`) — it never ships to NuGet.

## What it demonstrates

| File | Package | Feature |
| --- | --- | --- |
| `DemoTools.cs` | `Andes.Extensions.AI` (core) | A local function tool (`GetWeather`) reporting sub-statuses with numeric progress via `ChatProgress.Report(status, progress, progressTotal)`; `CreatePlanTrip` builds a `PlanTrip` tool that invokes the Packing Agent directly in its body — the agent opens its own child scope and renders as a nested card under `PlanTrip` |
| `DemoMcpServer.cs` | `Andes.Extensions.AI.Mcp` | A genuine in-process MCP client/server pair over pipe streams; `get_forecast` reports MCP progress notifications that the satellite bridges into chat progress; tools exposed via `WithTracking(client)` |
| `DemoAgents.cs` | `Andes.Extensions.AI.Agent` | A "Research Agent" and a "Packing Agent", both over raw (untracked) Azure OpenAI clients. The Research Agent is a top-level tool (`WithTracking(reportFunctionCalls: true)`); the Packing Agent is wrapped once with `WithTracking()` and nests two ways — as a tool of the Research Agent and inside the `PlanTrip` tool body — rendering as a child activity card with its own usage either way. Inner clients stay untracked because `WithTracking`'s usage capture already attributes each agent's tokens — a tracked inner pipeline would double-count them |
| `StatusRenderer.cs` + `Program.cs` | `Andes.Extensions.AI.UI` | `ToStatusSnapshotsAsync()` → `AssistantStatusSnapshot` → Spectre.Console `Live` rendering: an activity tree with `fn`/`mcp`/`agent` badges, nested child cards, per-step progress bars, durations, and token usage |
| `Program.cs` (`StreamTurn`) | `Andes.Extensions.AI` (core) | A developer-emitted request status: the middleware no longer auto-announces request start, so the app prepends `ChatProgressUpdate.CreateCustom("Starting request").ToResponseUpdate()` to the stream the renderer consumes — the header shows "Starting request" before the first tracked event arrives |
| `FinalSnapshot.cs` | `Andes.Extensions.AI.UI` | The persistent end-of-turn frame: `ChatUsageReport.ToSnapshot()`'s report-derived tree (per-activity token usage lives only there) merged positionally with the last live snapshot's answer text and sub-status lines |

## Prerequisites

- .NET SDK **10.0** or later.
- An Azure OpenAI resource with a chat deployment.

The demo never sets `Temperature` — reasoning-model deployments reject non-default values.

## Configure

Copy the sample settings file next to it in this folder and fill in the `AzureOpenAI` section:

```shell
cp samples/Andes.Extensions.AI.Demo/appsettings.sample.json samples/Andes.Extensions.AI.Demo/appsettings.json
```

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "ApiKey": "<your-api-key>",
    "Deployment": "<your-deployment-name>"
  }
}
```

`appsettings.json` is gitignored, so secrets never land in git. Environment variables are deliberately not used — the file is the single configuration source.

## Run

From the repo root:

```shell
dotnet run --project samples/Andes.Extensions.AI.Demo
```

Try the prompts printed at startup — each lights up a different flow:

> Get the weather in Quito and the 5-day forecast.

Function and MCP tools side by side: numeric sub-status progress from `GetWeather`, bridged MCP notifications from `get_forecast`.

> Ask the Research Agent what to pack for Quito.

An agent nested in an agent: the Research Agent consults its Packing Agent tool, which opens its own child scope and renders as a nested activity card with its own duration and token usage.

> Plan a trip to Cusco.

An agent nested in a plain tool: `PlanTrip` invokes the Packing Agent directly in its body — the same child card, opened from user code instead of an invocation loop.

Exit with an empty line, `exit`, or `quit`. In a non-interactive console (piped input or redirected output — scripts, CI) the Spectre `Live` region is skipped and prompts are read as plain lines; only the persistent final frame of each turn is rendered.

## How it fits together

`Program.cs` builds the pipeline with the one ordering invariant: `UseToolTracking()` **before** `UseFunctionInvocation()`, so the tracker wraps the tools the invoker executes and observes the merged stream from outside the invocation loop. `UseMcpToolClassification()` and `UseAgentToolClassification()` install the satellite classifiers so MCP tools and agents get their own badges and display names.

The Packing Agent is created once and shared by both nesting scenarios: registered as a tool of the Research Agent, and captured by the `PlanTrip` tool body. Either invocation path opens its own child scope, so the live tree and the final usage report both show it as a child of whatever called it.

Each turn tees the raw `ChatResponseUpdate` stream: one side drives the live renderer through `ToStatusSnapshotsAsync()`, the other is recorded for history. The tee prepends a developer-emitted `Custom` status ("Starting request") outside the recording loop, so the Live header lights up immediately while the synthetic update never enters the history or the usage report. After the stream drains, `FinalSnapshot.Merge` builds the persistent frame — the report's `ToSnapshot()` tree (the only place per-activity token usage exists) merged with the live snapshot's text and sub-statuses. Before the next turn, the app calls `StripProgressContent()` on the recorded response so the synthetic in-band progress and usage content never re-enters the request.

## See also

- [Root README](../../README.md) — package overview and quickstarts.
- [Getting started](../../docs/getting-started.md) — the core pipeline, step by step.
- [UI support](../../docs/ui.md) — the `AssistantStatusSnapshot` contract this demo renders.
