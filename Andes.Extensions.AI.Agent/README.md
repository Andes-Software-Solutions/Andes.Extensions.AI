# Andes.Extensions.AI.Agent

Microsoft Agent Framework support for [Andes.Extensions.AI](https://www.nuget.org/packages/Andes.Extensions.AI) tool tracking. Adds two things on top of the core middleware:

- **Classification** — agents wrapped with `WithTracking()` are recognized as `ToolKind.Agent` and render with the `"Calling {Agent} Agent"` header in progress updates and usage reports.
- **Usage capture** — each agent run's `AgentResponse.Usage` is attributed to the calling tool's scope, so hosted or remote agents' token usage lands in the `ChatUsageReport` even when their pipeline cannot be instrumented.

## Install

```bash
dotnet add package Andes.Extensions.AI.Agent
```

## Quickstart

```csharp
using Andes.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

AIAgent weatherAgent = weatherChatClient.AsAIAgent(
    instructions: "You answer questions about the weather.",
    name: "Weather Agent",
    description: "Answers questions about the weather.",
    tools: [AIFunctionFactory.Create(GetWeather)]);

IChatClient client = innerClient
    .AsBuilder()
    .UseToolTracking(options => options.UseAgentToolClassification())
    .UseFunctionInvocation() // tracking must be registered before function invocation
    .Build();

var chatOptions = new ChatOptions
{
    Tools = [weatherAgent.WithTracking()],
};

await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync("prompt", chatOptions))
{
    foreach (ChatProgressContent progress in update.Contents.OfType<ChatProgressContent>())
    {
        // e.g. "Calling Weather Agent" (header), then any statuses the agent's tools report
        Console.WriteLine(progress.Progress.Message);
    }
}
```

`WithTracking` carries the agent identity used for classification and attributes each run's usage to the calling tool's scope. Pass `trackUsage: false` when the agent's own chat pipeline uses `UseToolTracking()` — the nested pipeline already rolls its total usage up, and reporting `AgentResponse.Usage` on top would double-count. Pass `reportFunctionCalls: true` to additionally report a `"Calling {Function} Tool"` status each time the agent invokes one of its function tools (local function-invoking agents only; names only).

## Notes

- A function created by a plain `agent.AsAIFunction()` call exposes neither its agent nor its usage; without `WithTracking` it classifies as a regular function tool. The same applies to agents hidden inside your own `DelegatingAIFunction` wrappers, which this package never unwraps or bypasses.
- The agent runs in-process on the caller's async flow, so statuses its tools report through `ChatProgress.Report(...)` surface beneath the agent's header automatically.
- Usage capture reports whatever the agent implementation surfaces on `AgentResponse.Usage`; agents that report no usage attribute nothing.
- Privacy posture matches the core package: progress events never carry prompt content, tool arguments, or tool results.

Full documentation lives in the [repository docs](https://github.com/RorroRojas3/Enterprise.AI/tree/main/docs).
