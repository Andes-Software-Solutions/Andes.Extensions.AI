using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace Andes.Extensions.AI.Integration.Test;

/// <summary>
/// Azure OpenAI connection settings bound from the "AzureOpenAI" section of
/// appsettings.integration.json (gitignored; see appsettings.integration.sample.json).
/// </summary>
public sealed class AzureOpenAISettings
{
    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    public string? Deployment { get; set; }

    /// <summary>
    /// Optional reasoning-capable deployment (gpt-5 family / o-series) used by the Responses API
    /// tests; when absent those tests skip while the chat-deployment tests still run.
    /// </summary>
    public string? ResponsesDeployment { get; set; }
}

/// <summary>
/// Loads Azure OpenAI settings from appsettings.integration.json and builds the pipeline under test.
/// Tests must skip when <see cref="IsConfigured"/> is false.
/// </summary>
public sealed class AzureOpenAIFixture
{
    public const string SkipReason =
        "appsettings.integration.json is missing or incomplete; copy appsettings.integration.sample.json and fill in the AzureOpenAI section.";

    public const string ResponsesSkipReason =
        "AzureOpenAI:ResponsesDeployment is not configured; add a reasoning-capable deployment name to appsettings.integration.json to run the Responses API tests.";

    public AzureOpenAIFixture()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.integration.json", optional: true)
            .Build();
        Settings = configuration.GetSection("AzureOpenAI").Get<AzureOpenAISettings>() ?? new AzureOpenAISettings();
    }

    public AzureOpenAISettings Settings { get; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Settings.Endpoint)
        && !string.IsNullOrWhiteSpace(Settings.ApiKey)
        && !string.IsNullOrWhiteSpace(Settings.Deployment);

    public bool IsResponsesConfigured =>
        !string.IsNullOrWhiteSpace(Settings.Endpoint)
        && !string.IsNullOrWhiteSpace(Settings.ApiKey)
        && !string.IsNullOrWhiteSpace(Settings.ResponsesDeployment);

    public IChatClient CreatePipeline(Action<ToolTrackingOptions>? configure = null)
    {
        var azureClient = new AzureOpenAIClient(new Uri(Settings.Endpoint!), new AzureKeyCredential(Settings.ApiKey!));
        return azureClient
            .GetChatClient(Settings.Deployment!)
            .AsIChatClient()
            .AsBuilder()
            .UseToolTracking(configure)
            .UseFunctionInvocation()
            .Build();
    }
}
