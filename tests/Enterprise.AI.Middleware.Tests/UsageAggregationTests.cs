using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Enterprise.AI.Middleware.Tracking.Internal;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests;

public sealed class UsageAggregationTests
{
    [Fact]
    public void Add_MultipleUsagesSameAccumulator_SumsAllCounts()
    {
        var accumulator = new UsageAccumulator();
        accumulator.Add(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 4, TotalTokenCount = 14 });
        accumulator.Add(new UsageDetails { InputTokenCount = 5, OutputTokenCount = 6, TotalTokenCount = 11 });

        UsageDetails total = accumulator.Snapshot();

        Assert.Equal(15, total.InputTokenCount);
        Assert.Equal(10, total.OutputTokenCount);
        Assert.Equal(25, total.TotalTokenCount);
    }

    [Fact]
    public void Add_NullCounts_RemainUnknownRatherThanZero()
    {
        var accumulator = new UsageAccumulator();
        accumulator.Add(new UsageDetails { OutputTokenCount = 3 });

        UsageDetails total = accumulator.Snapshot();

        Assert.Null(total.InputTokenCount);
        Assert.Equal(3, total.OutputTokenCount);
    }

    [Fact]
    public void Snapshot_ReturnsDefensiveCopy()
    {
        var accumulator = new UsageAccumulator();
        accumulator.Add(new UsageDetails { TotalTokenCount = 5 });

        UsageDetails first = accumulator.Snapshot();
        accumulator.Add(new UsageDetails { TotalTokenCount = 5 });

        Assert.Equal(5, first.TotalTokenCount);
        Assert.Equal(10, accumulator.Snapshot().TotalTokenCount);
    }

    [Fact]
    public async Task Add_ConcurrentWriters_LosesNoTokens()
    {
        var accumulator = new UsageAccumulator();
        const int Writers = 8;
        const int PerWriter = 500;

        await Task.WhenAll(Enumerable.Range(0, Writers).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < PerWriter; i++)
            {
                accumulator.Add(new UsageDetails { InputTokenCount = 1, OutputTokenCount = 2, TotalTokenCount = 3 });
            }
        })));

        UsageDetails total = accumulator.Snapshot();
        Assert.Equal(Writers * PerWriter, total.InputTokenCount);
        Assert.Equal(Writers * PerWriter * 2, total.OutputTokenCount);
        Assert.Equal(Writers * PerWriter * 3, total.TotalTokenCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_TwoModels_ProducesTwoBreakdownEntries()
    {
        AIFunction tool = AIFunctionFactory.Create(() => "ok", "any_tool");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(
                ScriptedChatClient.FunctionCallUpdate("call-1", "any_tool"),
                ScriptedChatClient.UsageUpdate(10, 5, "model-a"))
            .AddTurn(
                ScriptedChatClient.TextUpdate("done"),
                ScriptedChatClient.UsageUpdate(20, 7, "model-b"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("go"),
            new ChatOptions { Tools = [tool] }));

        ChatActivityReport report = Assert.Single(observer.Reports);
        Assert.Equal(2, report.UsageByModel.Count);

        ModelUsageBreakdown modelA = report.UsageByModel.Single(b => b.ModelId == "model-a");
        Assert.Equal(15, modelA.TotalTokenCount);
        ModelUsageBreakdown modelB = report.UsageByModel.Single(b => b.ModelId == "model-b");
        Assert.Equal(27, modelB.TotalTokenCount);
        Assert.Equal(42, report.TotalUsage.TotalTokenCount);
    }
}
