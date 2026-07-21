namespace Enterprise.AI.Middleware.Tracking.Internal;

/// <summary>
/// The result of classifying an <see cref="Microsoft.Extensions.AI.AIFunction"/> for tracking.
/// </summary>
/// <param name="Kind">The kind of tool the function represents.</param>
/// <param name="SourceName">
/// The display name of the tool's source — the agent name for agent tools or the MCP server name
/// for MCP tools — or <see langword="null"/> for plain function tools.
/// </param>
internal readonly record struct ToolClassification(ToolKind Kind, string? SourceName);
