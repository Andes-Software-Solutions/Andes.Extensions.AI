using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Describes a single activity that occurred while the assistant processed a chat request,
/// such as a tool invocation starting or token usage being reported.
/// </summary>
/// <remarks>
/// Instances are published to <see cref="IChatActivityObserver.OnActivityEvent"/> in the order
/// the activities occurred. Events never carry prompt content, tool arguments, or tool results.
/// <see cref="ErrorMessage"/> is populated only when
/// <see cref="ToolTrackingOptions.IncludeErrorMessages"/> is enabled, because exception messages
/// can echo argument values; sanitize events before forwarding them to untrusted clients.
/// </remarks>
public sealed record ChatActivityEvent
{
    /// <summary>
    /// Gets the lifecycle stage this event represents.
    /// </summary>
    public required ChatActivityEventKind Kind { get; init; }

    /// <summary>
    /// Gets the identifier of the top-level chat request this event belongs to.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// Gets the identifier of the activity scope that produced this event.
    /// </summary>
    public required string ScopeId { get; init; }

    /// <summary>
    /// Gets the identifier of the parent scope, or <see langword="null"/> for the root scope.
    /// </summary>
    public string? ParentScopeId { get; init; }

    /// <summary>
    /// Gets the kind of tool involved, or <see langword="null"/> when the event is not tool-related.
    /// </summary>
    public ToolKind? ToolKind { get; init; }

    /// <summary>
    /// Gets the name of the tool involved, or <see langword="null"/> when the event is not tool-related.
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Gets the provider-assigned call id of the tool invocation (matching
    /// <see cref="Microsoft.Extensions.AI.ToolCallContent.CallId"/>), when available.
    /// </summary>
    public string? ToolCallId { get; init; }

    /// <summary>
    /// Gets the display name of the tool's source — the agent name for agent tools or the
    /// MCP server name for MCP tools — or <see langword="null"/> for plain function tools.
    /// </summary>
    public string? SourceName { get; init; }

    /// <summary>
    /// Gets the human-readable status header for this event, formatted from
    /// <see cref="ActivityStatusTemplates"/>, or <see langword="null"/> when suppressed.
    /// </summary>
    public string? Header { get; init; }

    /// <summary>
    /// Gets the human-readable status subheader for this event, or <see langword="null"/> when suppressed.
    /// </summary>
    public string? Subheader { get; init; }

    /// <summary>
    /// Gets the time at which the activity occurred.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Gets the elapsed duration of the activity, populated on completion and failure events.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Gets the token usage associated with this event, populated on
    /// <see cref="ChatActivityEventKind.UsageReported"/> and terminal request events.
    /// </summary>
    public UsageDetails? Usage { get; init; }

    /// <summary>
    /// Gets the numeric progress reported so far on
    /// <see cref="ChatActivityEventKind.StatusReported"/> events — typically a percentage or a
    /// count of completed items — or <see langword="null"/> when no numeric progress was supplied.
    /// </summary>
    public double? Progress { get; init; }

    /// <summary>
    /// Gets the total progress required (the denominator for <see cref="Progress"/>), when known.
    /// </summary>
    public double? ProgressTotal { get; init; }

    /// <summary>
    /// Gets the identifier of the model that produced the usage, when known.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Gets the name of the provider that served the request (for example
    /// <c>azure.ai.openai</c> or <c>aws.bedrock</c>), when known.
    /// </summary>
    public string? ProviderName { get; init; }

    /// <summary>
    /// Gets the full type name of the exception on failure events, or <see langword="null"/> otherwise.
    /// </summary>
    public string? ErrorType { get; init; }

    /// <summary>
    /// Gets the exception message on failure events when
    /// <see cref="ToolTrackingOptions.IncludeErrorMessages"/> is enabled, or
    /// <see langword="null"/> otherwise.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the caller-supplied correlation tag set via
    /// <see cref="ChatOptionsTrackingExtensions.WithActivityTag"/>, or <see langword="null"/> when unset.
    /// </summary>
    public string? ActivityTag { get; init; }
}
