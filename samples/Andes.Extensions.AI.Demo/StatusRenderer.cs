using Spectre.Console;
using Spectre.Console.Rendering;

namespace Andes.Extensions.AI.Demo;

/// <summary>
/// Renders <see cref="AssistantStatusSnapshot"/> instances from the UI package as
/// Claude-Code-style console frames: a status header, an activity tree with per-kind
/// badges and progress bars, the streamed answer text, and a token-usage footer.
/// </summary>
internal static class StatusRenderer
{
    public static IRenderable RenderLive(AssistantStatusSnapshot snapshot)
    {
        // The Live region cannot scroll, so only the tail of the streamed answer is shown
        // while working; RenderFinal prints the full text once the turn completes.
        const int liveTextTailLines = 10;

        var rows = new List<IRenderable> { Header(snapshot) };
        AppendActivities(rows, snapshot);
        if (!string.IsNullOrEmpty(snapshot.Text))
        {
            rows.Add(TextPanel(TailLines(snapshot.Text, liveTextTailLines)));
        }

        return new Rows(rows);
    }

    public static IRenderable RenderFinal(AssistantStatusSnapshot snapshot)
    {
        var rows = new List<IRenderable>();
        AppendActivities(rows, snapshot);
        if (!string.IsNullOrEmpty(snapshot.Text))
        {
            rows.Add(TextPanel(snapshot.Text));
        }

        if (UsageLine(snapshot.Usage) is { } usage)
        {
            rows.Add(usage);
        }

        return new Rows(rows);
    }

    public static IRenderable RenderFailed(AssistantStatusSnapshot? snapshot, Exception exception)
    {
        var rows = new List<IRenderable>();
        if (snapshot is not null)
        {
            // Request-level failure is reported out-of-band (observers only), so the last
            // snapshot still says Running — flip the running cards to failed for display.
            AppendActivities(rows, snapshot, forceRunningToFailed: true);
        }

        rows.Add(new Panel(new Markup($"[red]{Markup.Escape(exception.Message)}[/]"))
            .Header("[red]request failed[/]")
            .BorderColor(Color.Red));
        return new Rows(rows);
    }

    private static IRenderable Header(AssistantStatusSnapshot snapshot)
    {
        return snapshot.Phase switch
        {
            ActivityState.Completed => new Markup("[green]✓[/] [bold]Done[/]"),
            ActivityState.Failed => new Markup("[red]✗[/] [bold]Failed[/]"),
            _ => new Markup($"[yellow]●[/] [bold]{Markup.Escape(snapshot.AssistantStatus ?? "Working…")}[/]"),
        };
    }

    private static void AppendActivities(
        List<IRenderable> rows,
        AssistantStatusSnapshot snapshot,
        bool forceRunningToFailed = false)
    {
        if (snapshot.Activities.Count == 0)
        {
            return;
        }

        var tree = new Tree("[dim]activity[/]");
        foreach (AssistantActivity activity in snapshot.Activities)
        {
            AddActivityNode(tree, activity, forceRunningToFailed);
        }

        rows.Add(tree);
    }

    private static void AddActivityNode(IHasTreeNodes parent, AssistantActivity activity, bool forceRunningToFailed)
    {
        ActivityState state = forceRunningToFailed && activity.State == ActivityState.Running
            ? ActivityState.Failed
            : activity.State;
        string glyph = state switch
        {
            ActivityState.Completed => "[green]✓[/]",
            ActivityState.Failed => "[red]✗[/]",
            _ => "[yellow]●[/]",
        };
        string badge = activity.Kind switch
        {
            ToolKind.Function => "[white on blue] fn [/]",
            ToolKind.McpTool => "[white on purple] mcp [/]",
            ToolKind.Agent => "[black on green] agent [/]",
            _ => "[black on grey] tool [/]",
        };
        string duration = activity.DurationSeconds is { } seconds ? $" [dim]{seconds:0.0}s[/]" : string.Empty;
        string usage = activity.Usage?.TotalTokens is { } tokens ? $" [dim]· {tokens:N0} tok[/]" : string.Empty;

        TreeNode node = parent.AddNode(new Markup(
            $"{glyph} [bold]{Markup.Escape(activity.DisplayName)}[/] {badge}{duration}{usage}"));

        foreach (SubStatus subStatus in activity.SubStatuses)
        {
            node.AddNode(new Markup(SubStatusMarkup(subStatus)));
        }

        foreach (AssistantActivity child in activity.Children)
        {
            AddActivityNode(node, child, forceRunningToFailed);
        }
    }

    private static string SubStatusMarkup(SubStatus subStatus)
    {
        string text = $"[grey]{Markup.Escape(subStatus.Message)}[/]";
        if (subStatus is { Progress: { } progress, ProgressTotal: { } total } && total > 0)
        {
            const int width = 20;
            int filled = Math.Clamp((int)Math.Round(width * progress / total), 0, width);
            text += $" [green]{new string('█', filled)}[/][grey]{new string('░', width - filled)}[/]" +
                    $" [dim]{progress / total:P0}[/]";
        }

        return text;
    }

    private static IRenderable TextPanel(string text)
    {
        return new Panel(new Markup(Markup.Escape(text)))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey)
            .Header("[dim]assistant[/]");
    }

    private static string TailLines(string text, int maxLines)
    {
        string[] lines = text.Split('\n');
        if (lines.Length <= maxLines)
        {
            return text;
        }

        return "…\n" + string.Join('\n', lines[^maxLines..]);
    }

    private static IRenderable? UsageLine(UsageSummary? usage)
    {
        if (usage is null)
        {
            return null;
        }

        return new Markup(
            $"[dim]tokens: in {Format(usage.InputTokens)} · out {Format(usage.OutputTokens)} · total {Format(usage.TotalTokens)}[/]");

        static string Format(long? tokens)
        {
            return tokens?.ToString("N0") ?? "—";
        }
    }
}
