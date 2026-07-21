using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Provides request-level helpers for the tool-tracking middleware.
/// </summary>
public static class ChatOptionsTrackingExtensions
{
    /// <summary>
    /// Stamps a correlation tag on the request that is copied onto every
    /// <see cref="ChatActivityEvent"/> it produces — for example a SignalR connection id or a
    /// tenant identifier.
    /// </summary>
    /// <param name="options">The options for the request.</param>
    /// <param name="tag">The opaque tag to correlate events with.</param>
    /// <returns>The same <paramref name="options"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="tag"/> is <see langword="null"/>.</exception>
    public static ChatOptions WithActivityTag(this ChatOptions options, string tag)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tag);

        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[TrackingToolAnnotations.ActivityTagKey] = tag;
        return options;
    }

    /// <summary>
    /// Removes every in-band <see cref="ActivityStatusContent"/> item from the given messages.
    /// Call this before echoing a streamed response back into the conversation history, so
    /// synthetic status updates are never sent to the model.
    /// </summary>
    /// <param name="messages">The messages to clean in place.</param>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is <see langword="null"/>.</exception>
    public static void RemoveActivityContent(this IList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        foreach (ChatMessage message in messages)
        {
            for (int i = message.Contents.Count - 1; i >= 0; i--)
            {
                if (message.Contents[i] is ActivityStatusContent)
                {
                    message.Contents.RemoveAt(i);
                }
            }
        }
    }
}
