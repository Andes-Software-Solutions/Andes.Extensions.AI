using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Describes a tool participating in a tracked chat request, including how it should be
/// classified and displayed in progress updates.
/// </summary>
/// <remarks>
/// Instances are produced by the default classifier in <see cref="ToolTrackingChatClient"/> or by a custom
/// <see cref="ToolTrackingOptions.ToolClassifier"/>. The classifier hook is the extension point through which
/// future tool categories (MCP tools, agents as tools) receive first-class display treatment.
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
}
