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

// No Temperature: reasoning-model deployments reject non-default values.
var chatOptions = new ChatOptions
{
    Tools =
    [
        AIFunctionFactory.Create(DemoTools.GetWeather),
        .. mcp.Tools.WithTracking(mcp.Client),
        DemoAgents.CreateResearchAgent(settings).WithTracking(reportFunctionCalls: true),
    ],
};

AnsiConsole.Write(new Rule("[bold]Andes.Extensions.AI[/] [dim]demo[/]").LeftJustified());
AnsiConsole.MarkupLine("[dim]A tracked IChatClient pipeline rendered live from Andes.Extensions.AI.UI snapshots.[/]");
AnsiConsole.MarkupLine("[dim]Try:[/] [italic]Get the weather in Quito, the 5-day forecast, and ask the Research Agent what to pack.[/]");
AnsiConsole.MarkupLine("[dim]Press Enter on an empty line (or type 'exit') to quit.[/]");
AnsiConsole.WriteLine();

List<ChatMessage> history = [];

while (true)
{
    string prompt = AnsiConsole.Prompt(new TextPrompt<string>("[bold green]›[/]").AllowEmpty());
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
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(history, chatOptions, cancellationToken))
        {
            updates.Add(update);
            yield return update;
        }
    }

    AssistantStatusSnapshot? last = null;
    try
    {
        await AnsiConsole.Live(Text.Empty)
            .AutoClear(true)
            .Overflow(VerticalOverflow.Ellipsis)
            .StartAsync(async context =>
            {
                // Snapshots arrive per text delta; throttle redraws to stay flicker-free.
                var throttle = Stopwatch.StartNew();
                await foreach (AssistantStatusSnapshot snapshot in StreamTurn().ToStatusSnapshotsAsync())
                {
                    last = snapshot;
                    if (throttle.ElapsedMilliseconds >= 80)
                    {
                        context.UpdateTarget(StatusRenderer.RenderLive(snapshot));
                        throttle.Restart();
                    }
                }
            });

        if (last is not null)
        {
            AnsiConsole.Write(StatusRenderer.RenderFinal(last));
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
