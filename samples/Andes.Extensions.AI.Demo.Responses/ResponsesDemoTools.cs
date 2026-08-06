using System.ComponentModel;

namespace Andes.Extensions.AI.Demo.Responses;

/// <summary>
/// Local function tools for the Responses demo: <see cref="ChatProgress.Report(string, double?, double?)"/>
/// emits sub-statuses with numeric progress that surface under the tool's activity card while the
/// model reasons between turns.
/// </summary>
internal static class ResponsesDemoTools
{
    [Description("Gets the current weather for a city.")]
    public static async Task<string> GetWeather(
        [Description("The city to get the weather for.")] string city,
        CancellationToken cancellationToken)
    {
        const int steps = 3;
        for (int i = 1; i <= steps; i++)
        {
            // Statuses deliberately omit the tool's arguments: progress events stay
            // argument-free unless ToolTrackingOptions.IncludeToolArguments is opted in.
            ChatProgress.Report($"Checking station {i} of {steps}…", i, steps);
            await Task.Delay(250, cancellationToken);
        }

        return $"The weather in {city} is sunny with a high of 25C.";
    }

    [Description("Converts a temperature between Celsius and Fahrenheit.")]
    public static string ConvertTemperature(
        [Description("The temperature value to convert.")] double value,
        [Description("The unit of the input value: C or F.")] string fromUnit)
    {
        ChatProgress.Report("Converting…");
        return fromUnit.Trim().ToUpperInvariant() switch
        {
            "C" => $"{value}C is {(value * 9 / 5) + 32}F.",
            "F" => $"{value}F is {(value - 32) * 5 / 9:0.#}C.",
            _ => $"Unknown unit '{fromUnit}'; expected C or F.",
        };
    }
}
