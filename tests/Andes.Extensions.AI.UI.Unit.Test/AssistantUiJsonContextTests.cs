using System.Text.Json;

namespace Andes.Extensions.AI.UI.Unit.Test;

public class AssistantUiJsonContextTests
{
    [Fact]
    public void Serialize_Snapshot_UsesCamelCaseStringEnumsAndOmitsNulls()
    {
        var snapshot = new AssistantStatusSnapshot
        {
            AssistantStatus = "Working",
            Phase = ActivityState.Running,
            Activities =
            [
                new AssistantActivity
                {
                    ScopeId = "scope-1",
                    DisplayName = "Andes Test MCP",
                    Kind = ToolKind.McpTool,
                    Source = "Andes Test MCP",
                    State = ActivityState.Completed,
                    Children =
                    [
                        new AssistantActivity
                        {
                            ScopeId = "scope-2",
                            DisplayName = "SearchDocs",
                            Kind = ToolKind.Function,
                        },
                    ],
                },
            ],
        };

        string json = JsonSerializer.Serialize(snapshot, AssistantUiJsonContext.Default.AssistantStatusSnapshot);

        Assert.Contains("\"displayName\":\"Andes Test MCP\"", json);
        Assert.Contains("\"kind\":\"McpTool\"", json);
        Assert.Contains("\"activities\":", json);
        Assert.DoesNotContain("MCP MCP", json);
        Assert.DoesNotContain("\"usage\"", json);
        Assert.DoesNotContain("\"text\"", json);
    }

    [Fact]
    public void SerializeRoundTrip_Event_PreservesFields()
    {
        var uiEvent = new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.ActivityStarted,
            ScopeId = "scope-1",
            DisplayName = "Research Agent",
            ToolKind = ToolKind.Agent,
            Source = "Research Agent",
            Depth = 1,
        };

        string json = JsonSerializer.Serialize(uiEvent, AssistantUiJsonContext.Default.AssistantUiEvent);
        AssistantUiEvent? roundTripped = JsonSerializer.Deserialize(json, AssistantUiJsonContext.Default.AssistantUiEvent);

        Assert.Contains("\"kind\":\"ActivityStarted\"", json);
        Assert.Contains("\"toolKind\":\"Agent\"", json);
        Assert.NotNull(roundTripped);
        Assert.Equal(AssistantUiEventKind.ActivityStarted, roundTripped!.Kind);
        Assert.Equal("Research Agent", roundTripped.DisplayName);
        Assert.Equal(ToolKind.Agent, roundTripped.ToolKind);
    }
}
