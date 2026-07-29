# Example: The UI Contract, Three Ways

This example takes one tracked `IChatClient` pipeline and shows the [`Andes.Extensions.AI.UI`](../ui.md) contract landing on three different consumer surfaces without changing shape at any hop:

1. **An ASP.NET Core minimal API** streams `AssistantUiEvent` as JSON over server-sent events, serialized with the package's own `AssistantUiJsonContext`.
2. **A Blazor WebAssembly component** consumes that same stream directly in C# — no second serialization format, no hand-rolled DTOs — and folds it into `AssistantStatusSnapshot` with the same `AssistantStatusReducer` a console app would use.
3. **A TypeScript SPA** consumes the identical stream with the browser's native `EventSource`, `JSON.parse`s each payload into the shipped `AssistantUiEvent` interface, and folds it with `foldAssistantEvents` — the TypeScript twin of the C# reducer.

The point isn't the tool-tracking pipeline itself — [Getting started](../getting-started.md) and the [Progress Board example](progress-board.md) already cover a pipeline with all three tool kinds side by side. The point is that **the wire format between step 1 and steps 2/3 is one small, versioned, serializable contract**, so a Blazor app and a hand-rolled SPA render the same activity tree from the same bytes.

## A. The ASP.NET Core producer

Any tracked pipeline works — this one keeps a single reporting function tool so the listing stays focused on the streaming plumbing, not the tools. Swap in MCP tools or an agent-as-tool exactly as the [Progress Board example](progress-board.md#the-pipeline) does; nothing downstream changes.

```csharp
using Andes.Extensions.AI;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// Register the UI contract's source-generated context so minimal APIs serialize AssistantUiEvent
// exactly the way AssistantUiJsonContext documents: camelCase, string enums, nulls omitted --
// the same JSON the shipped TypeScript file and the Blazor client below both expect.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AssistantUiJsonContext.Default));

builder.Services.AddSingleton<IChatClient>(_ =>
{
    IChatClient innerClient = CreateProviderClient(); // any IChatClient: Azure OpenAI, OpenAI, Ollama, ...
    return innerClient
        .AsBuilder()
        .UseToolTracking()
        .UseFunctionInvocation() // tracking before function invocation (core invariant)
        .Build();
});

WebApplication app = builder.Build();

app.MapGet("/chat/stream", (string prompt, IChatClient client, CancellationToken cancellationToken) =>
{
    AIFunction forecast = AIFunctionFactory.Create(
        (string city) =>
        {
            ChatProgress.Report("Contacting the forecast service…");
            ChatProgress.Report("Crunching the numbers…", progress: 2, progressTotal: 3);
            return $"Sunny in {city} all week.";
        },
        "GetForecast");

    var chatOptions = new ChatOptions { Tools = [forecast] };

    IAsyncEnumerable<AssistantUiEvent> events = client
        .GetStreamingResponseAsync(prompt, chatOptions, cancellationToken)
        .ToUiEventsAsync(cancellationToken);

    // TypedResults.ServerSentEvents (.NET 10) writes "event: assistant-ui\ndata: {...}\n\n" per item,
    // using the JsonOptions configured above -- no manual header setting or line encoding needed.
    return TypedResults.ServerSentEvents(events, eventType: "assistant-ui");
});

app.Run();

static IChatClient CreateProviderClient() =>
    throw new NotImplementedException("Plug in your provider client (Azure OpenAI, OpenAI, Ollama, ...).");
```

Three things to notice:

- **`ToUiEventsAsync(cancellationToken)` is the only translation step.** The endpoint never touches `ChatProgressContent` or `UsageReportContent` directly — the mapper already turned them into flat `AssistantUiEvent` values in stream order.
- **One `ConfigureHttpJsonOptions` call wires the whole contract.** `TypedResults.ServerSentEvents<T>` serializes each `SseItem<T>.Data` with the `JsonOptions` resolved from `HttpContext.RequestServices`; inserting `AssistantUiJsonContext.Default` at the front of the `TypeInfoResolverChain` makes it use the contract's source-generated, trim-safe metadata instead of reflection.
- **The named SSE event (`"assistant-ui"`) lets consumers filter cheaply.** Both consumers below listen for that event name specifically, so a page that also multiplexes other SSE traffic (heartbeats, unrelated notifications) never has to inspect payloads it doesn't care about.

## B. The Blazor WebAssembly consumer

Two standalone components: a container that owns the stream and folds it, and a small recursive card that renders one `AssistantActivity` — including its own children, so nesting needs no per-depth markup.

`AssistantStatusBoard.razor` opens the stream, feeds every event through an `AssistantStatusReducer`, and re-renders once per folded snapshot:

```razor
@implements IAsyncDisposable
@inject HttpClient Http
@using System.Net.ServerSentEvents
@using System.Text.Json

<div class="assistant-status-board">
    <p class="assistant-status-line">@_snapshot.AssistantStatus</p>

    @foreach (AssistantActivity activity in _snapshot.Activities)
    {
        <ActivityCard Activity="activity" />
    }

    @if (_snapshot.Text is { Length: > 0 } text)
    {
        <p class="assistant-answer">@text</p>
    }

    @if (_snapshot.Usage is { TotalTokens: { } tokens })
    {
        <p class="assistant-usage">@tokens tokens total</p>
    }
</div>

@code {
    [Parameter, EditorRequired]
    public required string Prompt { get; set; }

    private readonly AssistantStatusReducer _reducer = new();
    private AssistantStatusSnapshot _snapshot = new();
    private CancellationTokenSource? _cts;

    protected override void OnInitialized()
    {
        _cts = new CancellationTokenSource();

        // Fire-and-forget by design: the loop below drives StateHasChanged itself, and disposal
        // (below) cancels the token that stops it -- the standard pattern for a streaming component.
        _ = StreamAsync(_cts.Token);
    }

    private async Task StreamAsync(CancellationToken cancellationToken)
    {
        // Blazor WebAssembly streams HttpClient responses by default as of .NET 10, so this reads
        // the server-sent events incrementally rather than waiting for the whole response to buffer.
        using Stream stream = await Http.GetStreamAsync(
            $"chat/stream?prompt={Uri.EscapeDataString(Prompt)}", cancellationToken);

        SseParser<AssistantUiEvent> parser = SseParser.Create(
            stream,
            (_, data) => JsonSerializer.Deserialize(data, AssistantUiJsonContext.Default.AssistantUiEvent)!);

        await foreach (SseItem<AssistantUiEvent> item in parser.EnumerateAsync(cancellationToken))
        {
            // One fold, one re-render, per event -- never per raw byte chunk the transport delivers.
            _snapshot = _reducer.Apply(item.Data);
            await InvokeAsync(StateHasChanged);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }
    }
}
```

`ActivityCard.razor` is the recursive leaf — one component renders every depth of the tree, so there is exactly one place that knows how to draw a card:

```razor
@* Renders one activity card, then its own Children with the same component -- the recursion is
   what keeps a three-level agent-with-tools tree from needing three near-duplicate templates. *@
@using System.Globalization

<div class="activity-card activity-card--@Activity.State.ToString().ToLowerInvariant()">
    <header>
        <span class="activity-name">@Activity.DisplayName</span>
        <span class="activity-badge">@Activity.Kind</span>
        @if (Activity.DurationSeconds is { } seconds)
        {
            <span class="activity-duration">@seconds.ToString("0.0", CultureInfo.InvariantCulture)s</span>
        }
    </header>

    @if (Activity.SubStatuses.Count > 0)
    {
        <ul class="activity-substatuses">
            @foreach (SubStatus sub in Activity.SubStatuses)
            {
                <li>@FormatSubStatus(sub)</li>
            }
        </ul>
    }

    @if (Activity.Children.Count > 0)
    {
        <div class="activity-children">
            @foreach (AssistantActivity child in Activity.Children)
            {
                <ActivityCard Activity="child" />
            }
        </div>
    }
</div>

@code {
    [Parameter, EditorRequired]
    public required AssistantActivity Activity { get; set; }

    private static string FormatSubStatus(SubStatus sub)
    {
        if (sub.Progress is not { } progress)
        {
            return sub.Message;
        }

        string value = progress.ToString("0.#", CultureInfo.InvariantCulture);
        return sub.ProgressTotal is { } total
            ? $"{sub.Message} ({value}/{total.ToString("0.#", CultureInfo.InvariantCulture)})"
            : $"{sub.Message} ({value})";
    }
}
```

Two design notes on rendering cost, the Blazor equivalent of an `OnPush` change-detection strategy:

- **`AssistantStatusReducer` rebuilds the whole activity tree on every `Apply` call** (it folds into new immutable records, never mutates in place), so every `ActivityCard`'s `Activity` parameter is a new reference on every event — reference-equality change detection would gain nothing here, because it would never short-circuit. `ActivityCard` is a plain, stateless, presentational component instead: Blazor's own render-tree diffing already skips real DOM writes for markup that comes out identical, which is where the actual savings live for a card whose fields didn't change.
- **`StateHasChanged()` is called exactly once per folded snapshot**, not once per raw chunk the transport delivers or per `SseItem` field — `AssistantStatusBoard` is the only place that decides when to re-render, and it decides once per `AssistantUiEvent`. That single call re-renders the whole card tree in one pass, which is cheap because Blazor diffs virtual output, not because any single card was skipped.

## C. The TypeScript SPA consumer

The browser's built-in `EventSource` already understands the wire format the producer writes — no fetch-and-parse plumbing needed, and no dependency beyond the file the package ships:

```typescript
import {
  createInitialSnapshot,
  foldAssistantEvents,
  type AssistantUiEvent,
  type AssistantStatusSnapshot,
} from "./andes-assistant-ui";

let snapshot: AssistantStatusSnapshot = createInitialSnapshot();

export function streamAssistantReply(prompt: string, render: (snapshot: AssistantStatusSnapshot) => void): void {
  const source = new EventSource(`/chat/stream?prompt=${encodeURIComponent(prompt)}`);

  // The producer names its events "assistant-ui" (TypedResults.ServerSentEvents(events, eventType:
  // "assistant-ui")); listening for that name specifically means unrelated SSE traffic on the same
  // page -- heartbeats, other notifications -- never reaches this handler.
  source.addEventListener("assistant-ui", (message: MessageEvent<string>) => {
    const event = JSON.parse(message.data) as AssistantUiEvent;
    snapshot = foldAssistantEvents(snapshot, event);
    render(snapshot);

    if (event.kind === "Finished") {
      source.close();
    }
  });

  source.onerror = () => source.close();
}
```

Rendering the tree is a small recursive function — the TypeScript counterpart of the Blazor `ActivityCard` recursion above, and of the console `ProgressBoard`'s `RenderBox` from the [Progress Board example](progress-board.md#what-the-user-sees):

```typescript
import type { AssistantActivity, AssistantStatusSnapshot } from "./andes-assistant-ui";

export function renderSnapshot(snapshot: AssistantStatusSnapshot, root: HTMLElement): void {
  root.innerHTML = "";

  const status = document.createElement("p");
  status.className = "assistant-status-line";
  status.textContent = snapshot.assistantStatus ?? "";
  root.append(status);

  for (const activity of snapshot.activities) {
    root.append(renderActivity(activity));
  }

  if (snapshot.text) {
    const answer = document.createElement("p");
    answer.className = "assistant-answer";
    answer.textContent = snapshot.text;
    root.append(answer);
  }
}

function renderActivity(activity: AssistantActivity): HTMLElement {
  const card = document.createElement("div");
  card.className = `activity-card activity-card--${activity.state.toLowerCase()}`;

  const header = document.createElement("header");
  header.innerHTML = `
    <span class="activity-name">${activity.displayName}</span>
    <span class="activity-badge">${activity.kind}</span>
  `;
  card.append(header);

  for (const sub of activity.subStatuses) {
    const line = document.createElement("div");
    line.className = "activity-substatus";
    line.textContent =
      sub.progress != null
        ? `${sub.message} (${sub.progress.toFixed(1)}${sub.progressTotal != null ? `/${sub.progressTotal}` : ""})`
        : sub.message;
    card.append(line);
  }

  // Same recursion as the Blazor ActivityCard and the C# ProgressBoard's RenderBox: one function
  // renders every depth, so an agent's nested tool calls need no special-casing.
  for (const child of activity.children) {
    card.append(renderActivity(child));
  }

  return card;
}
```

## What each consumer needed, and what it didn't

| Consumer | Serialization | Reconstructs the tree with | Never had to |
| --- | --- | --- | --- |
| ASP.NET Core minimal API (producer) | `AssistantUiJsonContext` via `ConfigureHttpJsonOptions` | N/A — only emits flat events | Parse `ChatResponseUpdate`, know about `ChatProgressContent`/`UsageReportContent`, or hand-write SSE framing |
| Blazor WebAssembly | `System.Text.Json` + `AssistantUiJsonContext.Default.AssistantUiEvent` (same context, client side) | `AssistantStatusReducer` (C#) | Write a second parser for the same JSON shape, or hand-roll SSE framing (`SseParser` from `System.Net.ServerSentEvents` does it) |
| TypeScript SPA | `JSON.parse` (the shape matches `AssistantUiEvent` by construction) | `foldAssistantEvents` (the shipped `.ts`) | Reimplement the fold logic, or agree on a JSON shape by hand — the interface and the reducer both ship with the package |

## Notes

- **Privacy invariant, unchanged.** Every field on the wire is a header-turned-name, a status, activity metadata, or a token count — never prompt content, tool arguments, or tool results. See [UI: Privacy posture](../ui.md#privacy-posture).
- **The Blazor client and the TypeScript client consume literally the same bytes.** Neither is a "primary" implementation the other approximates — both read the identical `AssistantUiJsonContext`-produced JSON over the identical named SSE event, which is the entire point of shipping one contract instead of one C# shape and a hand-maintained TypeScript approximation of it.
- **Keep the shipped `.ts` file in step with the NuGet package version it came from.** The file travels inside the package (`typescript/andes-assistant-ui.ts`) rather than as a separate npm package specifically so a frontend and a backend on different versions don't silently drift — see [UI: The TypeScript file](../ui.md#the-typescript-file).
- **A production endpoint needs the usual hardening this example skips** — authentication on `/chat/stream`, a request size/time limit, and reconnect handling on the client (`EventSource` retries automatically with the `retry:` field; a Blazor client restarting a dropped stream needs to do so explicitly). None of that changes the contract itself.

## References

- [UI status contract](../ui.md) — the two DTO layers, the mapper, the reducer, and the clean-name design
- [Getting started](../getting-started.md) — the core pipeline and `ChatProgress.Report`
- [Example: the Progress Board](progress-board.md) — the same contract's console-rendering ancestor, and a pipeline with all three tool kinds side by side
- [What's new in ASP.NET Core in .NET 10](https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-10.0) — native `TypedResults.ServerSentEvents` support for minimal APIs
- [`SseParser<T>`](https://learn.microsoft.com/dotnet/api/system.net.serversentevents.sseparser-1) — the BCL client-side SSE parser used by the Blazor component
- [`EventSource` (MDN)](https://developer.mozilla.org/en-US/docs/Web/API/EventSource) — the browser API the TypeScript consumer uses
- [Blazor WebAssembly streaming HTTP responses](https://learn.microsoft.com/dotnet/core/compatibility/networking/10.0/default-http-streaming) — enabled by default as of .NET 10
