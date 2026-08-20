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
                    DisplayName = "get_forecast",
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

        Assert.Contains("\"displayName\":\"get_forecast\"", json);
        Assert.Contains("\"source\":\"Andes Test MCP\"", json);
        Assert.Contains("\"kind\":\"McpTool\"", json);
        Assert.Contains("\"activities\":", json);
        Assert.DoesNotContain("MCP MCP", json);
        Assert.DoesNotContain("\"usage\"", json);
        Assert.DoesNotContain("\"text\"", json);
        Assert.DoesNotContain("\"reasoningText\"", json);
        Assert.DoesNotContain("\"metadata\"", json);
    }

    [Fact]
    public void Serialize_SnapshotWithReasoningText_EmitsCamelCaseProperty()
    {
        var snapshot = new AssistantStatusSnapshot
        {
            Phase = ActivityState.Running,
            ReasoningText = "planning the call",
        };
        var uiEvent = new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.ReasoningDelta,
            Text = "planning the call",
        };

        string snapshotJson = JsonSerializer.Serialize(snapshot, AssistantUiJsonContext.Default.AssistantStatusSnapshot);
        string eventJson = JsonSerializer.Serialize(uiEvent, AssistantUiJsonContext.Default.AssistantUiEvent);

        Assert.Contains("\"reasoningText\":\"planning the call\"", snapshotJson);
        Assert.Contains("\"kind\":\"ReasoningDelta\"", eventJson);
        Assert.Contains("\"text\":\"planning the call\"", eventJson);
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

    [Fact]
    public void SerializeRoundTrip_EventWithMetadata_PreservesEntriesWithVerbatimKeys()
    {
        var uiEvent = new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.Finished,
            Metadata = new Dictionary<string, string>
            {
                ["MessageId"] = "m1",
                ["conversation-id"] = "c1",
            },
        };

        string json = JsonSerializer.Serialize(uiEvent, AssistantUiJsonContext.Default.AssistantUiEvent);
        AssistantUiEvent? roundTripped = JsonSerializer.Deserialize(json, AssistantUiJsonContext.Default.AssistantUiEvent);

        // The camelCase naming policy applies to property names only — dictionary keys travel verbatim.
        Assert.Contains("\"metadata\":{", json);
        Assert.Contains("\"MessageId\":\"m1\"", json);
        Assert.Contains("\"conversation-id\":\"c1\"", json);
        Assert.NotNull(roundTripped);
        Assert.NotNull(roundTripped!.Metadata);
        Assert.Equal("m1", roundTripped.Metadata!["MessageId"]);
        Assert.Equal("c1", roundTripped.Metadata["conversation-id"]);
    }

    [Fact]
    public void Serialize_SnapshotWithMetadata_EmitsCamelCasePropertyWithVerbatimKeys()
    {
        var snapshot = new AssistantStatusSnapshot
        {
            Phase = ActivityState.Completed,
            Metadata = new Dictionary<string, string> { ["MessageId"] = "m1" },
        };

        string json = JsonSerializer.Serialize(snapshot, AssistantUiJsonContext.Default.AssistantStatusSnapshot);

        Assert.Contains("\"metadata\":{", json);
        Assert.Contains("\"MessageId\":\"m1\"", json);
    }
}
