using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Carries a <see cref="ChatProgressUpdate"/> in-band as a content item within a streamed
/// <see cref="ChatResponseUpdate"/>.
/// </summary>
/// <remarks>
/// Synthetic updates carrying this content contain no <see cref="TextContent"/>, so text accumulation
/// helpers are unaffected. Because this type is not part of the <see cref="AIContent"/> polymorphic
/// serialization contract, call <see cref="ChatResponseProgressExtensions.StripProgressContent(ChatResponse)"/>
/// before persisting or serializing responses that may contain it.
/// </remarks>
public sealed class ChatProgressContent : AIContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatProgressContent"/> class.
    /// </summary>
    /// <param name="progress">The progress event to carry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="progress"/> is <see langword="null"/>.</exception>
    public ChatProgressContent(ChatProgressUpdate progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        Progress = progress;
    }

    /// <summary>
    /// Gets the progress event carried by this content item.
    /// </summary>
    public ChatProgressUpdate Progress { get; }
}
