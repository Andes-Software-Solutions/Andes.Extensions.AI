using Enterprise.AI.Middleware.Tracking.Internal;

namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Identifies one node of the activity tree for an in-flight chat request: the root request,
/// a tool invocation, or a nested model call made by an agent tool.
/// </summary>
/// <remarks>
/// The scope for the currently executing activity is available ambiently via
/// <see cref="ChatActivityScope.Current"/>; it flows across asynchronous calls.
/// </remarks>
public sealed class ActivityScope
{
    internal ActivityScope(
        string id,
        string? parentId,
        ActivityScopeType type,
        ToolKind? toolKind,
        string? name,
        string? sourceName,
        string? nearestAgentName,
        int depth)
    {
        Id = id;
        ParentId = parentId;
        Type = type;
        ToolKind = toolKind;
        Name = name;
        SourceName = sourceName;
        NearestAgentName = nearestAgentName;
        Depth = depth;
    }

    /// <summary>
    /// Gets the unique identifier of this scope within its request.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the identifier of the parent scope, or <see langword="null"/> for the root scope.
    /// </summary>
    public string? ParentId { get; }

    /// <summary>
    /// Gets the kind of tool this scope represents, or <see langword="null"/> when the scope is
    /// not a tool invocation.
    /// </summary>
    public ToolKind? ToolKind { get; }

    /// <summary>
    /// Gets the tool name for tool-invocation scopes, or <see langword="null"/> otherwise.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Gets the nesting depth of this scope; the root scope has depth <c>0</c>.
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// Gets the structural role of this scope within the activity tree.
    /// </summary>
    internal ActivityScopeType Type { get; }

    /// <summary>
    /// Gets the agent or MCP server display name for tool scopes, when known.
    /// </summary>
    internal string? SourceName { get; }

    /// <summary>
    /// Gets the display name of the closest enclosing agent (including this scope itself when it
    /// is an agent tool), used to render nested activity under the agent's header.
    /// </summary>
    internal string? NearestAgentName { get; }
}
