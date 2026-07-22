using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Enterprise.AI.Middleware.Tracking.Internal;

/// <summary>
/// Coordinates all tracking state for one top-level chat request: the activity scope tree,
/// per-scope and per-model usage accumulation, event publication to the observer, and in-band
/// status injection into the streaming response.
/// </summary>
internal sealed class ActivityContext
{
    private readonly IChatActivityObserver? _observer;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly string? _activityTag;
    private readonly ConcurrentDictionary<string, ScopeNode> _nodes = new();
    private readonly object _rollupGate = new();
    private readonly List<RollupEntry> _rollups = [];
    private readonly ScopeNode _rootNode;
    private readonly long _startTimestamp;
    private readonly object _completeGate = new();
    private ChannelWriter<ChatResponseUpdate>? _inBandWriter;
    private ChatActivityReport? _report;

    public ActivityContext(
        ToolTrackingOptions options,
        IChatActivityObserver? observer,
        ILogger logger,
        string? activityTag)
    {
        Options = options;
        _observer = observer;
        _logger = logger;
        _timeProvider = options.TimeProvider;
        _activityTag = activityTag;
        RequestId = Guid.NewGuid().ToString("N");
        StartedAt = _timeProvider.GetUtcNow();
        _startTimestamp = _timeProvider.GetTimestamp();
        RootScope = new ActivityScope(
            CreateScopeId(),
            parentId: null,
            ActivityScopeType.Root,
            toolKind: null,
            name: null,
            sourceName: null,
            nearestAgentName: null,
            depth: 0);
        _rootNode = new ScopeNode(RootScope, _startTimestamp);
        _nodes[RootScope.Id] = _rootNode;
    }

    public string RequestId { get; }

    public DateTimeOffset StartedAt { get; }

    public ActivityScope RootScope { get; }

    public ToolTrackingOptions Options { get; }

    public ILogger Logger => _logger;

    /// <summary>
    /// Attaches the channel the streaming pipeline reads from, enabling in-band injection of
    /// <see cref="ActivityStatusContent"/> updates.
    /// </summary>
    public void AttachInBandWriter(ChannelWriter<ChatResponseUpdate> writer)
    {
        _inBandWriter = writer;
    }

    /// <summary>
    /// Publishes the <see cref="ChatActivityEventKind.RequestStarted"/> event.
    /// </summary>
    public void PublishRequestStarted()
    {
        Log.RequestStarted(_logger, RequestId);
        NotifyObserver(new ChatActivityEvent
        {
            Kind = ChatActivityEventKind.RequestStarted,
            RequestId = RequestId,
            ScopeId = RootScope.Id,
            Timestamp = _timeProvider.GetUtcNow(),
            ActivityTag = _activityTag,
        });
    }

    /// <summary>
    /// Starts a tool-call scope under <paramref name="parent"/> and publishes the
    /// <see cref="ChatActivityEventKind.ToolCallStarted"/> event with formatted status strings.
    /// </summary>
    public ActivityScope BeginToolScope(
        ActivityScope parent,
        ToolKind toolKind,
        string toolName,
        string? sourceName,
        string? toolCallId)
    {
        string? nearestAgentName = toolKind == ToolKind.Agent ? sourceName : parent.NearestAgentName;
        var scope = new ActivityScope(
            CreateScopeId(),
            parent.Id,
            ActivityScopeType.ToolCall,
            toolKind,
            toolName,
            sourceName,
            nearestAgentName,
            parent.Depth + 1);
        RegisterNode(parent, scope);

        ChatActivityEvent probe = new()
        {
            Kind = ChatActivityEventKind.ToolCallStarted,
            RequestId = RequestId,
            ScopeId = scope.Id,
            ParentScopeId = parent.Id,
            ToolKind = toolKind,
            ToolName = toolName,
            ToolCallId = toolCallId,
            SourceName = sourceName,
            Timestamp = _timeProvider.GetUtcNow(),
            ActivityTag = _activityTag,
        };
        (string? header, string? subheader) = ComputeStatusStrings(probe, parent, toolKind, toolName, sourceName);
        ChatActivityEvent started = probe with { Header = header, Subheader = subheader };

        Log.ToolCallStarted(_logger, toolKind, toolName, scope.Id, RequestId);
        NotifyObserver(started);
        WriteInBand(started);
        return scope;
    }

    /// <summary>
    /// Starts a silent model-call scope under <paramref name="parent"/>, used when a nested
    /// instrumented chat client executes inside a tool invocation.
    /// </summary>
    public ActivityScope BeginModelScope(ActivityScope parent)
    {
        var scope = new ActivityScope(
            CreateScopeId(),
            parent.Id,
            ActivityScopeType.ModelCall,
            toolKind: null,
            name: null,
            sourceName: null,
            parent.NearestAgentName,
            parent.Depth + 1);
        RegisterNode(parent, scope);
        return scope;
    }

    /// <summary>
    /// Completes a scope, recording its duration and publishing the terminal tool event when the
    /// scope is a tool call.
    /// </summary>
    public void CompleteScope(ActivityScope scope, Exception? error)
    {
        if (!_nodes.TryGetValue(scope.Id, out ScopeNode? node))
        {
            return;
        }

        TimeSpan duration = _timeProvider.GetElapsedTime(node.StartTimestamp);
        node.Duration = duration;

        if (scope.Type != ActivityScopeType.ToolCall || scope.Name is null)
        {
            return;
        }

        if (error is null)
        {
            Log.ToolCallCompleted(_logger, scope.Name, duration.TotalMilliseconds, scope.Id);
        }
        else
        {
            Log.ToolCallFailed(_logger, error, scope.Name, duration.TotalMilliseconds, scope.Id);
        }

        ChatActivityEvent completed = new()
        {
            Kind = error is null ? ChatActivityEventKind.ToolCallCompleted : ChatActivityEventKind.ToolCallFailed,
            RequestId = RequestId,
            ScopeId = scope.Id,
            ParentScopeId = scope.ParentId,
            ToolKind = scope.ToolKind,
            ToolName = scope.Name,
            SourceName = scope.SourceName,
            Timestamp = _timeProvider.GetUtcNow(),
            Duration = duration,
            ErrorType = error?.GetType().FullName,
            ErrorMessage = Options.IncludeErrorMessages ? error?.Message : null,
            ActivityTag = _activityTag,
        };
        NotifyObserver(completed);
        WriteInBand(completed);
    }

    /// <summary>
    /// Publishes a <see cref="ChatActivityEventKind.StatusReported"/> event for custom status or
    /// progress raised inside a tool, rendered under the scope's contextual header. Publishes
    /// arriving after the request completed (late MCP notifications) are dropped so that
    /// <see cref="IChatActivityObserver.OnRequestCompleted"/> remains terminal.
    /// </summary>
    public void PublishStatus(ActivityScope scope, string message, double? progress, double? progressTotal)
    {
        lock (_completeGate)
        {
            if (_report is not null)
            {
                return;
            }
        }

        ActivityStatusTemplates templates = Options.Templates;
        string? header = scope switch
        {
            { NearestAgentName: { } agentName } => templates.FormatDefaultHeader(ToolKind.Agent, agentName),
            { ToolKind: ToolKind.Mcp } => templates.FormatDefaultHeader(ToolKind.Mcp, scope.SourceName),
            { ToolKind: not null } => templates.PlainToolHeader,
            _ => null,
        };

        ChatActivityEvent probe = new()
        {
            Kind = ChatActivityEventKind.StatusReported,
            RequestId = RequestId,
            ScopeId = scope.Id,
            ParentScopeId = scope.ParentId,
            ToolKind = scope.ToolKind,
            ToolName = scope.Name,
            SourceName = scope.SourceName,
            Timestamp = _timeProvider.GetUtcNow(),
            Subheader = message,
            Progress = progress,
            ProgressTotal = progressTotal,
            ActivityTag = _activityTag,
        };

        string? subheader = message;
        if (templates.FormatHeader is { } formatHeader)
        {
            header = formatHeader(probe);
        }

        if (templates.FormatSubheader is { } formatSubheader)
        {
            subheader = formatSubheader(probe);
        }

        ChatActivityEvent status = probe with { Header = header, Subheader = subheader };
        NotifyObserver(status);
        WriteInBand(status);
    }

    /// <summary>
    /// Records token usage against a scope and the per-model rollups, publishing a
    /// <see cref="ChatActivityEventKind.UsageReported"/> event.
    /// </summary>
    public void RecordUsage(ActivityScope scope, UsageDetails usage, string? modelId, string? providerName)
    {
        if (!_nodes.TryGetValue(scope.Id, out ScopeNode? node))
        {
            node = _rootNode;
        }

        node.OwnUsage.Add(usage);

        lock (_rollupGate)
        {
            RollupEntry? entry = null;
            foreach (RollupEntry existing in _rollups)
            {
                if (string.Equals(existing.ProviderName, providerName, StringComparison.Ordinal) &&
                    string.Equals(existing.ModelId, modelId, StringComparison.Ordinal))
                {
                    entry = existing;
                    break;
                }
            }

            if (entry is null)
            {
                entry = new RollupEntry(providerName, modelId);
                _rollups.Add(entry);
            }

            entry.Usage.Add(usage);
        }

        Log.UsageRecorded(
            _logger,
            scope.Id,
            usage.InputTokenCount,
            usage.OutputTokenCount,
            usage.TotalTokenCount,
            modelId,
            providerName);

        NotifyObserver(new ChatActivityEvent
        {
            Kind = ChatActivityEventKind.UsageReported,
            RequestId = RequestId,
            ScopeId = scope.Id,
            ParentScopeId = scope.ParentId,
            Timestamp = _timeProvider.GetUtcNow(),
            Usage = usage,
            ModelId = modelId,
            ProviderName = providerName,
            ActivityTag = _activityTag,
        });
    }

    /// <summary>
    /// Completes the request exactly once: computes durations, builds the final report, publishes
    /// the terminal request event, and notifies the observer. Subsequent calls return the same report.
    /// </summary>
    public ChatActivityReport Complete(Exception? error)
    {
        ChatActivityReport report;
        TimeSpan duration;
        lock (_completeGate)
        {
            if (_report is { } existing)
            {
                return existing;
            }

            duration = _timeProvider.GetElapsedTime(_startTimestamp);
            _rootNode.Duration = duration;

            (ActivityScopeReport rootReport, UsageDetails totalUsage) = BuildScopeReport(_rootNode);
            report = new ChatActivityReport
            {
                RequestId = RequestId,
                StartedAt = StartedAt,
                Duration = duration,
                TotalUsage = totalUsage,
                Root = rootReport,
                UsageByModel = BuildRollups(),
            };
            _report = report;
        }

        if (error is null)
        {
            Log.RequestCompleted(_logger, RequestId, duration.TotalMilliseconds, report.TotalUsage.TotalTokenCount);
        }
        else
        {
            Log.RequestFailed(_logger, RequestId, duration.TotalMilliseconds, error.GetType().FullName ?? "Exception");
        }

        NotifyObserver(new ChatActivityEvent
        {
            Kind = error is null ? ChatActivityEventKind.RequestCompleted : ChatActivityEventKind.RequestFailed,
            RequestId = RequestId,
            ScopeId = RootScope.Id,
            Timestamp = _timeProvider.GetUtcNow(),
            Duration = duration,
            Usage = report.TotalUsage,
            ErrorType = error?.GetType().FullName,
            ErrorMessage = Options.IncludeErrorMessages ? error?.Message : null,
            ActivityTag = _activityTag,
        });

        if (_observer is { } observer)
        {
            try
            {
                observer.OnRequestCompleted(report);
            }
            catch (Exception ex)
            {
                Log.ObserverThrew(_logger, ex, observer.GetType().FullName ?? observer.GetType().Name);
            }
        }

        return report;
    }

    /// <summary>
    /// Creates the synthetic streaming update that carries an in-band activity status content item.
    /// </summary>
    public static ChatResponseUpdate CreateStatusUpdate(ChatActivityEvent activityEvent)
    {
        var content = new ActivityStatusContent
        {
            Kind = activityEvent.Kind,
            ToolKind = activityEvent.ToolKind,
            Header = activityEvent.Header,
            Subheader = activityEvent.Subheader,
            ToolCallId = activityEvent.ToolCallId,
            ScopeId = activityEvent.ScopeId,
            ParentScopeId = activityEvent.ParentScopeId,
            Timestamp = activityEvent.Timestamp,
            Usage = activityEvent.Usage,
            Progress = activityEvent.Progress,
            ProgressTotal = activityEvent.ProgressTotal,
        };

        return new ChatResponseUpdate(ChatRole.Assistant, [content]);
    }

    private void RegisterNode(ActivityScope parent, ActivityScope scope)
    {
        var node = new ScopeNode(scope, _timeProvider.GetTimestamp());
        _nodes[scope.Id] = node;
        if (_nodes.TryGetValue(parent.Id, out ScopeNode? parentNode))
        {
            parentNode.AddChild(node);
        }
        else
        {
            _rootNode.AddChild(node);
        }
    }

    private (string? Header, string? Subheader) ComputeStatusStrings(
        ChatActivityEvent probe,
        ActivityScope parent,
        ToolKind toolKind,
        string toolName,
        string? sourceName)
    {
        ActivityStatusTemplates templates = Options.Templates;

        string? header;
        string? subheader;
        if (parent.NearestAgentName is { } enclosingAgent)
        {
            header = templates.FormatDefaultHeader(ToolKind.Agent, enclosingAgent);
            subheader = templates.FormatDefaultSubheader(ToolKind.Agent, toolName);
        }
        else
        {
            header = templates.FormatDefaultHeader(toolKind, sourceName);
            subheader = toolKind == ToolKind.Agent ? null : templates.FormatDefaultSubheader(toolKind, toolName);
        }

        if (templates.FormatHeader is { } formatHeader)
        {
            header = formatHeader(probe);
        }

        if (templates.FormatSubheader is { } formatSubheader)
        {
            subheader = formatSubheader(probe);
        }

        return (header, subheader);
    }

    private IReadOnlyList<ModelUsageBreakdown> BuildRollups()
    {
        lock (_rollupGate)
        {
            var breakdowns = new List<ModelUsageBreakdown>(_rollups.Count);
            foreach (RollupEntry entry in _rollups)
            {
                UsageDetails usage = entry.Usage.Snapshot();
                breakdowns.Add(new ModelUsageBreakdown
                {
                    ProviderName = entry.ProviderName,
                    ModelId = entry.ModelId,
                    InputTokenCount = usage.InputTokenCount,
                    OutputTokenCount = usage.OutputTokenCount,
                    TotalTokenCount = usage.TotalTokenCount,
                });
            }

            return breakdowns;
        }
    }

    private static (ActivityScopeReport Report, UsageDetails TotalUsage) BuildScopeReport(ScopeNode node)
    {
        ScopeNode[] children = node.SnapshotChildren();
        var childReports = new List<ActivityScopeReport>(children.Length);
        UsageDetails total = node.OwnUsage.Snapshot();
        UsageDetails own = node.OwnUsage.Snapshot();

        foreach (ScopeNode child in children)
        {
            (ActivityScopeReport childReport, UsageDetails childTotal) = BuildScopeReport(child);
            childReports.Add(childReport);
            total.Add(childTotal);
        }

        var report = new ActivityScopeReport
        {
            ScopeId = node.Scope.Id,
            ParentScopeId = node.Scope.ParentId,
            ToolKind = node.Scope.ToolKind,
            ToolName = node.Scope.Name,
            SourceName = node.Scope.SourceName,
            Duration = node.Duration,
            OwnUsage = own,
            TotalUsage = total,
            Children = childReports,
        };

        return (report, total);
    }

    private void NotifyObserver(ChatActivityEvent activityEvent)
    {
        if (_observer is null)
        {
            return;
        }

        try
        {
            _observer.OnActivityEvent(activityEvent);
        }
        catch (Exception ex)
        {
            Log.ObserverThrew(_logger, ex, _observer.GetType().FullName ?? _observer.GetType().Name);
        }
    }

    private void WriteInBand(ChatActivityEvent activityEvent)
    {
        if (_inBandWriter is { } writer)
        {
            writer.TryWrite(CreateStatusUpdate(activityEvent));
        }
    }

    private static string CreateScopeId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private sealed class RollupEntry
    {
        public RollupEntry(string? providerName, string? modelId)
        {
            ProviderName = providerName;
            ModelId = modelId;
        }

        public string? ProviderName { get; }

        public string? ModelId { get; }

        public UsageAccumulator Usage { get; } = new();
    }
}
