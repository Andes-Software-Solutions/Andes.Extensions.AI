using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Configures the behavior of <see cref="ToolTrackingChatClient"/>.
/// </summary>
public sealed class ToolTrackingOptions
{
    private TimeProvider _timeProvider = TimeProvider.System;

    /// <summary>
    /// Gets or sets a value indicating whether synthetic <see cref="ChatProgressContent"/> updates are
    /// emitted into the streamed response. Out-of-band observers are notified regardless of this setting.
    /// The default is <see langword="true"/>.
    /// </summary>
    public bool EmitProgressContent { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a final synthetic update carrying
    /// <see cref="UsageReportContent"/> is emitted at the end of the stream.
    /// The default is <see langword="true"/>.
    /// </summary>
    public bool EmitUsageReportContent { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the <see cref="ChatUsageReport"/> is attached to
    /// <see cref="ChatResponse.AdditionalProperties"/> under
    /// <see cref="ToolTrackingChatClient.UsageReportPropertyName"/> for non-streaming calls.
    /// The default is <see langword="true"/>.
    /// </summary>
    public bool AttachReportToResponse { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether stringified tool arguments are included in
    /// <see cref="ChatProgressUpdate.Arguments"/>. The default is <see langword="false"/>: progress
    /// events never carry prompt content, tool arguments, or tool results unless explicitly opted in.
    /// </summary>
    public bool IncludeToolArguments { get; set; }

    /// <summary>
    /// Gets the out-of-band observers notified of progress events and request completion.
    /// Observers registered in the dependency injection container as <see cref="IChatProgressObserver"/>
    /// are appended automatically by
    /// <see cref="ToolTrackingChatClientBuilderExtensions.UseToolTracking(ChatClientBuilder, Action{ToolTrackingOptions})"/>.
    /// </summary>
    public IList<IChatProgressObserver> Observers { get; } = [];

    /// <summary>
    /// Gets or sets a callback that classifies an <see cref="AITool"/> into a <see cref="ToolDescriptor"/>.
    /// When <see langword="null"/>, plain <see cref="AIFunction"/> tools are classified as
    /// <see cref="ToolKind.Function"/> and everything else as <see cref="ToolKind.Unknown"/>.
    /// This is the extension point through which additional tool categories receive first-class
    /// treatment: the <c>Andes.Extensions.AI.Mcp</c> and <c>Andes.Extensions.AI.Agent</c> satellite
    /// packages install composing classifiers for MCP tools and agents as tools.
    /// </summary>
    public Func<AITool, ToolDescriptor>? ToolClassifier { get; set; }

    /// <summary>
    /// Gets or sets a callback that formats the header message for a tool invocation.
    /// When <see langword="null"/>, the default produces "Calling {DisplayName} Tool" for functions,
    /// "Calling {Source} MCP" for MCP tools, and "Calling {DisplayName} Agent" for agents.
    /// </summary>
    public Func<ToolDescriptor, string>? HeaderFormatter { get; set; }

    /// <summary>
    /// Gets or sets the clock used for timestamps and durations. Replace in tests to make timing
    /// deterministic. The default is <see cref="TimeProvider.System"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public TimeProvider TimeProvider
    {
        get => _timeProvider;
        set => _timeProvider = value ?? throw new ArgumentNullException(nameof(value));
    }
}
