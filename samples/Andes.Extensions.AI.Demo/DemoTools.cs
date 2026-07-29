using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Demo;

/// <summary>
/// Local function tools showcasing the core package: <see cref="ChatProgress.Report(string, double?, double?)"/>
/// emits sub-statuses with numeric progress that surface under the tool's activity card, and
/// <see cref="CreatePlanTrip"/> invokes an agent from inside a plain tool body — the agent opens
/// its own child scope and renders as a nested activity card under the tool.
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

    public static AIFunction CreatePlanTrip(AIFunction packingAgent)
    {
        return AIFunctionFactory.Create(
            async ([Description("The destination city.")] string city, CancellationToken cancellationToken) =>
            {
                ChatProgress.Report("Building itinerary…");
                await Task.Delay(300, cancellationToken);

                // Invoking a WithTracking-wrapped agent inside a tool body opens a child scope:
                // the Packing Agent renders as its own card (with its usage) under PlanTrip.
                object? packing = await packingAgent.InvokeAsync(
                    new AIFunctionArguments { ["query"] = $"What should I pack for {city}?" },
                    cancellationToken);

                ChatProgress.Report("Finalizing plan…");
                return $"Trip plan for {city}: day 1 old town, day 2 viewpoints, day 3 markets. Packing advice: {packing}";
            },
            "PlanTrip",
            "Plans a short trip to a city, consulting the Packing Agent for what to bring.");
    }
}
