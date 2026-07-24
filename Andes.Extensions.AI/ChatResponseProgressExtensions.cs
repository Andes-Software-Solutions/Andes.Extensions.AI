using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Provides helpers for removing Andes-specific content items from responses so they can be
/// persisted into conversation history or serialized with the standard
/// <see cref="AIContent"/> contract.
/// </summary>
public static class ChatResponseProgressExtensions
{
    /// <summary>
    /// Removes <see cref="ChatProgressContent"/> and <see cref="UsageReportContent"/> items from all
    /// messages in the response, and removes the <see cref="ChatUsageReport"/> attached under
    /// <see cref="ToolTrackingChatClient.UsageReportPropertyName"/>, so the response round-trips
    /// through standard serialization.
    /// </summary>
    /// <param name="response">The response to strip.</param>
    /// <returns>The same <paramref name="response"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
    public static ChatResponse StripProgressContent(this ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        foreach (ChatMessage message in response.Messages)
        {
            StripProgressContent(message);
        }

        response.AdditionalProperties?.Remove(ToolTrackingChatClient.UsageReportPropertyName);
        return response;
    }

    /// <summary>
    /// Removes <see cref="ChatProgressContent"/> and <see cref="UsageReportContent"/> items from the message.
    /// </summary>
    /// <param name="message">The message to strip.</param>
    /// <returns>The same <paramref name="message"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    public static ChatMessage StripProgressContent(this ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        for (int i = message.Contents.Count - 1; i >= 0; i--)
        {
            if (message.Contents[i] is ChatProgressContent or UsageReportContent)
            {
                message.Contents.RemoveAt(i);
            }
        }

        return message;
    }
}
