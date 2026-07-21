using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Represents an activity status update injected in-band into a streaming chat response
/// by the tool-tracking middleware.
/// </summary>
/// <remarks>
/// <para>
/// Instances appear inside <see cref="ChatResponseUpdate.Contents"/> alongside the model's own
/// content. Consumers can filter for them with
/// <c>update.Contents.OfType&lt;ActivityStatusContent&gt;()</c>.
/// </para>
/// <para>
/// To serialize this type polymorphically as an <see cref="AIContent"/> (for example when
/// forwarding updates over SSE), register it on your <see cref="System.Text.Json.JsonSerializerOptions"/>
/// via <see cref="EnterpriseAIJsonUtilities.AddActivityStatusContent"/>.
/// </para>
/// </remarks>
public sealed class ActivityStatusContent : AIContent
{
    /// <summary>
    /// Gets or sets the lifecycle stage this status update represents.
    /// </summary>
    public ChatActivityEventKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the kind of tool involved, or <see langword="null"/> when not tool-related.
    /// </summary>
    public ToolKind? ToolKind { get; set; }

    /// <summary>
    /// Gets or sets the human-readable status header (for example <c>"Calling Tool(s)"</c>).
    /// </summary>
    public string? Header { get; set; }

    /// <summary>
    /// Gets or sets the human-readable status subheader (for example <c>"Called search_document"</c>).
    /// </summary>
    public string? Subheader { get; set; }

    /// <summary>
    /// Gets or sets the provider-assigned call id of the tool invocation (matching
    /// <see cref="ToolCallContent.CallId"/>), when available.
    /// </summary>
    public string? ToolCallId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the activity scope that produced this update.
    /// </summary>
    public string ScopeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the identifier of the parent scope, or <see langword="null"/> for the root scope.
    /// </summary>
    public string? ParentScopeId { get; set; }

    /// <summary>
    /// Gets or sets the time at which the underlying activity occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the token usage associated with this update, populated on usage and
    /// terminal request updates.
    /// </summary>
    public UsageDetails? Usage { get; set; }
}
