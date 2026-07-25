using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Describes a tool participating in a tracked chat request, including how it should be
/// classified and displayed in progress updates.
/// </summary>
/// <remarks>
/// Instances are produced by the default classifier in <see cref="ToolTrackingChatClient"/> or by a custom
/// <see cref="ToolTrackingOptions.ToolClassifier"/>. The classifier hook is the extension point through which
/// additional tool categories (MCP tools via <c>Andes.Extensions.AI.Mcp</c>, agents as tools via
/// <c>Andes.Extensions.AI.Agent</c>) receive first-class display treatment.
/// </remarks>
public sealed class ToolDescriptor
{
    private readonly string? _displayName;

    /// <summary>
    /// Gets the tool name as registered with the chat client (matches <see cref="AITool.Name"/>).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the human-friendly name used in progress messages. Defaults to <see cref="Name"/>.
    /// </summary>
    public string DisplayName
    {
        get => _displayName ?? Name;
        init => _displayName = value;
    }

    /// <summary>
    /// Gets the category of the tool.
    /// </summary>
    public ToolKind Kind { get; init; }

    /// <summary>
    /// Gets the origin of the tool, such as an MCP server name or agent identifier, if any.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Classifies a tool the way the built-in classifier does: <see cref="AIFunction"/> tools as
    /// <see cref="ToolKind.Function"/> and everything else as <see cref="ToolKind.Unknown"/>.
    /// </summary>
    /// <remarks>
    /// Custom <see cref="ToolTrackingOptions.ToolClassifier"/> implementations can use this as
    /// their fallback for tools they do not recognize, keeping their behavior aligned with the
    /// default pipeline.
    /// </remarks>
    /// <param name="tool">The tool to classify.</param>
    /// <returns>The default descriptor for <paramref name="tool"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> is <see langword="null"/>.</exception>
    public static ToolDescriptor CreateDefault(AITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return new ToolDescriptor
        {
            Name = tool.Name,
            Kind = tool is AIFunction ? ToolKind.Function : ToolKind.Unknown,
        };
    }
}
