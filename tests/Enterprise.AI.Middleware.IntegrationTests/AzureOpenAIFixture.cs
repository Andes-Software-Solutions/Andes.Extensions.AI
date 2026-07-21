using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.IntegrationTests;

/// <summary>
/// Provides an Azure OpenAI <see cref="IChatClient"/> configured from environment variables.
/// Tests skip when the environment is not configured.
/// </summary>
/// <remarks>
/// Required environment variables:
/// <list type="bullet">
///   <item><description><c>AZURE_OPENAI_ENDPOINT</c> — for example <c>https://my-resource.openai.azure.com</c>.</description></item>
///   <item><description><c>AZURE_OPENAI_API_KEY</c> — an API key for the resource.</description></item>
///   <item><description><c>AZURE_OPENAI_DEPLOYMENT</c> — a chat model deployment name (for example <c>gpt-4o-mini</c>).</description></item>
/// </list>
/// </remarks>
public sealed class AzureOpenAIFixture
{
    public AzureOpenAIFixture()
    {
        Endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        ApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        Deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");
    }

    public string? Endpoint { get; }

    public string? ApiKey { get; }

    public string? Deployment { get; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Deployment);

    /// <summary>
    /// Skips the current test when the Azure OpenAI environment variables are not set.
    /// </summary>
    public void SkipUnlessConfigured()
    {
        Skip.If(
            !IsConfigured,
            "Set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, and AZURE_OPENAI_DEPLOYMENT to run Azure OpenAI integration tests.");
    }

    /// <summary>
    /// Creates the raw provider-level chat client for the configured deployment.
    /// </summary>
    public IChatClient CreateProviderClient()
    {
        return new AzureOpenAIClient(new Uri(Endpoint!), new ApiKeyCredential(ApiKey!))
            .GetChatClient(Deployment!)
            .AsIChatClient();
    }
}
