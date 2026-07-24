namespace Andes.Extensions.AI;

/// <summary>
/// Represents a single progress event raised while a tracked chat request executes.
/// </summary>
/// <remarks>
/// Progress events never carry prompt content, tool results, or tool arguments unless
/// <see cref="ToolTrackingOptions.IncludeToolArguments"/> is explicitly enabled, in which case
/// only <see cref="Arguments"/> is populated.
/// </remarks>
public sealed class ChatProgressUpdate
{
    /// <summary>
    /// Gets the kind of progress event.
    /// </summary>
    public required ChatProgressKind Kind { get; init; }

    /// <summary>
    /// Gets the display text for the event (for example, "Calling GetWeather Tool" or a tool sub-status).
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the identifier of the tool-call scope this event belongs to. Events of kind
    /// <see cref="ChatProgressKind.ToolProgress"/> share the scope identifier of their owning tool call,
    /// which lets consumers group sub-statuses under the tool's header.
    /// </summary>
    public required string ScopeId { get; init; }

    /// <summary>
    /// Gets the identifier of the parent scope, or <see langword="null"/> when the event belongs to the request root.
    /// </summary>
    public string? ParentScopeId { get; init; }

    /// <summary>
    /// Gets the display depth of the event: 0 for request-level events, 1 for tool headers,
    /// 2 for sub-statuses beneath a tool header, and deeper for nested tool calls.
    /// </summary>
    public int Depth { get; init; }

    /// <summary>
    /// Gets the name of the tool associated with the event, if any.
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Gets the category of the tool associated with the event.
    /// </summary>
    public ToolKind ToolKind { get; init; }

    /// <summary>
    /// Gets the origin of the tool associated with the event, such as an MCP server or agent name, if any.
    /// </summary>
    public string? ToolSource { get; init; }

    /// <summary>
    /// Gets the function call identifier correlating the event with the model's
    /// <see cref="Microsoft.Extensions.AI.FunctionCallContent"/>, if available.
    /// </summary>
    public string? CallId { get; init; }

    /// <summary>
    /// Gets the time at which the event was raised.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Gets the elapsed duration for completion events
    /// (<see cref="ChatProgressKind.ToolCompleted"/>, <see cref="ChatProgressKind.ToolFailed"/>,
    /// and <see cref="ChatProgressKind.RequestCompleted"/>).
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Gets the stringified tool arguments. Always <see langword="null"/> unless
    /// <see cref="ToolTrackingOptions.IncludeToolArguments"/> is enabled.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Arguments { get; init; }
}
