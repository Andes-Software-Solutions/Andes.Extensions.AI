using Microsoft.Extensions.Logging;

namespace Enterprise.AI.Middleware.Tracking.Internal;

/// <summary>
/// Source-generated structured log messages for the tool-tracking middleware.
/// </summary>
/// <remarks>
/// Messages intentionally carry only identifiers, names, and counts — never prompt content,
/// tool arguments, or tool results — unless the host explicitly opts in via
/// <see cref="ToolArgumentLogging"/>.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Chat request {RequestId} started.")]
    public static partial void RequestStarted(ILogger logger, string requestId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Chat request {RequestId} completed in {DurationMs} ms; total tokens: {TotalTokens}.")]
    public static partial void RequestCompleted(ILogger logger, string requestId, double durationMs, long? totalTokens);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Chat request {RequestId} failed after {DurationMs} ms with {ErrorType}.")]
    public static partial void RequestFailed(ILogger logger, string requestId, double durationMs, string errorType);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Tool call started: {ToolKind} '{ToolName}' (scope {ScopeId}, request {RequestId}).")]
    public static partial void ToolCallStarted(ILogger logger, ToolKind toolKind, string toolName, string scopeId, string requestId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Tool call completed: '{ToolName}' in {DurationMs} ms (scope {ScopeId}).")]
    public static partial void ToolCallCompleted(ILogger logger, string toolName, double durationMs, string scopeId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "Tool call failed: '{ToolName}' after {DurationMs} ms (scope {ScopeId}).")]
    public static partial void ToolCallFailed(ILogger logger, Exception exception, string toolName, double durationMs, string scopeId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Debug, Message = "Tool call arguments for '{ToolName}': {Arguments}.")]
    public static partial void ToolCallArguments(ILogger logger, string toolName, string arguments);

    [LoggerMessage(EventId = 8, Level = LogLevel.Debug, Message = "Usage recorded for scope {ScopeId}: input {InputTokens}, output {OutputTokens}, total {TotalTokens} (model {ModelId}, provider {ProviderName}).")]
    public static partial void UsageRecorded(ILogger logger, string scopeId, long? inputTokens, long? outputTokens, long? totalTokens, string? modelId, string? providerName);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "Activity observer {ObserverType} threw; the chat request is unaffected.")]
    public static partial void ObserverThrew(ILogger logger, Exception exception, string observerType);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "The MCP progress bridge for scope {ScopeId} threw while publishing; the notification was dropped and the tool call is unaffected.")]
    public static partial void ProgressBridgeThrew(ILogger logger, Exception exception, string scopeId);
}
