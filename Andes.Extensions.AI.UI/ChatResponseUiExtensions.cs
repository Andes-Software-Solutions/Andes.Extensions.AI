using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Projects a tracked chat stream (the in-band <see cref="ChatProgressContent"/> and
/// <see cref="UsageReportContent"/> emitted by <c>UseToolTracking()</c>, plus answer text) into the
/// serializable UI contract: <see cref="AssistantUiEvent"/> deltas and
/// <see cref="AssistantStatusSnapshot"/> snapshots.
/// </summary>
/// <remarks>
/// These projections derive each activity's clean <see cref="AssistantActivity.DisplayName"/> from
/// the raw tool/server/agent name rather than the composed progress header, so the kind word is
/// never repeated and the label localizes cleanly.
/// </remarks>
public static class ChatResponseUiExtensions
{
    /// <summary>
    /// Translates a tracked streaming response into a stream of <see cref="AssistantUiEvent"/>
    /// deltas — one per progress flush, per answer-text chunk, and one final
    /// <see cref="AssistantUiEventKind.Finished"/> event.
    /// </summary>
    /// <param name="updates">The tracked streaming response.</param>
    /// <param name="cancellationToken">A token to cancel enumeration.</param>
    /// <returns>The projected UI events, in stream order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="updates"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code language="csharp">
    /// await foreach (AssistantUiEvent uiEvent in client
    ///     .GetStreamingResponseAsync(prompt, chatOptions)
    ///     .ToUiEventsAsync())
    /// {
    ///     await WriteServerSentEventAsync(uiEvent);
    /// }
    /// </code>
    /// </example>
    public static async IAsyncEnumerable<AssistantUiEvent> ToUiEventsAsync(
        this IAsyncEnumerable<ChatResponseUpdate> updates,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);

        await foreach (ChatResponseUpdate update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            foreach (AIContent content in update.Contents)
            {
                switch (content)
                {
                    case ChatProgressContent progress:
                        yield return progress.Progress.ToUiEvent();
                        break;

                    case UsageReportContent usage:
                        yield return ToFinishedEvent(usage.Report);
                        break;
                }
            }

            if (update.Text is { Length: > 0 } text)
            {
                yield return new AssistantUiEvent
                {
                    Kind = AssistantUiEventKind.TextDelta,
                    Text = text,
                };
            }
        }
    }

    /// <summary>
    /// Translates a tracked streaming response into a stream of immutable
    /// <see cref="AssistantStatusSnapshot"/> values by folding its events through an
    /// <see cref="AssistantStatusReducer"/> — one snapshot per event.
    /// </summary>
    /// <param name="updates">The tracked streaming response.</param>
    /// <param name="cancellationToken">A token to cancel enumeration.</param>
    /// <returns>The successive snapshots, each reflecting every event so far.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="updates"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The final snapshot reaches <see cref="ActivityState.Completed"/> when the request finishes.
    /// Request-level failure is not observable here: the core raises it out-of-band to observers
    /// only, so a faulted request's last in-band snapshot stays at <see cref="ActivityState.Running"/>
    /// rather than <see cref="ActivityState.Failed"/>.
    /// </remarks>
    public static async IAsyncEnumerable<AssistantStatusSnapshot> ToStatusSnapshotsAsync(
        this IAsyncEnumerable<ChatResponseUpdate> updates,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);

        var reducer = new AssistantStatusReducer();
        await foreach (AssistantUiEvent uiEvent in updates.ToUiEventsAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return reducer.Apply(uiEvent);
        }
    }

    /// <summary>
    /// Projects a single core progress update into an <see cref="AssistantUiEvent"/>, deriving the
    /// clean <see cref="AssistantUiEvent.DisplayName"/> from the tool source or name.
    /// </summary>
    /// <param name="update">The progress update to project.</param>
    /// <returns>The equivalent UI event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="update"/> is <see langword="null"/>.</exception>
    public static AssistantUiEvent ToUiEvent(this ChatProgressUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        return new AssistantUiEvent
        {
            Kind = MapKind(update.Kind),
            Message = update.Message,
            ScopeId = update.ScopeId,
            ParentScopeId = update.ParentScopeId,
            Depth = update.Depth,
            ToolKind = update.ToolKind,
            DisplayName = update.ToolSource ?? update.ToolName,
            Source = update.ToolSource,
            Progress = update.Progress,
            ProgressTotal = update.ProgressTotal,
            DurationSeconds = update.Duration?.TotalSeconds,
            Timestamp = update.Timestamp,
        };
    }

    /// <summary>
    /// Flattens Microsoft.Extensions.AI token usage into a serialization-friendly
    /// <see cref="UsageSummary"/>.
    /// </summary>
    /// <param name="usage">The usage details to flatten.</param>
    /// <returns>The flattened usage summary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="usage"/> is <see langword="null"/>.</exception>
    public static UsageSummary ToUsageSummary(this UsageDetails usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        return new UsageSummary
        {
            InputTokens = usage.InputTokenCount,
            OutputTokens = usage.OutputTokenCount,
            TotalTokens = usage.TotalTokenCount,
        };
    }

    /// <summary>
    /// Folds a completed usage report into an <see cref="AssistantStatusSnapshot"/>, materializing
    /// the per-activity tree (including nested children) with token usage attributed per activity.
    /// </summary>
    /// <param name="report">The final usage report.</param>
    /// <returns>A completed snapshot built from the report's tool-call tree.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is <see langword="null"/>.</exception>
    public static AssistantStatusSnapshot ToSnapshot(this ChatUsageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new AssistantStatusSnapshot
        {
            Phase = ActivityState.Completed,
            Activities = [.. report.ToolCalls.Select(ToActivity)],
            Usage = report.TotalUsage.ToUsageSummary(),
        };
    }

    private static AssistantUiEvent ToFinishedEvent(ChatUsageReport report)
    {
        return new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.Finished,
            Usage = report.TotalUsage.ToUsageSummary(),
            DurationSeconds = report.Duration.TotalSeconds,
        };
    }

    private static AssistantActivity ToActivity(ToolCallUsage call)
    {
        return new AssistantActivity
        {
            ScopeId = call.CallId ?? call.ToolName,
            DisplayName = call.Source ?? call.ToolName,
            Kind = call.Kind,
            Source = call.Source,
            State = call.Succeeded ? ActivityState.Completed : ActivityState.Failed,
            DurationSeconds = call.Duration.TotalSeconds,
            SubStatuses = [],
            Children = [.. call.Children.Select(ToActivity)],
            Usage = call.Usage?.ToUsageSummary(),
        };
    }

    private static AssistantUiEventKind MapKind(ChatProgressKind kind)
    {
        return kind switch
        {
            ChatProgressKind.ToolInvoking => AssistantUiEventKind.ActivityStarted,
            ChatProgressKind.ToolProgress => AssistantUiEventKind.ActivityProgress,
            ChatProgressKind.ToolCompleted => AssistantUiEventKind.ActivityCompleted,
            ChatProgressKind.ToolFailed => AssistantUiEventKind.ActivityFailed,
            _ => AssistantUiEventKind.Status,
        };
    }
}
