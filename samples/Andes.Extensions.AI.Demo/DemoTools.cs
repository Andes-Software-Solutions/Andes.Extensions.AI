using System.ComponentModel;

namespace Andes.Extensions.AI.Demo;

/// <summary>
/// Local function tools showcasing the core package: <see cref="ChatProgress.Report(string, double?, double?)"/>
/// emits sub-statuses with numeric progress that surface under the tool's activity card.
/// </summary>
internal static class DemoTools
{
    [Description("Gets the current weather for a city.")]
    public static async Task<string> GetWeather(
        [Description("The city to get the weather for.")] string city,
        CancellationToken cancellationToken)
    {
        const int steps = 4;
        for (int i = 1; i <= steps; i++)
        {
            // Statuses deliberately omit the tool's arguments: progress events stay
            // argument-free unless ToolTrackingOptions.IncludeToolArguments is opted in.
            ChatProgress.Report($"Checking station {i} of {steps}…", i, steps);
            await Task.Delay(250, cancellationToken);
        }

        return $"The weather in {city} is sunny with a high of 25C.";
    }
}
