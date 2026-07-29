using System.ComponentModel;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Demo;

/// <summary>
/// Agent-as-tool showcasing the Agent satellite: the agent is exposed to the outer pipeline
/// via <c>WithTracking</c>, which attributes the agent run's token usage to its tool scope.
/// </summary>
internal static class DemoAgents
{
    public static AIAgent CreateResearchAgent(AzureOpenAISettings settings)
    {
        // The inner agent runs over a raw (untracked) chat client: the outer pipeline
        // attributes the agent's usage via WithTracking's usage capture (trackUsage: true
        // default); a tracked inner pipeline would double-count the same tokens.
        IChatClient rawClient = new AzureOpenAIClient(
                new Uri(settings.Endpoint!),
                new AzureKeyCredential(settings.ApiKey!))
            .GetChatClient(settings.Deployment!)
            .AsIChatClient();

        return rawClient.AsAIAgent(
            instructions: "You are a travel researcher. Answer briefly, using the SearchNotes tool when helpful.",
            name: "Research Agent",
            description: "Researches travel topics, such as what to pack for a destination, and summarizes findings.",
            tools: [AIFunctionFactory.Create(SearchNotes)]);
    }

    [Description("Searches the travel notes knowledge base for a topic.")]
    private static string SearchNotes([Description("The topic to search for.")] string topic)
    {
        // Ambient reporting flows from inside the agent's own function into the outer
        // pipeline's activity tree — no plumbing required. The status omits the argument:
        // progress events never carry tool arguments unless explicitly opted in.
        ChatProgress.Report("Searching notes…");
        return $"Notes on {topic}: pack layers, sunscreen, and a rain shell; altitude 2,850m.";
    }
}
