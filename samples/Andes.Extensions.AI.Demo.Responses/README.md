# Andes.Extensions.AI Responses Demo

An interactive console chat like the [main demo](../Andes.Extensions.AI.Demo/README.md), but built on the **Azure OpenAI Responses API** instead of Chat Completions — the pipeline where the core package's detection-driven **`Reasoning` status** comes alive. Every turn streams through `ToStatusSnapshotsAsync()` and renders live with [Spectre.Console](https://spectreconsole.net/); while the model works through its hidden reasoning, the header switches to "Reasoning..." the moment reasoning summaries start streaming. The project is intentionally not packable (`samples/Directory.Build.props` sets `IsPackable=false`) — it never ships to NuGet.

## What it demonstrates

| Feature | Where |
| --- | --- |
| Responses API with **stable packages only**: the stable `Azure.AI.OpenAI` client has no Responses surface, so the plain `OpenAIClient` (stable `OpenAI` 2.12+) targets Azure's OpenAI-v1-compatible endpoint (`https://{resource}.openai.azure.com/openai/v1`) and `GetResponsesClient().AsIChatClient(deployment)` adapts it to `IChatClient` | `Program.cs` |
| Requesting reasoning summaries provider-agnostically with `ChatOptions.Reasoning = new ReasoningOptions { Output = ReasoningOutput.Summary }` — the summaries stream back as `TextReasoningContent` | `Program.cs` |
| The middleware's detection-driven `Reasoning` status: emitted once per model turn when reasoning content is detected, re-armed after each tool round-trip — no synthetic "Thinking" guesses | core `ToolTrackingChatClient` (just observe the header) |
| A developer-emitted request status: the middleware no longer auto-announces request start, so the app prepends `ChatProgressUpdate.CreateRequestStarted().ToResponseUpdate()` to the stream the renderer consumes | `Program.cs` (`StreamTurn`) |
| Local function tools reporting sub-statuses with numeric progress via `ChatProgress.Report(status, progress, progressTotal)` | `ResponsesDemoTools.cs` |
| `ToStatusSnapshotsAsync()` → `AssistantStatusSnapshot` → Spectre.Console `Live` rendering | `StatusRenderer.cs` + `Program.cs` |

## Prerequisites

- .NET SDK **10.0** or later.
- An Azure OpenAI resource with a **reasoning-capable deployment** (gpt-5 family / o-series).

Notes:

- The `Endpoint` setting is the plain resource endpoint — the app appends `/openai/v1` itself.
- Whether reasoning **summaries** actually stream depends on the deployment; some models additionally require [organization verification](https://learn.microsoft.com/azure/ai-services/openai/how-to/reasoning) before summaries are returned. Without summaries the demo still works — the `Reasoning` status simply has nothing to detect.
- The demo never sets `Temperature` — reasoning deployments reject non-default values.

## Configure

Copy the sample settings file next to it in this folder and fill in the `AzureOpenAI` section:

```shell
cp samples/Andes.Extensions.AI.Demo.Responses/appsettings.sample.json samples/Andes.Extensions.AI.Demo.Responses/appsettings.json
```

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "ApiKey": "<your-api-key>",
    "Deployment": "<your-reasoning-capable-deployment-name>"
  }
}
```

`appsettings.json` is gitignored, so secrets never land in git. Environment variables are deliberately not used — the file is the single configuration source.

## Run

From the repo root:

```shell
dotnet run --project samples/Andes.Extensions.AI.Demo.Responses
```

Try the prompts printed at startup:

> Get the weather in Quito, then convert the high to Fahrenheit.

A tool loop over the Responses API: the header shows "Reasoning..." while the model plans each call, then the `fn` activity cards stream their numeric sub-status progress.

> What is the sum of the first ten prime numbers? Reason it out.

A pure reasoning turn — no tools, just the detection-driven status followed by the streamed answer.

Exit with an empty line, `exit`, or `quit`. In a non-interactive console (piped input or redirected output — scripts, CI) the Spectre `Live` region is skipped and only the persistent final frame of each turn is rendered.

## How it fits together

`Program.cs` builds the pipeline with the one ordering invariant: `UseToolTracking()` **before** `UseFunctionInvocation()`. The stream tee prepends a developer-emitted `RequestStarted` status outside the recording loop, so the Live header lights up immediately while the synthetic update never enters the history or the usage report.

When history re-enters the next request, only the synthetic progress/usage content is stripped (`StripProgressContent()`). `TextReasoningContent` is deliberately kept: the Responses API expects prior reasoning items to be replayed across tool round-trips and follow-up turns.

Unlike the main demo this sample renders its final frame from the last live snapshot directly (the trailing `Finished` event carries the completed phase and total usage); see the main demo's `FinalSnapshot.cs` for the report-merge pattern that adds per-activity token usage.

## See also

- [Main demo](../Andes.Extensions.AI.Demo/README.md) — all four packages, MCP + agent tools, report-derived final frames.
- [Root README](../../README.md) — package overview and quickstarts.
- [Getting started](../../docs/getting-started.md) — the core pipeline, step by step.
