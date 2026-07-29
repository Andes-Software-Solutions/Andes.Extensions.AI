# Andes.Extensions.AI Demo

An interactive, Claude-Code-style console chat that exercises all four packages — [`Andes.Extensions.AI`](../../README.md), `Andes.Extensions.AI.Mcp`, `Andes.Extensions.AI.Agent`, and `Andes.Extensions.AI.UI` — in a single tracked `IChatClient` pipeline. Every turn streams through `ToStatusSnapshotsAsync()` and is rendered live with [Spectre.Console](https://spectreconsole.net/): a status header, an activity tree with per-kind badges and progress bars, the streamed answer, and a token-usage footer. The project is intentionally not packable (`samples/Directory.Build.props` sets `IsPackable=false`) — it never ships to NuGet.

## What it demonstrates

| File | Package | Feature |
| --- | --- | --- |
| `DemoTools.cs` | `Andes.Extensions.AI` (core) | A local function tool (`GetWeather`) reporting sub-statuses with numeric progress via `ChatProgress.Report(status, progress, progressTotal)` |
| `DemoMcpServer.cs` | `Andes.Extensions.AI.Mcp` | A genuine in-process MCP client/server pair over pipe streams; `get_forecast` reports MCP progress notifications that the satellite bridges into chat progress; tools exposed via `WithTracking(client)` |
| `DemoAgents.cs` | `Andes.Extensions.AI.Agent` | A "Research Agent" running over a raw (untracked) Azure OpenAI client, exposed as a tool via `agent.WithTracking(reportFunctionCalls: true)`; the inner client stays untracked because `WithTracking`'s usage capture already attributes the agent's tokens — a tracked inner pipeline would double-count them |
| `StatusRenderer.cs` + `Program.cs` | `Andes.Extensions.AI.UI` | `ToStatusSnapshotsAsync()` → `AssistantStatusSnapshot` → Spectre.Console `Live` rendering: an activity tree with `fn`/`mcp`/`agent` badges, per-step progress bars, durations, and token usage |

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

Try a prompt that lights up all three tool kinds at once:

> Get the weather in Quito, the 5-day forecast, and ask the Research Agent what to pack.

Exit with an empty line, `exit`, or `quit`.

## How it fits together

`Program.cs` builds the pipeline with the one ordering invariant: `UseToolTracking()` **before** `UseFunctionInvocation()`, so the tracker wraps the tools the invoker executes and observes the merged stream from outside the invocation loop. `UseMcpToolClassification()` and `UseAgentToolClassification()` install the satellite classifiers so MCP tools and agents get their own badges and display names.

Each turn tees the raw `ChatResponseUpdate` stream: one side drives the live renderer through `ToStatusSnapshotsAsync()`, the other is recorded for history. Before the next turn, the app calls `StripProgressContent()` on the recorded response so the synthetic in-band progress and usage content never re-enters the request.

## See also

- [Root README](../../README.md) — package overview and quickstarts.
- [Getting started](../../docs/getting-started.md) — the core pipeline, step by step.
- [UI support](../../docs/ui.md) — the `AssistantStatusSnapshot` contract this demo renders.
