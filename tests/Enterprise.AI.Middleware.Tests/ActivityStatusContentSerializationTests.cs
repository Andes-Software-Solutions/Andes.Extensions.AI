using System.Text.Json;
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests;

public sealed class ActivityStatusContentSerializationTests
{
    private static ActivityStatusContent CreateContent()
    {
        return new ActivityStatusContent
        {
            Kind = ChatActivityEventKind.ToolCallStarted,
            ToolKind = ToolKind.Mcp,
            Header = "Calling GitHub MCP",
            Subheader = "Called search_issues",
            ToolCallId = "call-9",
            ScopeId = "scope-1",
            ParentScopeId = "scope-0",
            Timestamp = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero),
            Usage = new UsageDetails { InputTokenCount = 12, OutputTokenCount = 3, TotalTokenCount = 15 },
            Progress = 4,
            ProgressTotal = 9,
        };
    }

    [Fact]
    public void AddActivityStatusContent_SerializePolymorphically_EmitsTypeDiscriminator()
    {
        JsonSerializerOptions jsonOptions = new(AIJsonUtilities.DefaultOptions);
        jsonOptions.AddActivityStatusContent();

        string json = JsonSerializer.Serialize<AIContent>(CreateContent(), jsonOptions);

        Assert.Contains(EnterpriseAIJsonUtilities.ActivityStatusContentTypeId, json, StringComparison.Ordinal);
    }

    [Fact]
    public void AddActivityStatusContent_RoundTrip_PreservesAllProperties()
    {
        JsonSerializerOptions jsonOptions = new(AIJsonUtilities.DefaultOptions);
        jsonOptions.AddActivityStatusContent();
        ActivityStatusContent original = CreateContent();

        string json = JsonSerializer.Serialize<AIContent>(original, jsonOptions);
        AIContent? deserialized = JsonSerializer.Deserialize<AIContent>(json, jsonOptions);

        ActivityStatusContent typed = Assert.IsType<ActivityStatusContent>(deserialized);
        Assert.Equal(original.Kind, typed.Kind);
        Assert.Equal(original.ToolKind, typed.ToolKind);
        Assert.Equal(original.Header, typed.Header);
        Assert.Equal(original.Subheader, typed.Subheader);
        Assert.Equal(original.ToolCallId, typed.ToolCallId);
        Assert.Equal(original.ScopeId, typed.ScopeId);
        Assert.Equal(original.ParentScopeId, typed.ParentScopeId);
        Assert.Equal(original.Timestamp, typed.Timestamp);
        Assert.Equal(original.Usage!.TotalTokenCount, typed.Usage!.TotalTokenCount);
        Assert.Equal(original.Progress, typed.Progress);
        Assert.Equal(original.ProgressTotal, typed.ProgressTotal);
    }

    [Fact]
    public void AddActivityStatusContent_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => EnterpriseAIJsonUtilities.AddActivityStatusContent(null!));
    }
}
