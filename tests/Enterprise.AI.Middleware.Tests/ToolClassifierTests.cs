using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Enterprise.AI.Middleware.Tracking.Internal;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests;

public sealed class ToolClassifierTests : IClassFixture<InMemoryMcpFixture>
{
    private readonly InMemoryMcpFixture _mcp;

    public ToolClassifierTests(InMemoryMcpFixture mcp)
    {
        _mcp = mcp;
    }

    private static AIFunction PlainTool => AIFunctionFactory.Create(() => "ok", "plain_tool");

    [Fact]
    public void Classify_PlainAIFunction_ReturnsFunctionKind()
    {
        ToolClassification classification = ToolClassifier.Classify(PlainTool, new ToolTrackingOptions());

        Assert.Equal(ToolKind.Function, classification.Kind);
        Assert.Null(classification.SourceName);
    }

    [Fact]
    public void Classify_McpClientTool_ReturnsMcpKindWithDefaultServerName()
    {
        ToolClassification classification = ToolClassifier.Classify(_mcp.EchoTool, new ToolTrackingOptions());

        Assert.Equal(ToolKind.Mcp, classification.Kind);
        Assert.Equal("MCP", classification.SourceName);
    }

    [Fact]
    public void Classify_McpToolWithoutAnnotation_PrefersResolverOverDefault()
    {
        var options = new ToolTrackingOptions
        {
            McpServerNameResolver = _ => "Resolved Server",
        };

        ToolClassification classification = ToolClassifier.Classify(_mcp.EchoTool, options);

        Assert.Equal(ToolKind.Mcp, classification.Kind);
        Assert.Equal("Resolved Server", classification.SourceName);
    }

    [Fact]
    public void Classify_AnnotatedMcpTool_UsesServerInfoName()
    {
        AIFunction annotated = _mcp.EchoTool.WithTrackingMetadata(_mcp.Client);

        ToolClassification classification = ToolClassifier.Classify(annotated, new ToolTrackingOptions());

        Assert.Equal(ToolKind.Mcp, classification.Kind);
        Assert.Equal(InMemoryMcpFixture.ServerName, classification.SourceName);
    }

    [Fact]
    public void Classify_AnnotationPresent_OverridesTypeCheck()
    {
        AIFunction annotated = new AnnotatedAIFunction(_mcp.EchoTool, ToolKind.Agent, "Disguised Agent");

        ToolClassification classification = ToolClassifier.Classify(annotated, new ToolTrackingOptions());

        Assert.Equal(ToolKind.Agent, classification.Kind);
        Assert.Equal("Disguised Agent", classification.SourceName);
    }

    [Fact]
    public void Classify_AgentAnnotatedFunction_ReturnsAgentKindWithSourceName()
    {
        AIFunction annotated = new AnnotatedAIFunction(PlainTool, ToolKind.Agent, "Researcher");

        ToolClassification classification = ToolClassifier.Classify(annotated, new ToolTrackingOptions());

        Assert.Equal(ToolKind.Agent, classification.Kind);
        Assert.Equal("Researcher", classification.SourceName);
    }

    [Fact]
    public void Classify_AgentNameResolver_ClassifiesUnannotatedFunctionAsAgent()
    {
        var options = new ToolTrackingOptions
        {
            AgentNameResolver = function => function.Name == "plain_tool" ? "Heuristic Agent" : null,
        };

        ToolClassification classification = ToolClassifier.Classify(PlainTool, options);

        Assert.Equal(ToolKind.Agent, classification.Kind);
        Assert.Equal("Heuristic Agent", classification.SourceName);
    }

    [Fact]
    public void Classify_StringAnnotationValue_ParsesToolKind()
    {
        AIFunction function = AIFunctionFactory.Create(
            () => "ok",
            new AIFunctionFactoryOptions
            {
                Name = "string_annotated",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    [TrackingToolAnnotations.ToolKindKey] = "mcp",
                    [TrackingToolAnnotations.SourceNameKey] = "String Server",
                },
            });

        ToolClassification classification = ToolClassifier.Classify(function, new ToolTrackingOptions());

        Assert.Equal(ToolKind.Mcp, classification.Kind);
        Assert.Equal("String Server", classification.SourceName);
    }
}
