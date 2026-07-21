using System.Globalization;

namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Defines the display strings used to render activity status headers and subheaders.
/// </summary>
/// <remarks>
/// <para>
/// Each template is a <see cref="string.Format(IFormatProvider, string, object[])"/> pattern where
/// <c>{0}</c> is the tool name (subheaders) or the source name (agent/MCP headers). For full
/// control — localization, suppression, or entirely custom wording — set
/// <see cref="FormatHeader"/> / <see cref="FormatSubheader"/>, which take precedence over the
/// string templates. Returning <see langword="null"/> from a delegate suppresses that line.
/// </para>
/// </remarks>
public sealed class ActivityStatusTemplates
{
    /// <summary>
    /// Gets or sets the header shown when a plain function tool is invoked.
    /// </summary>
    public string PlainToolHeader { get; set; } = "Calling Tool(s)";

    /// <summary>
    /// Gets or sets the subheader shown per plain function tool invocation;
    /// <c>{0}</c> is the tool name.
    /// </summary>
    public string PlainToolSubheader { get; set; } = "Called {0}";

    /// <summary>
    /// Gets or sets the header shown when an agent tool is invoked; <c>{0}</c> is the agent name.
    /// </summary>
    public string AgentHeader { get; set; } = "Calling {0} Agent";

    /// <summary>
    /// Gets or sets the subheader shown for activity nested inside an agent tool;
    /// <c>{0}</c> is the nested tool or method name.
    /// </summary>
    public string AgentSubheader { get; set; } = "Calling {0}";

    /// <summary>
    /// Gets or sets the header shown when an MCP tool is invoked; <c>{0}</c> is the MCP server name.
    /// </summary>
    public string McpHeader { get; set; } = "Calling {0} MCP";

    /// <summary>
    /// Gets or sets the subheader shown per MCP tool invocation; <c>{0}</c> is the tool name.
    /// </summary>
    public string McpSubheader { get; set; } = "Called {0}";

    /// <summary>
    /// Gets or sets a delegate that formats the header for an event, overriding the string
    /// templates entirely. Return <see langword="null"/> to suppress the header.
    /// </summary>
    public Func<ChatActivityEvent, string?>? FormatHeader { get; set; }

    /// <summary>
    /// Gets or sets a delegate that formats the subheader for an event, overriding the string
    /// templates entirely. Return <see langword="null"/> to suppress the subheader.
    /// </summary>
    public Func<ChatActivityEvent, string?>? FormatSubheader { get; set; }

    /// <summary>
    /// Formats the default header for a tool of the given kind using the string templates.
    /// </summary>
    /// <param name="toolKind">The kind of tool being invoked.</param>
    /// <param name="sourceName">The agent or MCP server display name, when applicable.</param>
    /// <returns>The formatted header.</returns>
    internal string FormatDefaultHeader(ToolKind toolKind, string? sourceName)
    {
        return toolKind switch
        {
            ToolKind.Agent => string.Format(CultureInfo.CurrentCulture, AgentHeader, sourceName),
            ToolKind.Mcp => string.Format(CultureInfo.CurrentCulture, McpHeader, sourceName),
            _ => PlainToolHeader,
        };
    }

    /// <summary>
    /// Formats the default subheader for a tool of the given kind using the string templates.
    /// </summary>
    /// <param name="toolKind">The kind of tool being invoked.</param>
    /// <param name="toolName">The name of the tool being invoked.</param>
    /// <returns>The formatted subheader.</returns>
    internal string FormatDefaultSubheader(ToolKind toolKind, string toolName)
    {
        string template = toolKind switch
        {
            ToolKind.Agent => AgentSubheader,
            ToolKind.Mcp => McpSubheader,
            _ => PlainToolSubheader,
        };

        return string.Format(CultureInfo.CurrentCulture, template, toolName);
    }
}
