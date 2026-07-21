using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Enterprise.AI.Middleware.Tracking.Internal;

/// <summary>
/// Determines whether an <see cref="AIFunction"/> is a plain function tool, an agent exposed as
/// a tool, or an MCP tool, and resolves the display name of its source.
/// </summary>
internal static class ToolClassifier
{
    /// <summary>
    /// Classifies the given function. Explicit annotations written via
    /// <see cref="TrackingToolAnnotations"/> always win; otherwise MCP tools are recognized by
    /// type and agent tools via <see cref="ToolTrackingOptions.AgentNameResolver"/>.
    /// </summary>
    /// <param name="function">The function to classify.</param>
    /// <param name="options">The middleware options supplying resolvers and defaults.</param>
    /// <returns>The classification result.</returns>
    public static ToolClassification Classify(AIFunction function, ToolTrackingOptions options)
    {
        IReadOnlyDictionary<string, object?> properties = function.AdditionalProperties;
        ToolKind? annotatedKind = ReadAnnotatedKind(properties);
        string? sourceName = ReadSourceName(properties);

        if (annotatedKind is { } kind)
        {
            sourceName ??= kind switch
            {
                ToolKind.Mcp => options.McpServerNameResolver?.Invoke(function) ?? options.DefaultMcpServerName,
                ToolKind.Agent => options.AgentNameResolver?.Invoke(function) ?? function.Name,
                _ => null,
            };

            return new ToolClassification(kind, sourceName);
        }

        if (function.GetService(typeof(McpClientTool)) is McpClientTool)
        {
            string mcpName = sourceName
                ?? options.McpServerNameResolver?.Invoke(function)
                ?? options.DefaultMcpServerName;
            return new ToolClassification(ToolKind.Mcp, mcpName);
        }

        if (options.AgentNameResolver?.Invoke(function) is { } agentName)
        {
            return new ToolClassification(ToolKind.Agent, agentName);
        }

        return new ToolClassification(ToolKind.Function, sourceName);
    }

    private static ToolKind? ReadAnnotatedKind(IReadOnlyDictionary<string, object?> properties)
    {
        if (!properties.TryGetValue(TrackingToolAnnotations.ToolKindKey, out object? value))
        {
            return null;
        }

        return value switch
        {
            ToolKind kind => kind,
            string name when Enum.TryParse(name, ignoreCase: true, out ToolKind parsed) => parsed,
            _ => null,
        };
    }

    private static string? ReadSourceName(IReadOnlyDictionary<string, object?> properties)
    {
        if (properties.TryGetValue(TrackingToolAnnotations.SourceNameKey, out object? value) &&
            value is string name &&
            name.Length > 0)
        {
            return name;
        }

        return null;
    }
}
