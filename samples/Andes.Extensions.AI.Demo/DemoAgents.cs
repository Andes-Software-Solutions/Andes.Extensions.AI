using System.ComponentModel;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Demo;

/// <summary>
/// Agents-as-tools showcasing the Agent satellite: each agent is exposed via <c>WithTracking</c>,
/// which attributes its run's token usage to its own tracking scope. The Packing Agent nests
/// inside the Research Agent (and inside the PlanTrip tool), rendering as a child activity card.
/// </summary>
internal static class DemoAgents
{
    public static AIAgent CreateResearchAgent(AzureOpenAISettings settings, AIFunction packingAgent)
    {
        return CreateRawClient(settings).AsAIAgent(
            instructions: "You are a travel researcher. Answer briefly. Use the SearchNotes tool " +
                          "for destination facts and consult the Packing Agent tool for packing advice.",
            name: "Research Agent",
            description: "Researches travel topics, such as what to pack for a destination, and summarizes findings.",
            tools: [AIFunctionFactory.Create(SearchNotes), packingAgent]);
    }

    public static AIAgent CreatePackingAgent(AzureOpenAISettings settings)
    {
        return CreateRawClient(settings).AsAIAgent(
            instructions: "You suggest what to pack for a destination. Use the CheckEssentials tool, " +
                          "then answer in one short sentence.",
            name: "Packing Agent",
            description: "Suggests what to pack for a destination.",
            tools: [AIFunctionFactory.Create(CheckEssentials)]);
    }

    private static IChatClient CreateRawClient(AzureOpenAISettings settings)
    {
        // Inner agents run over raw (untracked) chat clients: the outer pipeline attributes each
        // agent's usage via WithTracking's usage capture (trackUsage: true default); a tracked
        // inner pipeline would double-count the same tokens.
        return new AzureOpenAIClient(
                new Uri(settings.Endpoint!),
                new AzureKeyCredential(settings.ApiKey!))
            .GetChatClient(settings.Deployment!)
            .AsIChatClient();
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

    [Description("Checks the packing essentials list for a destination type.")]
    private static string CheckEssentials([Description("The destination type, such as mountain, beach, or city.")] string destinationType)
    {
        ChatProgress.Report("Checking essentials…");
        return $"Essentials for {destinationType}: layers, sunscreen, a rain shell, and comfortable shoes.";
    }
}
