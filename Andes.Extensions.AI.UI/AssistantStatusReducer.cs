namespace Andes.Extensions.AI;

/// <summary>
/// Folds a sequence of <see cref="AssistantUiEvent"/> deltas into successive immutable
/// <see cref="AssistantStatusSnapshot"/> values, reconstructing the activity hierarchy from the
/// events' <see cref="AssistantUiEvent.ScopeId"/> and <see cref="AssistantUiEvent.ParentScopeId"/>.
/// </summary>
/// <remarks>
/// A reducer is stateful and single-consumer: feed it the events of one request in order (the core
/// stream already serializes them). It is the C# counterpart of the TypeScript
/// <c>foldAssistantEvents</c> function shipped with this package, so a Blazor app and a SPA render
/// the same tree from the same events.
/// </remarks>
/// <example>
/// <code language="csharp">
/// var reducer = new AssistantStatusReducer();
/// await foreach (AssistantUiEvent uiEvent in events)
/// {
///     AssistantStatusSnapshot snapshot = reducer.Apply(uiEvent);
///     Render(snapshot);
/// }
/// </code>
/// </example>
public sealed class AssistantStatusReducer
{
    private readonly Dictionary<string, MutableActivity> _byScope = [];
    private readonly List<MutableActivity> _roots = [];
    private string? _assistantStatus;
    private ActivityState _phase = ActivityState.Running;
    private string? _text;
    private string? _reasoningText;
    private UsageSummary? _usage;
    private IReadOnlyDictionary<string, string>? _metadata;

    /// <summary>
    /// Applies one event to the accumulated state and returns the resulting snapshot.
    /// </summary>
    /// <remarks>
    /// Application-supplied <see cref="AssistantUiEvent.Metadata"/> merges into the snapshot's
    /// <see cref="AssistantStatusSnapshot.Metadata"/> before the kind-specific handling — last
    /// write per key wins, <see langword="null"/> and empty bags are no-ops — so every kind,
    /// including future ones this reducer does not recognize, still contributes its values. The
    /// TypeScript <c>foldAssistantEvents</c> applies the same rule.
    /// </remarks>
    /// <param name="uiEvent">The event to fold in.</param>
    /// <returns>An immutable snapshot reflecting every event applied so far.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uiEvent"/> is <see langword="null"/>.</exception>
    public AssistantStatusSnapshot Apply(AssistantUiEvent uiEvent)
    {
        ArgumentNullException.ThrowIfNull(uiEvent);

        // Metadata merges before the kind switch so every event — including kinds this reducer
        // does not yet recognize — contributes its application-supplied values. The TypeScript
        // foldAssistantEvents applies the same rule, keeping the two folds in lockstep.
        MergeMetadata(uiEvent.Metadata);

        switch (uiEvent.Kind)
        {
            case AssistantUiEventKind.Status:
                _assistantStatus = uiEvent.Message;
                break;

            case AssistantUiEventKind.ActivityStarted:
                StartActivity(uiEvent);
                break;

            case AssistantUiEventKind.ActivityProgress:
                if (uiEvent.ScopeId is { } progressScope && _byScope.TryGetValue(progressScope, out MutableActivity? owner))
                {
                    owner.SubStatuses.Add(new SubStatus
                    {
                        Message = uiEvent.Message ?? string.Empty,
                        Progress = uiEvent.Progress,
                        ProgressTotal = uiEvent.ProgressTotal,
                    });
                }

                break;

            case AssistantUiEventKind.ActivityCompleted or AssistantUiEventKind.ActivityFailed:
                if (uiEvent.ScopeId is { } finishScope && _byScope.TryGetValue(finishScope, out MutableActivity? finished))
                {
                    finished.State = uiEvent.Kind == AssistantUiEventKind.ActivityFailed
                        ? ActivityState.Failed
                        : ActivityState.Completed;
                    finished.DurationSeconds = uiEvent.DurationSeconds;
                }

                break;

            case AssistantUiEventKind.TextDelta:
                _text = (_text ?? string.Empty) + uiEvent.Text;
                break;

            case AssistantUiEventKind.ReasoningDelta:
                _reasoningText = (_reasoningText ?? string.Empty) + uiEvent.Text;
                break;

            case AssistantUiEventKind.Finished:
                _phase = ActivityState.Completed;
                _usage = uiEvent.Usage;
                break;
        }

        return BuildSnapshot();
    }

    private void StartActivity(AssistantUiEvent uiEvent)
    {
        var activity = new MutableActivity
        {
            ScopeId = uiEvent.ScopeId ?? string.Empty,
            DisplayName = uiEvent.DisplayName ?? uiEvent.Source ?? uiEvent.ScopeId ?? string.Empty,
            Kind = uiEvent.ToolKind,
            Source = uiEvent.Source,
        };

        if (uiEvent.ScopeId is { } scopeId)
        {
            _byScope[scopeId] = activity;
        }

        // A top-level activity's parent is the request root, which has no card, so it becomes a root.
        if (uiEvent.ParentScopeId is { } parentId && _byScope.TryGetValue(parentId, out MutableActivity? parent))
        {
            parent.Children.Add(activity);
        }
        else
        {
            _roots.Add(activity);
        }
    }

    private void MergeMetadata(IReadOnlyDictionary<string, string>? eventMetadata)
    {
        if (eventMetadata is not { Count: > 0 })
        {
            return;
        }

        // Copy-on-write: a fresh dictionary per merge keeps already-published snapshots immutable
        // and never aliases the caller-owned event dictionary. Metadata-free events cost nothing.
        Dictionary<string, string> merged = _metadata is { Count: > 0 } current
            ? new Dictionary<string, string>(current)
            : new Dictionary<string, string>(eventMetadata.Count);

        foreach (KeyValuePair<string, string> pair in eventMetadata)
        {
            merged[pair.Key] = pair.Value;
        }

        _metadata = merged;
    }

    private AssistantStatusSnapshot BuildSnapshot()
    {
        return new AssistantStatusSnapshot
        {
            AssistantStatus = _assistantStatus,
            Phase = _phase,
            Activities = [.. _roots.Select(root => root.ToImmutable())],
            Text = _text,
            ReasoningText = _reasoningText,
            Usage = _usage,
            Metadata = _metadata,
        };
    }

    private sealed class MutableActivity
    {
        public required string ScopeId { get; init; }

        public required string DisplayName { get; init; }

        public ToolKind Kind { get; init; }

        public string? Source { get; init; }

        public ActivityState State { get; set; } = ActivityState.Running;

        public double? DurationSeconds { get; set; }

        public List<SubStatus> SubStatuses { get; } = [];

        public List<MutableActivity> Children { get; } = [];

        public AssistantActivity ToImmutable()
        {
            return new AssistantActivity
            {
                ScopeId = ScopeId,
                DisplayName = DisplayName,
                Kind = Kind,
                Source = Source,
                State = State,
                DurationSeconds = DurationSeconds,
                SubStatuses = [.. SubStatuses],
                Children = [.. Children.Select(child => child.ToImmutable())],
            };
        }
    }
}
