using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Andes.Extensions.AI;
using Andes.Extensions.AI.Demo;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;

AzureOpenAISettings settings = AzureOpenAISettings.Load();
if (!settings.IsConfigured)
{
    AnsiConsole.Write(new Panel(new Markup(
            "[yellow]Azure OpenAI is not configured.[/]\n\n" +
            "Copy [bold]appsettings.sample.json[/] to [bold]appsettings.json[/] next to this project\n" +
            "and fill in the [bold]AzureOpenAI[/] section ([grey]Endpoint, ApiKey, Deployment[/])."))
        .Header("Andes.Extensions.AI demo")
        .BorderColor(Color.Yellow));
    return;
}

await using DemoMcpServer mcp = await DemoMcpServer.StartAsync();

// The one ordering invariant: UseToolTracking BEFORE UseFunctionInvocation, so the tracker
// wraps the tools the invoker executes and observes the merged stream from outside the loop.
IChatClient client = new AzureOpenAIClient(
        new Uri(settings.Endpoint!),
        new AzureKeyCredential(settings.ApiKey!))
    .GetChatClient(settings.Deployment!)
    .AsIChatClient()
    .AsBuilder()
    .UseToolTracking(options =>
    {
        options.UseMcpToolClassification();
        options.UseAgentToolClassification();
        // Out-of-band alternative: options.Observers.Add(new MyProgressObserver());
        // Skipped here — a console-writing observer would interleave with the Live region.
    })
    .UseFunctionInvocation()
    .Build();

// The Packing Agent is shared by both nesting scenarios: as a tool of the Research Agent
// (agent inside agent) and invoked inside the PlanTrip tool body (agent inside a function).
// Either way it opens its own child scope and renders as a nested activity card.
AIFunction packingAgent = DemoAgents.CreatePackingAgent(settings).WithTracking();

// No Temperature: reasoning-model deployments reject non-default values.
var chatOptions = new ChatOptions
{
    Reasoning = new ReasoningOptions
    {
        Effort = ReasoningEffort.ExtraHigh,
        Output = ReasoningOutput.Full,
    },
    Tools =
    [
        AIFunctionFactory.Create(DemoTools.GetWeather),
        DemoTools.CreatePlanTrip(packingAgent),
        .. mcp.Tools.WithTracking(mcp.Client),
        DemoAgents.CreateResearchAgent(settings, packingAgent).WithTracking(reportFunctionCalls: true),
    ],
};

AnsiConsole.Write(new Rule("[bold]Andes.Extensions.AI[/] [dim]demo[/]").LeftJustified());
AnsiConsole.MarkupLine("[dim]A tracked IChatClient pipeline rendered live from Andes.Extensions.AI.UI snapshots.[/]");
AnsiConsole.MarkupLine("[dim]Try:[/]  [italic]Get the weather in Quito and the 5-day forecast.[/]");
AnsiConsole.MarkupLine("[dim]     [/] [italic]Ask the Research Agent what to pack for Quito.[/] [dim](agent nested in an agent)[/]");
AnsiConsole.MarkupLine("[dim]     [/] [italic]Plan a trip to Cusco.[/] [dim](agent nested in a plain tool)[/]");
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

        // The final frame renders the report-derived tree (per-activity tokens live only there)
        // merged with the live snapshot's text and sub-statuses.
        ChatUsageReport? report = updates
            .SelectMany(update => update.Contents)
            .OfType<UsageReportContent>()
            .FirstOrDefault()?.Report;
        if (FinalSnapshot.Merge(report, last) is { } final)
        {
            AnsiConsole.Write(StatusRenderer.RenderFinal(final));
        }

        // Strip in-band progress/usage content before it re-enters the next request.
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
