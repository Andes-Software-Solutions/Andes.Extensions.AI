using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Enterprise.AI.Middleware.Tests;

public sealed class McpToolTrackingExtensionsTests : IClassFixture<InMemoryMcpFixture>
{
    private readonly InMemoryMcpFixture _mcp;

    public McpToolTrackingExtensionsTests(InMemoryMcpFixture mcp)
    {
        _mcp = mcp;
    }

    [Fact]
    public void WithTrackingMetadata_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(
            () => McpToolTrackingExtensions.WithTrackingMetadata((McpClientTool)null!, _mcp.Client));
        Assert.Throws<ArgumentNullException>(
            () => _mcp.EchoTool.WithTrackingMetadata((McpClient)null!));
        Assert.Throws<ArgumentException>(
            () => _mcp.EchoTool.WithTrackingMetadata(string.Empty));
    }

    [Fact]
    public void WithTrackingMetadata_PreservesNameDescriptionAndSchema()
    {
        AIFunction annotated = _mcp.EchoTool.WithTrackingMetadata(_mcp.Client);

        Assert.Equal(_mcp.EchoTool.Name, annotated.Name);
        Assert.Equal(_mcp.EchoTool.Description, annotated.Description);
        Assert.Equal(_mcp.EchoTool.JsonSchema.ToString(), annotated.JsonSchema.ToString());
    }

    [Fact]
    public async Task WithTrackingMetadata_Invocation_StillReachesServer()
    {
        AIFunction annotated = _mcp.EchoTool.WithTrackingMetadata(_mcp.Client);

        object? result = await annotated.InvokeAsync(new AIFunctionArguments
        {
            ["message"] = "hello",
        });

        Assert.Contains("Echo: hello", result?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WithTrackingMetadata_ExplicitName_UsedAsSourceName()
    {
        AIFunction annotated = _mcp.EchoTool.WithTrackingMetadata("Custom Server");

        Assert.Equal("Custom Server", annotated.AdditionalProperties[TrackingToolAnnotations.SourceNameKey]);
        Assert.Equal(ToolKind.Mcp, annotated.AdditionalProperties[TrackingToolAnnotations.ToolKindKey]);
    }

    [Fact]
    public void WithTrackingMetadata_Enumerable_AnnotatesEveryTool()
    {
        IList<AITool> annotated = _mcp.Tools.WithTrackingMetadata(_mcp.Client);

        Assert.Equal(_mcp.Tools.Count, annotated.Count);
        Assert.All(annotated, tool =>
        {
            var function = Assert.IsAssignableFrom<AIFunction>(tool);
            Assert.Equal(
                InMemoryMcpFixture.ServerName,
                function.AdditionalProperties[TrackingToolAnnotations.SourceNameKey]);
        });
    }
}
