using Andes.Extensions.AI.Mcp.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Andes.Extensions.AI.Mcp.Unit.Test;

public class McpToolTrackingExtensionsTests(InMemoryMcpFixture fixture) : IClassFixture<InMemoryMcpFixture>
{
    private readonly InMemoryMcpFixture _fixture = fixture;

    [Fact]
    public void WithTracking_NullArguments_Throw()
    {
        McpClientTool tool = _fixture.EchoTool;

        Assert.Throws<ArgumentNullException>(() => ((McpClientTool)null!).WithTracking(_fixture.Client));
        Assert.Throws<ArgumentNullException>(() => tool.WithTracking((McpClient)null!));
        Assert.Throws<ArgumentNullException>(() => tool.WithTracking((string)null!));
        Assert.Throws<ArgumentException>(() => tool.WithTracking(""));
        Assert.Throws<ArgumentNullException>(() => ((IEnumerable<McpClientTool>)null!).WithTracking(_fixture.Client));
        Assert.Throws<ArgumentNullException>(() => _fixture.Tools.WithTracking(null!));
    }

    [Fact]
    public void WithTracking_PreservesNameDescriptionAndSchema()
    {
        McpClientTool tool = _fixture.EchoTool;

        AIFunction wrapped = tool.WithTracking(_fixture.Client);

        Assert.Equal(tool.Name, wrapped.Name);
        Assert.Equal(tool.Description, wrapped.Description);
        Assert.Equal(tool.JsonSchema.GetRawText(), wrapped.JsonSchema.GetRawText());
    }

    [Fact]
    public async Task WithTracking_Invocation_StillReachesServer()
    {
        AIFunction wrapped = _fixture.EchoTool.WithTracking(_fixture.Client);

        object? result = await wrapped.InvokeAsync(new AIFunctionArguments { ["message"] = "hello" });

        Assert.Contains("Echo: hello", result?.ToString());
    }

    [Fact]
    public void WithTracking_ClientOverload_UsesServerInfoName()
    {
        ToolTrackingOptions options = new ToolTrackingOptions().UseMcpToolClassification();
        AIFunction wrapped = _fixture.EchoTool.WithTracking(_fixture.Client);

        ToolDescriptor descriptor = options.ToolClassifier!(wrapped);

        Assert.Equal(ToolKind.McpTool, descriptor.Kind);
        Assert.Equal(InMemoryMcpFixture.ServerName, descriptor.Source);
        Assert.Equal("echo", descriptor.Name);
    }

    [Fact]
    public void WithTracking_ExplicitServerName_UsedAsSource()
    {
        ToolTrackingOptions options = new ToolTrackingOptions().UseMcpToolClassification();
        AIFunction wrapped = _fixture.EchoTool.WithTracking("Custom Server");

        ToolDescriptor descriptor = options.ToolClassifier!(wrapped);

        Assert.Equal(ToolKind.McpTool, descriptor.Kind);
        Assert.Equal("Custom Server", descriptor.Source);
    }

    [Fact]
    public void WithTracking_Enumerable_WrapsEveryTool()
    {
        ToolTrackingOptions options = new ToolTrackingOptions().UseMcpToolClassification();

        IList<AITool> wrapped = _fixture.Tools.WithTracking(_fixture.Client);

        Assert.Equal(_fixture.Tools.Count, wrapped.Count);
        Assert.All(wrapped, tool =>
        {
            ToolDescriptor descriptor = options.ToolClassifier!(tool);
            Assert.Equal(ToolKind.McpTool, descriptor.Kind);
            Assert.Equal(InMemoryMcpFixture.ServerName, descriptor.Source);
        });
    }

    [Fact]
    public void WithTracking_GetService_ExposesMcpClientToolThroughWrapper()
    {
        AIFunction wrapped = _fixture.EchoTool.WithTracking(_fixture.Client);

        Assert.Same(_fixture.EchoTool, wrapped.GetService(typeof(McpClientTool)));
    }
}
