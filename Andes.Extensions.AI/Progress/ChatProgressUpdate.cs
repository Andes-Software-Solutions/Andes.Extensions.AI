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
    /// The well-known scope identifier stamped on updates created outside a tracked request via
    /// <see cref="CreateRequestStarted(string?)"/> and <see cref="CreateReasoning(string?)"/>.
    /// The middleware's own per-request identifiers ("scope-1", "scope-2", …) never collide with it.
    /// </summary>
    public const string ExternalScopeId = "scope-external";

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

    /// <summary>
    /// Gets the numeric progress reported for the current tool operation, when known.
    /// Only populated on <see cref="ChatProgressKind.ToolProgress"/> events whose reporter
    /// supplied a value.
    /// </summary>
    /// <remarks>
    /// Sources that report progress as single-precision values (for example MCP servers) may
    /// produce float-to-double widening artifacts for fractional values (<c>33.3f</c> becomes
    /// <c>33.29999923706055</c>). Integer step counts are unaffected.
    /// </remarks>
    public double? Progress { get; init; }

    /// <summary>
    /// Gets the total amount of work required — the denominator for <see cref="Progress"/> — when known.
    /// </summary>
    public double? ProgressTotal { get; init; }

    /// <summary>
    /// Creates a request-level <see cref="ChatProgressKind.RequestStarted"/> update for emitting
    /// outside the middleware — for example, prepended to the update stream a UI consumes so a
    /// status line shows before the first tracked event arrives.
    /// </summary>
    /// <param name="message">The status text, or <see langword="null"/> to use "Starting request".</param>
    /// <returns>An update stamped with <see cref="ExternalScopeId"/>, depth 0, and the current UTC time.</returns>
    /// <remarks>
    /// The middleware never emits this kind itself. Construct the update with an object initializer
    /// instead when a custom <see cref="ScopeId"/> or <see cref="Timestamp"/> is needed.
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// ChatResponseUpdate started = ChatProgressUpdate.CreateRequestStarted().ToResponseUpdate();
    /// </code>
    /// </example>
    public static ChatProgressUpdate CreateRequestStarted(string? message = null)
    {
        return new ChatProgressUpdate
        {
            Kind = ChatProgressKind.RequestStarted,
            Message = message ?? "Starting request",
            ScopeId = ExternalScopeId,
            Depth = 0,
            Timestamp = TimeProvider.System.GetUtcNow(),
        };
    }

    /// <summary>
    /// Creates a request-level <see cref="ChatProgressKind.Reasoning"/> update for emitting outside
    /// the middleware, mirroring the event the middleware raises when it detects reasoning content.
    /// </summary>
    /// <param name="message">The status text, or <see langword="null"/> to use "Reasoning...".</param>
    /// <returns>An update stamped with <see cref="ExternalScopeId"/>, depth 0, and the current UTC time.</returns>
    /// <remarks>
    /// Construct the update with an object initializer instead when a custom
    /// <see cref="ScopeId"/> or <see cref="Timestamp"/> is needed.
    /// </remarks>
    public static ChatProgressUpdate CreateReasoning(string? message = null)
    {
        return new ChatProgressUpdate
        {
            Kind = ChatProgressKind.Reasoning,
            Message = message ?? "Reasoning...",
            ScopeId = ExternalScopeId,
            Depth = 0,
            Timestamp = TimeProvider.System.GetUtcNow(),
        };
    }
}
