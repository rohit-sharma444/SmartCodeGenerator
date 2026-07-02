using NaturalLanguageEngine.Core;
using NaturalLanguageEngine.Models.Diagnostics;
using Xunit;

namespace NaturalLanguageEngine.Tests.Core;

public class PipelineTests
{
    /// <summary>A stage double that records whether it ran and can optionally throw.</summary>
    private sealed class RecordingStage : PipelineStageBase
    {
        private readonly Action<AnalysisContext>? _onExecute;
        private readonly Exception? _throw;

        public RecordingStage(string name, int order, Action<AnalysisContext>? onExecute = null, Exception? throwException = null)
        {
            Name = name;
            Order = order;
            _onExecute = onExecute;
            _throw = throwException;
        }

        public override string Name { get; }

        public override int Order { get; }

        public bool WasExecuted { get; private set; }

        public override AnalysisContext Execute(AnalysisContext context)
        {
            WasExecuted = true;

            if (_throw is not null)
                throw _throw;

            _onExecute?.Invoke(context);
            return context;
        }
    }

    [Fact]
    public void Execute_RunsStagesInOrderRegardlessOfAddOrder()
    {
        var executionOrder = new List<string>();

        var second = new RecordingStage("second", 20, ctx => executionOrder.Add("second"));
        var first = new RecordingStage("first", 10, ctx => executionOrder.Add("first"));

        // Added out of order on purpose - Pipeline must still run by Order.
        var pipeline = new PipelineBuilder().Add(second).Add(first).Build();

        pipeline.Execute("hello world");

        Assert.Equal(new[] { "first", "second" }, executionOrder);
    }

    [Fact]
    public void Execute_AllStagesSucceed_ProducesNoDiagnostics()
    {
        var pipeline = new PipelineBuilder()
            .Add(new RecordingStage("a", 10))
            .Add(new RecordingStage("b", 20))
            .Build();

        var context = pipeline.Execute("hello world");

        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public void Execute_StageThrows_IsCapturedAsCriticalDiagnostic_NotPropagated()
    {
        var pipeline = new PipelineBuilder()
            .Add(new RecordingStage("failing", 10, throwException: new InvalidOperationException("boom")))
            .Build();

        // Must not throw.
        var context = pipeline.Execute("hello world");

        var diagnostic = Assert.Single(context.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Critical, diagnostic.Severity);
        Assert.Contains("failing", diagnostic.Code);
        Assert.Contains("boom", diagnostic.Message);
    }

    [Fact]
    public void Execute_StageThrows_StopsLaterStagesFromRunning()
    {
        var third = new RecordingStage("third", 30);

        var pipeline = new PipelineBuilder()
            .Add(new RecordingStage("first", 10))
            .Add(new RecordingStage("failing", 20, throwException: new InvalidOperationException("boom")))
            .Add(third)
            .Build();

        pipeline.Execute("hello world");

        Assert.False(third.WasExecuted);
    }

    [Fact]
    public void Execute_StageThrows_PreservesWorkDoneByEarlierStages()
    {
        var pipeline = new PipelineBuilder()
            .Add(new RecordingStage("first", 10, ctx => ctx.Properties["marker"] = "set-by-first-stage"))
            .Add(new RecordingStage("failing", 20, throwException: new InvalidOperationException("boom")))
            .Build();

        var context = pipeline.Execute("hello world");

        Assert.Equal("set-by-first-stage", context.Properties["marker"]);
    }

    [Fact]
    public void Execute_StageReturnsNull_IsCapturedAsCriticalDiagnostic()
    {
        var pipeline = new PipelineBuilder().Add(new NullReturningStage()).Build();

        var context = pipeline.Execute("hello world");

        var diagnostic = Assert.Single(context.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Critical, diagnostic.Severity);
    }

    private sealed class NullReturningStage : PipelineStageBase
    {
        public override string Name => "null-returning";
        public override int Order => 10;
        public override AnalysisContext Execute(AnalysisContext context) => null!;
    }
}
