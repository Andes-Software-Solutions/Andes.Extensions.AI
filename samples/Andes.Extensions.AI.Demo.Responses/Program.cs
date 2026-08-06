using System.ClientModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Andes.Extensions.AI;
using Andes.Extensions.AI.Demo.Responses;
using Microsoft.Extensions.AI;
using OpenAI;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;

var settings = AzureOpenAISettings.Load();
if (!settings.IsConfigured)
{
    AnsiConsole.Write(new Panel(new Markup(
            "[yellow]Azure OpenAI is not configured.[/]\n\n" +
            "Copy [bold]appsettings.sample.json[/] to [bold]appsettings.json[/] next to this project\n" +
            "and fill in the [bold]AzureOpenAI[/] section ([grey]Endpoint, ApiKey, Deployment[/]).\n" +
            "The deployment must be [bold]reasoning-capable[/] (gpt-5 family / o-series)."))
        .Header("Andes.Extensions.AI Responses demo")
        .BorderColor(Color.Yellow));
    return;
}

// Azure OpenAI's OpenAI-v1-compatible endpoint (https://{resource}.openai.azure.com/openai/v1)
// lets the plain OpenAIClient reach the Responses API with stable packages — the stable
// Azure.AI.OpenAI client has no Responses surface. The deployment name doubles as the model id.
// The one ordering invariant: UseToolTracking BEFORE UseFunctionInvocation, so the tracker
// wraps the tools the invoker executes and observes the merged stream from outside the loop.
IChatClient client = new OpenAIClient(
        new ApiKeyCredential(settings.ApiKey!),
        new OpenAIClientOptions { Endpoint = new Uri(settings.Endpoint!.TrimEnd('/') + "/openai/v1") })
    .GetResponsesClient()
    .AsIChatClient(settings.Deployment!)
    .AsBuilder()
    .UseToolTracking()
    .UseFunctionInvocation()
    .Build();

// Reasoning summaries stream back as TextReasoningContent — the trigger for the middleware's
// Reasoning status. Full output maps to Responses summary verbosity "detailed". Not Summary:
// M.E.AI maps it to "concise", which gpt-5-series deployments reject (supported: auto,
// detailed) — no summaries stream and the Reasoning status never fires. No Effort override:
// ExtraHigh maps to "xhigh", accepted only by gpt-5.1+. No Temperature: reasoning models
// reject non-default values.
var chatOptions = new ChatOptions
{
    Reasoning = new ReasoningOptions { Output = ReasoningOutput.Full },
    Tools =
    [
        AIFunctionFactory.Create(ResponsesDemoTools.GetWeather),
        AIFunctionFactory.Create(ResponsesDemoTools.ConvertTemperature),
    ],
};

AnsiConsole.Write(new Rule("[bold]Andes.Extensions.AI[/] [dim]responses demo[/]").LeftJustified());
AnsiConsole.MarkupLine("[dim]The Responses API pipeline: the header flips \"Reasoning...\" → \"Reasoning completed\" as summaries stream,[/]");
AnsiConsole.MarkupLine("[dim]and the final frame keeps the full reasoning (with the measured time), the tool calls, and the answer.[/]");
AnsiConsole.MarkupLine("[dim]Try:[/]  [italic]Get the weather in Quito, then convert the high to Fahrenheit.[/]");
AnsiConsole.MarkupLine("[dim]     [/] [italic]What is the sum of the first ten prime numbers? Reason it out.[/]");
AnsiConsole.MarkupLine("[dim]Press Enter on an empty line (or type 'exit') to quit.[/]");
AnsiConsole.WriteLine();

List<ChatMessage> history = [];

while (true)
{
    string prompt = ReadPrompt();
    if (string.IsNullOrWhiteSpace(prompt) || prompt.Trim().ToLowerInvariant() is "exit" or "quit")
    {
        break;
    }

    // Checkpoint so a failed turn can roll back everything it added to history.
    int checkpoint = history.Count;
    history.Add(new ChatMessage(ChatRole.User, prompt));
    List<ChatResponseUpdate> updates = [];

    // Tee: record the raw updates for chat history while the same stream drives the renderer.
    async IAsyncEnumerable<ChatResponseUpdate> StreamTurn(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The middleware never emits request-start statuses — the app owns them via the Custom
        // kind. Prepended outside the recording loop, the status drives the Live header
        // immediately without ever entering the chat history or the usage report.
        yield return ChatProgressUpdate.CreateCustom("Starting request").ToResponseUpdate();

        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(history, chatOptions, cancellationToken))
        {
            updates.Add(update);
            yield return update;
        }
    }

    AssistantStatusSnapshot? last = null;

    // Snapshots arrive per text delta; throttle redraws to stay flicker-free.
    async Task ConsumeAsync(Action<AssistantStatusSnapshot>? render)
    {
        var throttle = Stopwatch.StartNew();
        await foreach (AssistantStatusSnapshot snapshot in StreamTurn().ToStatusSnapshotsAsync())
        {
            last = snapshot;
            if (render is not null && throttle.ElapsedMilliseconds >= 80)
            {
                render(snapshot);
                throttle.Restart();
            }
        }
    }

    try
    {
        if (AnsiConsole.Profile.Capabilities.Interactive)
        {
            await AnsiConsole.Live(Text.Empty)
                .AutoClear(true)
                .Overflow(VerticalOverflow.Ellipsis)
                .StartAsync(context => ConsumeAsync(snapshot => context.UpdateTarget(StatusRenderer.RenderLive(snapshot))));
        }
        else
        {
            // Redirected output (scripts, CI): the Live region needs a real terminal, so only
            // the persistent final frame below is rendered.
            await ConsumeAsync(render: null);
        }

        // The last snapshot already carries the completed phase, the full reasoning text, and
        // total usage. The recorded raw updates additionally carry the middleware's
        // ReasoningCompleted statuses — summing their Duration (one per model turn) gives the
        // total time the model spent reasoning, shown on the final frame's reasoning panel.
        if (last is not null)
        {
            TimeSpan? reasoningDuration = updates
                .SelectMany(update => update.Contents)
                .OfType<ChatProgressContent>()
                .Select(content => content.Progress)
                .Where(progress => progress.Kind == ChatProgressKind.ReasoningCompleted)
                .Aggregate(
                    default(TimeSpan?),
                    (total, progress) => progress.Duration is { } duration ? (total ?? TimeSpan.Zero) + duration : total);

            AnsiConsole.Write(StatusRenderer.RenderFinal(last, reasoningDuration));
        }

        // Strip only the synthetic progress/usage content before history re-enters the next
        // request. TextReasoningContent is deliberately kept: the Responses API expects prior
        // reasoning items to be replayed across tool round-trips and follow-up turns.
        history.AddRange(updates.ToChatResponse().StripProgressContent().Messages);
    }
    catch (Exception exception)
    {
        history.RemoveRange(checkpoint, history.Count - checkpoint);
        AnsiConsole.Write(StatusRenderer.RenderFailed(last, exception));
    }

    AnsiConsole.WriteLine();
}

static string ReadPrompt()
{
    if (AnsiConsole.Profile.Capabilities.Interactive)
    {
        return AnsiConsole.Prompt(new TextPrompt<string>("[bold green]›[/]").AllowEmpty());
    }

    // Piped or redirected input (scripts, CI): Spectre's interactive prompt would throw,
    // so fall back to plain line reading. Null at end-of-input exits the loop.
    AnsiConsole.Markup("[bold green]›[/] ");
    return Console.ReadLine() ?? string.Empty;
}
