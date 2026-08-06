using Microsoft.Extensions.Configuration;

namespace Andes.Extensions.AI.Demo;

/// <summary>
/// Azure OpenAI connection settings loaded from the gitignored <c>appsettings.json</c>
/// (copy <c>appsettings.sample.json</c> and fill in the <c>AzureOpenAI</c> section).
/// </summary>
internal sealed class AzureOpenAISettings
{
    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    public string? Deployment { get; set; }

    public bool IsConfigured =>
        Endpoint is { } endpoint && Uri.TryCreate(endpoint, UriKind.Absolute, out _) &&
        !endpoint.Contains("your-resource", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(ApiKey) && !ApiKey.StartsWith('<') &&
        !string.IsNullOrWhiteSpace(Deployment) && !Deployment.StartsWith('<');

    public static AzureOpenAISettings Load()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        return configuration.GetSection("AzureOpenAI").Get<AzureOpenAISettings>() ?? new AzureOpenAISettings();
    }
}
