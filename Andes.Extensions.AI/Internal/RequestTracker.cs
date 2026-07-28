using System.Globalization;
using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Owns the per-request tracking state: the scope tree, assistant usage accumulation, and the
/// emission of progress events to the streaming channel and out-of-band observers.
/// </summary>
internal sealed class RequestTracker
{
    private readonly ChatClientMetadata? _metadata;
    private readonly ChannelWriter<ChatResponseUpdate>? _writer;
    private readonly IChatProgressObserver[] _observers;
    private readonly Lock _lock = new();
    private readonly List<TurnAccumulator> _turns = [];
    private readonly UsageDetails _assistantUsage = new();
    private readonly HashSet<string> _wrappedToolNames = [];
    private readonly long _startTimestamp;
    private int _scopeCounter;
    private int _iteration;
    private string? _lastResponseId;
    private string? _lastModelId;

    public RequestTracker(ToolTrackingOptions options, ChatClientMetadata? metadata, ChannelWriter<ChatResponseUpdate>? writer)
    {
        Options = options;
        _metadata = metadata;
        _writer = writer;
        _observers = [.. options.Observers];
        _startTimestamp = options.TimeProvider.GetTimestamp();
        RootScope = new ToolScope(this, NextScopeId(), parent: null, descriptor: null, callId: null, depth: 0);
    }

    public ToolScope RootScope { get; }

    public ToolTrackingOptions Options { get; }

    public void RegisterWrappedTool(string name)
    {
        lock (_lock)
        {
            _wrappedToolNames.Add(name);
        }
    }

    public void EmitRequestStarted()
    {
        Emit(new ChatProgressUpdate
        {
            Kind = ChatProgressKind.RequestStarted,
            Message = "Starting request",
            ScopeId = RootScope.ScopeId,
            Depth = 0,
            Timestamp = Now(),
        });
    }

    public void EmitThinking()
    {
        Emit(new ChatProgressUpdate
        {
            Kind = ChatProgressKind.Thinking,
            Message = "Thinking...",
            ScopeId = RootScope.ScopeId,
            Depth = 0,
            Timestamp = Now(),
        });
    }

    public ToolScope BeginToolScope(
        ToolDescriptor descriptor,
        string? callId,
        ToolScope? parent,
        IReadOnlyDictionary<string, string>? arguments)
    {
        ToolScope effectiveParent = parent ?? RootScope;
        var scope = new ToolScope(this, NextScopeId(), effectiveParent, descriptor, callId, effectiveParent.Depth + 1);
        effectiveParent.AddChild(scope);
        Emit(new ChatProgressUpdate
        {
            Kind = ChatProgressKind.ToolInvoking,
            Message = FormatHeader(descriptor),
            ToolName = descriptor.Name,
            ToolKind = descriptor.Kind,
            ToolSource = descriptor.Source,
            CallId = callId,
            ScopeId = scope.ScopeId,
            ParentScopeId = effectiveParent.ScopeId,
            Depth = scope.Depth,
            Timestamp = Now(),
            Arguments = arguments,
        });
        return scope;
    }

    public void CompleteToolScope(ToolScope scope, TimeSpan duration, bool succeeded)
    {
        scope.Duration = duration;
        scope.Succeeded = succeeded;
        ToolDescriptor descriptor = scope.Descriptor!;
        Emit(new ChatProgressUpdate
        {
            Kind = succeeded ? ChatProgressKind.ToolCompleted : ChatProgressKind.ToolFailed,
            Message = succeeded ? $"{descriptor.DisplayName} completed" : $"{descriptor.DisplayName} failed",
            ToolName = descriptor.Name,
            ToolKind = descriptor.Kind,
            ToolSource = descriptor.Source,
            CallId = scope.CallId,
            ScopeId = scope.ScopeId,
            ParentScopeId = scope.Parent?.ScopeId,
            Depth = scope.Depth,
            Timestamp = Now(),
            Duration = duration,
        });
    }

    public void ReportToolProgress(ToolScope scope, string status, double? progress = null, double? progressTotal = null)
    {
        ToolDescriptor? descriptor = scope.Descriptor;
        Emit(new ChatProgressUpdate
        {
            Kind = ChatProgressKind.ToolProgress,
            Message = status,
            ToolName = descriptor?.Name,
            ToolKind = descriptor?.Kind ?? ToolKind.Unknown,
            ToolSource = descriptor?.Source,
            CallId = scope.CallId,
            ScopeId = scope.ScopeId,
            ParentScopeId = scope.Parent?.ScopeId,
            Depth = scope.Depth + 1,
            Timestamp = Now(),
            Progress = progress,
            ProgressTotal = progressTotal,
        });
    }

    public static void ReportToolUsage(ToolScope scope, UsageDetails usage)
    {
        scope.AddUsage(usage);
    }

    /// <summary>
    /// Records assistant usage observed on a streamed update, attributing it to the current model turn.
    /// </summary>
    public void RecordAssistantTurnUsage(UsageDetails usage, string? responseId, string? modelId)
    {
        lock (_lock)
        {
            UsageMath.AddTo(_assistantUsage, usage);

            TurnAccumulator? current = _turns.Count > 0 ? _turns[^1] : null;
            if (current is null || current.Iteration != _iteration)
            {
                current = new TurnAccumulator
                {
                    Iteration = _iteration,
                    ResponseId = responseId,
                    ModelId = modelId,
                };
                _turns.Add(current);
            }

            UsageMath.AddTo(current.Usage, usage);
            _lastResponseId = responseId ?? _lastResponseId;
            _lastModelId = modelId ?? _lastModelId;
        }
    }

    /// <summary>
    /// Records the aggregate assistant usage of a non-streaming response. No turn entry is created
    /// because per-turn breakdowns are unavailable outside of streaming.
    /// </summary>
    public void RecordAssistantUsage(UsageDetails usage, string? responseId, string? modelId)
    {
        lock (_lock)
        {
            UsageMath.AddTo(_assistantUsage, usage);
            _lastResponseId = responseId ?? _lastResponseId;
            _lastModelId = modelId ?? _lastModelId;
        }
    }

    /// <summary>
    /// Handles a function call observed in the stream. Tools the tracker could not wrap (declarations,
    /// hosted tools) get a best-effort header event with no completion or duration.
    /// </summary>
    public void OnFunctionCall(string callId, string name)
    {
        bool wrapped;
        lock (_lock)
        {
            wrapped = _wrappedToolNames.Contains(name);
        }

        if (!wrapped)
        {
            var descriptor = new ToolDescriptor { Name = name };
            Emit(new ChatProgressUpdate
            {
                Kind = ChatProgressKind.ToolInvoking,
                Message = FormatHeader(descriptor),
                ToolName = name,
                ToolKind = ToolKind.Unknown,
                CallId = callId,
                ScopeId = NextScopeId(),
                ParentScopeId = RootScope.ScopeId,
                Depth = RootScope.Depth + 1,
                Timestamp = Now(),
            });
        }
    }

    /// <summary>
    /// Advances the model-turn counter after a tool round-trip and announces the next turn.
    /// </summary>
    public void AdvanceIteration()
    {
        lock (_lock)
        {
            _iteration++;
        }

        EmitThinking();
    }

    public ChatProgressUpdate CreateRequestCompletedUpdate(TimeSpan duration)
    {
        return new ChatProgressUpdate
        {
            Kind = ChatProgressKind.RequestCompleted,
            Message = "Request completed",
            ScopeId = RootScope.ScopeId,
            Depth = 0,
            Timestamp = Now(),
            Duration = duration,
        };
    }

    public ChatProgressUpdate CreateRequestFailedUpdate(TimeSpan duration)
    {
        return new ChatProgressUpdate
        {
            Kind = ChatProgressKind.RequestFailed,
            Message = "Request failed",
            ScopeId = RootScope.ScopeId,
            Depth = 0,
            Timestamp = Now(),
            Duration = duration,
        };
    }

    public void EmitRequestCompleted(TimeSpan duration)
    {
        Emit(CreateRequestCompletedUpdate(duration));
    }

    public void NotifyObservers(ChatProgressUpdate update)
    {
        foreach (IChatProgressObserver observer in _observers)
        {
            try
            {
                observer.OnProgress(update);
            }
            catch
            {
                // Observers are documented as non-throwing; a faulty observer must not corrupt the response.
            }
        }
    }

    public void NotifyRequestCompleted(ChatUsageReport report)
    {
        foreach (IChatProgressObserver observer in _observers)
        {
            try
            {
                observer.OnRequestCompleted(report);
            }
            catch
            {
                // Observers are documented as non-throwing; a faulty observer must not corrupt the response.
            }
        }
    }

    public ChatUsageReport BuildReport()
    {
        TimeSpan duration = Options.TimeProvider.GetElapsedTime(_startTimestamp);

        UsageDetails assistantUsage;
        List<AssistantTurnUsage> turns;
        string? responseId;
        string? modelId;
        lock (_lock)
        {
            assistantUsage = UsageMath.Clone(_assistantUsage);
            turns = _turns
                .Select(turn => new AssistantTurnUsage
                {
                    Iteration = turn.Iteration,
                    ResponseId = turn.ResponseId,
                    ModelId = turn.ModelId,
                    Usage = UsageMath.Clone(turn.Usage),
                })
                .ToList();
            responseId = _lastResponseId;
            modelId = _lastModelId;
        }

        List<ToolCallUsage> toolCalls = RootScope.SnapshotChildren().Select(BuildToolCallUsage).ToList();

        UsageDetails total = UsageMath.Clone(assistantUsage);
        foreach (ToolCallUsage call in toolCalls)
        {
            if (call.Usage is { } callUsage)
            {
                UsageMath.AddTo(total, callUsage);
            }
        }

        return new ChatUsageReport
        {
            ResponseId = responseId,
            ModelId = modelId ?? _metadata?.DefaultModelId,
            ProviderName = _metadata?.ProviderName,
            AssistantUsage = assistantUsage,
            Turns = turns,
            ToolCalls = toolCalls,
            TotalUsage = total,
            Duration = duration,
        };
    }

    internal static string DefaultHeader(ToolDescriptor descriptor)
    {
        // The kind label ("MCP", "Agent", "Tool") is appended only when the display name (or the
        // MCP server source) does not already end with it, so a server named "Andes Test MCP" or
        // an agent named "Research Agent" reads as "Calling Andes Test MCP" / "Calling Research
        // Agent" instead of doubling the word.
        return descriptor.Kind switch
        {
            ToolKind.McpTool => Compose(descriptor.Source ?? descriptor.DisplayName, "MCP"),
            ToolKind.Agent => Compose(descriptor.DisplayName, "Agent"),
            ToolKind.Function => Compose(descriptor.DisplayName, "Tool"),
            _ => $"Calling {descriptor.DisplayName}",
        };

        static string Compose(string name, string suffix)
        {
            return name.TrimEnd().EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? $"Calling {name}"
                : $"Calling {name} {suffix}";
        }
    }

    private static ToolCallUsage BuildToolCallUsage(ToolScope scope)
    {
        List<ToolCallUsage> children = scope.SnapshotChildren().Select(BuildToolCallUsage).ToList();

        UsageDetails? rollup = scope.SnapshotUsage();
        foreach (ToolCallUsage child in children)
        {
            if (child.Usage is { } childUsage)
            {
                rollup ??= new UsageDetails();
                UsageMath.AddTo(rollup, childUsage);
            }
        }

        ToolDescriptor descriptor = scope.Descriptor!;
        return new ToolCallUsage
        {
            CallId = scope.CallId,
            ToolName = descriptor.Name,
            Kind = descriptor.Kind,
            Source = descriptor.Source,
            Usage = rollup,
            Children = children,
            Duration = scope.Duration,
            Succeeded = scope.Succeeded,
        };
    }

    private void Emit(ChatProgressUpdate update)
    {
        if (_writer is not null && Options.EmitProgressContent)
        {
            _writer.TryWrite(new ChatResponseUpdate(role: null, contents: [new ChatProgressContent(update)]));
        }

        NotifyObservers(update);
    }

    private string FormatHeader(ToolDescriptor descriptor)
    {
        return Options.HeaderFormatter?.Invoke(descriptor) ?? DefaultHeader(descriptor);
    }

    private DateTimeOffset Now()
    {
        return Options.TimeProvider.GetUtcNow();
    }

    private string NextScopeId()
    {
        int id = Interlocked.Increment(ref _scopeCounter);
        return string.Create(CultureInfo.InvariantCulture, $"scope-{id}");
    }

    private sealed class TurnAccumulator
    {
        public required int Iteration { get; init; }

        public string? ResponseId { get; init; }

        public string? ModelId { get; init; }

        public UsageDetails Usage { get; } = new();
    }
}
