using DataIngestion.Normalization;
using DomainDiscovery.Model;
using Xunit;

namespace DataIngestion.Tests.Normalization;

public class NormalizationDecisionTests
{
    private static DataIngestion.Schema.TabularTable Orders()
        => RepeatingGroupTests.Table("Orders",
            ["Ref", "Item1", "Item2"], ["ORD-1", "Nut", "Bolt"], ["ORD-2", "Washer", null]);

    [Fact]
    public void AnAppliedSplitIsRecordedAgainstTheEntityItCreated()
    {
        var table = Orders();
        var findings = NormalizationAnalyzer.Analyze(table).Findings
            .Where(f => f.Kind == NormalizationFindingKind.RepeatingGroup).ToList();

        var model = DataModelBuilder.Build(NormalizationPlan.Apply([table], findings), findings);

        Assert.Contains(model.Decisions, d =>
            d.Name == "OrderItem"
            && d.Stage == DecisionStage.Normalization
            && d.Outcome == DecisionOutcome.Kept
            && d.Reason == DecisionReasons.ExtractedFromARepeatingGroup);
    }

    /// <summary>The evidence has to name the columns, or `why` says a class exists without saying
    /// what in the data put it there.</summary>
    [Fact]
    public void TheRecordsEvidenceNamesTheColumnsItCameFrom()
    {
        var table = Orders();
        var findings = NormalizationAnalyzer.Analyze(table).Findings
            .Where(f => f.Kind == NormalizationFindingKind.RepeatingGroup).ToList();

        var record = DataModelBuilder
            .Build(NormalizationPlan.Apply([table], findings), findings)
            .Decisions.First(d => d.Stage == DecisionStage.Normalization);

        Assert.Contains("Item1", record.Evidence);
        Assert.Contains("Item2", record.Evidence);

        // The source label verbatim, as the fixture spells it — the evidence has to carry the
        // file through, and echoing it in a different case would mean it was rebuilt, not carried.
        Assert.Contains("Orders.csv", record.Evidence);
    }

    [Theory]
    [InlineData(NormalizationFindingKind.NonAtomicValues, DecisionReasons.ExtractedFromANonAtomicColumn)]
    [InlineData(NormalizationFindingKind.FunctionalDependency, DecisionReasons.ExtractedFromAPartialDependency)]
    public void EachKindHasItsOwnReason(NormalizationFindingKind kind, string reason)
    {
        var table = RepeatingGroupTests.Table("Orders",
            ["Ref", "Tags", "CustomerName", "CustomerCity"],
            ["ORD-1", "rush,gift", "Ada", "London"],
            ["ORD-2", "rush,fragile", "Grace", "York"],
            ["ORD-3", "gift,fragile", "Ada", "London"],
            ["ORD-4", "rush,gift", "Grace", "York"],
            ["ORD-5", "gift,rush", "Ada", "London"]);

        var findings = NormalizationAnalyzer.Analyze(table).Findings
            .Where(f => f.Kind == kind).ToList();

        Assert.NotEmpty(findings);

        var model = DataModelBuilder.Build(NormalizationPlan.Apply([table], findings), findings);

        Assert.Contains(model.Decisions, d => d.Reason == reason);
    }

    /// <summary>A dropped derived column is a removal, not an extraction, so it is recorded as one
    /// -- and against the column's own name, which is what a user would ask `why` about.</summary>
    [Fact]
    public void ADroppedDerivedColumnIsRecordedAsRemoved()
    {
        var table = RepeatingGroupTests.Table("Orders",
            ["Qty", "UnitPrice", "Total"],
            ["2", "5.00", "10.00"], ["3", "2.50", "7.50"], ["4", "1.25", "5.00"],
            ["5", "2.00", "10.00"], ["6", "0.50", "3.00"]);

        var findings = NormalizationAnalyzer.Analyze(table).Findings
            .Where(f => f.Kind == NormalizationFindingKind.DerivedColumn).ToList();

        var model = DataModelBuilder.Build(NormalizationPlan.Apply([table], findings), findings);

        Assert.Contains(model.Decisions, d =>
            d.Name == "Total"
            && d.Stage == DecisionStage.Normalization
            && d.Outcome == DecisionOutcome.Rejected
            && d.Reason == DecisionReasons.DroppedAsADerivedColumn);
    }

    /// <summary>Applying nothing records nothing about normalization -- the `k` path leaves no trail
    /// claiming a change was made.</summary>
    [Fact]
    public void KeepingTheDataAsItIsRecordsNoNormalizationDecision()
    {
        var table = Orders();

        var model = DataModelBuilder.Build(NormalizationPlan.Apply([table], []), []);

        Assert.DoesNotContain(model.Decisions, d => d.Stage == DecisionStage.Normalization);
    }
}
