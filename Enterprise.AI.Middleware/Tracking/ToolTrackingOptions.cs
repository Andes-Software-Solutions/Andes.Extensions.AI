using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Configures the behavior of the tool-tracking middleware added by
/// <see cref="ToolTrackingChatClientBuilderExtensions.UseToolTracking(ChatClientBuilder)"/>.
/// </summary>
public sealed class ToolTrackingOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether activity status updates are injected in-band into
    /// streaming responses as <see cref="ActivityStatusContent"/> items. The default is
    /// <see langword="true"/>.
    /// </summary>
    public bool EnableInBandStatusUpdates { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a final synthetic update carrying the request's
    /// total token usage is appended to streaming responses. The default is <see langword="true"/>.
    /// </summary>
    public bool EnableFinalUsageUpdate { get; set; } = true;

    /// <summary>
    /// Gets the display-string templates used to render status headers and subheaders.
    /// </summary>
    public ActivityStatusTemplates Templates { get; } = new();

    /// <summary>
    /// Gets or sets how tool-call arguments are written to structured logs.
    /// The default is <see cref="ToolArgumentLogging.None"/>.
    /// </summary>
    public ToolArgumentLogging ArgumentLogging { get; set; } = ToolArgumentLogging.None;

    /// <summary>
    /// Gets or sets a value indicating whether failure events carry the exception message in
    /// <see cref="ChatActivityEvent.ErrorMessage"/>. The default is <see langword="false"/>:
    /// exception messages frequently echo argument values or user input, so only
    /// <see cref="ChatActivityEvent.ErrorType"/> is populated unless the host opts in.
    /// </summary>
    public bool IncludeErrorMessages { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether progress notifications from MCP servers are
    /// bridged into <see cref="ChatActivityEventKind.StatusReported"/> events automatically.
    /// The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// The bridge activates for tools whose underlying function is an
    /// <c>McpClientTool</c>, either directly or annotated via
    /// <see cref="McpToolTrackingExtensions"/>. MCP tools wrapped in custom
    /// <see cref="Microsoft.Extensions.AI.DelegatingAIFunction"/> layers are invoked unchanged,
    /// so their custom behavior is never bypassed — at the cost of no progress bridging.
    /// </remarks>
    public bool EnableMcpProgress { get; set; } = true;

    /// <summary>
    /// Gets or sets the clock used for timestamps and durations. The default is
    /// <see cref="TimeProvider.System"/>. Override in tests for deterministic timing.
    /// </summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// Gets or sets the display name used for MCP tools whose server name could not be resolved.
    /// The default is <c>"MCP"</c>.
    /// </summary>
    public string DefaultMcpServerName { get; set; } = "MCP";

    /// <summary>
    /// Gets or sets a delegate that resolves the MCP server display name for an unannotated MCP
    /// tool. Return <see langword="null"/> to fall back to <see cref="DefaultMcpServerName"/>.
    /// </summary>
    public Func<AIFunction, string?>? McpServerNameResolver { get; set; }

    /// <summary>
    /// Gets or sets a delegate that identifies unannotated agent tools and resolves the agent
    /// display name. Return a non-<see langword="null"/> name to classify the function as an
    /// agent tool; return <see langword="null"/> to leave the classification unchanged.
    /// </summary>
    public Func<AIFunction, string?>? AgentNameResolver { get; set; }

    /// <summary>
    /// Creates a shallow snapshot of these options so later mutations do not affect an already
    /// constructed middleware instance.
    /// </summary>
    /// <returns>A copy of the current options.</returns>
    internal ToolTrackingOptions Clone()
    {
        var clone = new ToolTrackingOptions
        {
            EnableInBandStatusUpdates = EnableInBandStatusUpdates,
            EnableFinalUsageUpdate = EnableFinalUsageUpdate,
            ArgumentLogging = ArgumentLogging,
            IncludeErrorMessages = IncludeErrorMessages,
            EnableMcpProgress = EnableMcpProgress,
            TimeProvider = TimeProvider,
            DefaultMcpServerName = DefaultMcpServerName,
            McpServerNameResolver = McpServerNameResolver,
            AgentNameResolver = AgentNameResolver,
        };

        clone.Templates.PlainToolHeader = Templates.PlainToolHeader;
        clone.Templates.PlainToolSubheader = Templates.PlainToolSubheader;
        clone.Templates.AgentHeader = Templates.AgentHeader;
        clone.Templates.AgentSubheader = Templates.AgentSubheader;
        clone.Templates.McpHeader = Templates.McpHeader;
        clone.Templates.McpSubheader = Templates.McpSubheader;
        clone.Templates.FormatHeader = Templates.FormatHeader;
        clone.Templates.FormatSubheader = Templates.FormatSubheader;

        return clone;
    }
}
