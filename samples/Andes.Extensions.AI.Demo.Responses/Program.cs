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

AzureOpenAISettings settings = AzureOpenAISettings.Load();
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

// Summary output is what streams back as TextReasoningContent — the trigger for the middleware's
// Reasoning status. No Temperature: reasoning models reject non-default values.
var chatOptions = new ChatOptions
{
    Reasoning = new ReasoningOptions { Output = ReasoningOutput.Summary },
    Tools =
    [
        AIFunctionFactory.Create(ResponsesDemoTools.GetWeather),
        AIFunctionFactory.Create(ResponsesDemoTools.ConvertTemperature),
    ],
};

AnsiConsole.Write(new Rule("[bold]Andes.Extensions.AI[/] [dim]responses demo[/]").LeftJustified());
AnsiConsole.MarkupLine("[dim]The Responses API pipeline: watch the header switch to \"Reasoning...\" as summaries stream.[/]");
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
        // The middleware no longer auto-emits request-start statuses — the app owns them.
        // Prepended outside the recording loop, the status drives the Live header immediately
        // without ever entering the chat history or the usage report.
        yield return ChatProgressUpdate.CreateRequestStarted().ToResponseUpdate();

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

        // The last snapshot already carries the completed phase and total usage from the
        // trailing Finished event; see the main Demo's FinalSnapshot for the report-merge
        // pattern that adds per-activity token usage.
        if (last is not null)
        {
            AnsiConsole.Write(StatusRenderer.RenderFinal(last));
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
