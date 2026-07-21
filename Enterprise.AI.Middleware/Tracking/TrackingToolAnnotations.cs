namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Defines the well-known keys the tool-tracking middleware reads from and writes to
/// <c>AdditionalProperties</c> dictionaries.
/// </summary>
/// <remarks>
/// The helper extensions in <see cref="McpToolTrackingExtensions"/> and
/// <see cref="AgentToolTrackingExtensions"/> write these annotations for you. They are public
/// so hosts can annotate arbitrary <see cref="Microsoft.Extensions.AI.AIFunction"/> instances
/// (or read the attached report) without taking a dependency on those helpers.
/// </remarks>
public static class TrackingToolAnnotations
{
    /// <summary>
    /// Key on <see cref="Microsoft.Extensions.AI.AITool.AdditionalProperties"/> holding the
    /// <see cref="ToolKind"/> of the tool. The value may be a <see cref="ToolKind"/> or its string name.
    /// </summary>
    public const string ToolKindKey = "enterprise.ai.tracking.toolKind";

    /// <summary>
    /// Key on <see cref="Microsoft.Extensions.AI.AITool.AdditionalProperties"/> holding the display
    /// name of the tool's source — the agent name for agent tools or the MCP server name for MCP tools.
    /// </summary>
    public const string SourceNameKey = "enterprise.ai.tracking.sourceName";

    /// <summary>
    /// Key on <see cref="Microsoft.Extensions.AI.ChatResponse.AdditionalProperties"/> holding the
    /// <see cref="ChatActivityReport"/> for a completed non-streaming request.
    /// </summary>
    public const string ActivityReportKey = "enterprise.ai.tracking.activityReport";

    /// <summary>
    /// Key on <see cref="Microsoft.Extensions.AI.ChatOptions.AdditionalProperties"/> holding the
    /// caller-supplied correlation tag copied onto every <see cref="ChatActivityEvent"/>.
    /// </summary>
    public const string ActivityTagKey = "enterprise.ai.tracking.activityTag";
}
