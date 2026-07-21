# Usage Tracking

Beyond status updates, the tool-tracking middleware performs granular token-usage accounting: every token the provider reports is attributed to the exact scope — root request, tool call, or nested agent model call — that incurred it, then rolled up into a per-request `ChatActivityReport` with per-scope and per-model totals. This document explains what gets counted where, the difference between `OwnUsage` and `TotalUsage`, how nested agent usage is attributed, and the limits of what can be measured.

## What gets counted where

| Request shape | Usage source | Recorded |
| --- | --- | --- |
| Streaming | Each `UsageContent` item observed in the inner stream's `ChatResponseUpdate.Contents` | Per update, as it arrives — each one also publishes a `UsageReported` event |
| Non-streaming | `ChatResponse.Usage` on the inner response | Once, when the inner call returns |

Alongside the raw counts, each recording captures:

- **Model id** — from `ChatResponseUpdate.ModelId` (streaming) or `ChatResponse.ModelId` (non-streaming), falling back to the inner client's default model id from its metadata.
- **Provider name** — from the inner client's `ChatClientMetadata` (e.g. Azure OpenAI's provider name). The middleware resolves the metadata once from the client it wraps, so multi-provider setups report each provider correctly as long as each provider pipeline has its own tracker.

Usage is attributed to the scope that was ambient when it was observed: the root scope for the main model's calls, or a nested model-call scope when a tracked pipeline runs inside a tool (see [attribution](#attribution-for-nested-agents) below).

> [!NOTE]
> **Null means unknown, never zero.** A token count in any report is `null` until at least one provider-reported value arrived for it. Providers that do not report usage (or only report it when asked — some require a streaming usage option to be enabled) simply leave counts `null`. Treat `null` as "not measured", and never coerce it to `0` in cost calculations.

## The report

`ChatActivityReport` is produced exactly once per top-level request:

| Property | Type | Meaning |
| --- | --- | --- |
| `RequestId` | `string` | Identifier of the request. |
| `StartedAt` | `DateTimeOffset` | When the request started. |
| `Duration` | `TimeSpan` | Total wall-clock duration. |
| `TotalUsage` | `UsageDetails` | Total usage across the request and every nested scope. |
| `Root` | `ActivityScopeReport` | Root of the activity scope tree. |
| `UsageByModel` | `IReadOnlyList<ModelUsageBreakdown>` | Flat rollup per (provider, model) pair, in first-seen order. |

Each `ActivityScopeReport` node carries `ScopeId`, `ParentScopeId`, `ToolKind`, `ToolName`, `SourceName` (agent or MCP server display name), `Duration`, `OwnUsage`, `TotalUsage`, and `Children` (in start order).

### Delivery

1. **`IChatActivityObserver.OnRequestCompleted(report)`** — the canonical path; fires for streaming and non-streaming requests alike, on success, failure, cancellation, or abandoned streams.
2. **Non-streaming** — the same report instance is also attached to `ChatResponse.AdditionalProperties`; retrieve it with `ChatActivityReport.FromResponse(response)` (returns `null` for untracked responses).
3. **Streaming** — the final in-band `ActivityStatusContent` update (`Kind == RequestCompleted`) additionally carries `TotalUsage`, so browser clients get the headline number without a second channel.

Nested (joined) requests never produce their own report — their scopes and usage fold into the root request's report.

## `OwnUsage` vs `TotalUsage`

- **`OwnUsage`** — usage recorded *directly in* the scope, excluding descendants.
- **`TotalUsage`** — the scope's own usage plus all descendant scopes, computed bottom-up when the report is built.

### Worked example

The main pipeline answers a question by delegating to a Researcher agent exposed as a tracked tool; the agent runs its own tracked pipeline. Two model calls happen:

- The **outer** model call (deciding to call the agent, then producing the final answer): 10 input + 5 output = 15 tokens, recorded on the root scope.
- The **agent's nested** model call: 100 input + 50 output = 150 tokens, recorded on the nested model-call scope.

The resulting scope tree:

```text
Root (request)                          OwnUsage: 10 in /  5 out /  15 total
│                                       TotalUsage: 110 in / 55 out / 165 total
└── researcher_agent (ToolKind.Agent)   OwnUsage: null (nothing recorded here directly)
    │                                   TotalUsage: 100 in / 50 out / 150 total
    └── model call (nested pipeline)    OwnUsage: 100 in / 50 out / 150 total
                                        TotalUsage: 100 in / 50 out / 150 total
```

Reading it:

- `report.TotalUsage.TotalTokenCount` is **165** — the full cost of the request.
- The agent scope's `TotalUsage` is **150** — what delegating to the Researcher cost, even though the agent tool itself recorded no usage directly (`OwnUsage` counts stay `null`; the agent scope only aggregates its children).
- The nested model-call scope's `OwnUsage` is **150** — the tokens the agent's own LLM turn consumed.
- Root `OwnUsage` (15) + agent `TotalUsage` (150) = root `TotalUsage` (165).

## `UsageByModel` rollups

`UsageByModel` flattens the same numbers by (provider, model), in the order each pair was first seen — useful for cost dashboards that price per model:

```csharp
foreach (ModelUsageBreakdown model in report.UsageByModel)
{
    Console.WriteLine(
        $"{model.ProviderName}/{model.ModelId}: " +
        $"in={model.InputTokenCount?.ToString() ?? "?"} " +
        $"out={model.OutputTokenCount?.ToString() ?? "?"} " +
        $"total={model.TotalTokenCount?.ToString() ?? "?"}");
}
```

If the outer pipeline used `gpt-4o` and the Researcher agent used `gpt-4o-mini`, the example above yields two entries: `gpt-4o` with 15 total tokens and `gpt-4o-mini` with 150. A request whose tool loop spans multiple model turns on the same model produces a single summed entry.

## Attribution for nested agents

When a tracked chat client executes *inside* a tool invocation of another tracked request (the Microsoft Agent Framework agent-as-tool pattern), it detects the ambient request and joins it instead of starting a new one:

- A silent **model-call scope** is opened under the agent's tool-call scope; all the nested pipeline's usage is recorded there.
- Each nested usage recording still publishes a `UsageReported` event on the *root* request's observer, with the nested `ScopeId` — so live dashboards can attribute cost in real time, not just at the end.
- This works even though the outer and inner middleware are different instances in different pipelines: the ambient flow is static (`AsyncLocal`-based), not per-instance.
- Nesting is recursive: an agent that calls another agent produces a deeper tree, and every level rolls up correctly through `TotalUsage`.

The same applies to any tracked pipeline invoked inside any tool — the pattern is not agent-specific, agents are just the common case.

## Known limits

- **Only as granular as the provider reports.** The middleware attributes what it observes; it cannot split a single provider-reported usage figure across finer-grained boundaries than the provider exposes. In a streaming tool loop, usage typically arrives per model turn, which is the finest attribution available.
- **Un-instrumented nested clients are invisible.** If a tool internally calls an LLM through a client that does *not* have `UseToolTracking()` in its pipeline, that usage is never seen. The tool's duration and lifecycle are still tracked, but its scope shows no usage. Instrument every pipeline whose cost you care about.
- **`null` ≠ `0`.** Counts are `null` until the provider reports them (see the note above). A report with `TotalUsage.TotalTokenCount == null` means "the provider reported nothing", not "the request was free".
- **Cross-provider identity is metadata-based.** `ProviderName`/`ModelId` come from `ChatClientMetadata` and the response's model id; a provider client that exposes neither yields `null` identity in `UsageByModel` (usage is still counted).

## See also

- [Getting Started](getting-started.md) — reading the report from a response or an observer.
- [Architecture](architecture.md) — how scopes are created and joined.
- [Status Events](status-events.md) — the `UsageReported` event and the final in-band usage update.
- [Configuration](configuration.md) — `EnableFinalUsageUpdate` and related options.
