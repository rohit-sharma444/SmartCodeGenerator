using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Model;

public class DecisionRecordTests
{
    [Fact]
    public void Record_AppendsOneRecord_CarryingEveryField()
    {
        var model = new CandidateDomainModel();

        model.Record("Lending", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction, "head noun: practice");

        var record = Assert.Single(model.Decisions);
        Assert.Equal("Lending", record.Name);
        Assert.Equal(DecisionStage.Shaping, record.Stage);
        Assert.Equal(DecisionOutcome.Rejected, record.Outcome);
        Assert.Equal(DecisionReasons.DefinitionResolvesToAnAbstraction, record.Reason);
        Assert.Equal("head noun: practice", record.Evidence);
    }

    [Fact]
    public void Record_LeavesEvidenceNull_WhenNoneIsGiven()
    {
        var model = new CandidateDomainModel();

        model.Record("Book", DecisionStage.SeedExtraction, DecisionOutcome.Kept,
            DecisionReasons.YouNamedIt);

        Assert.Null(Assert.Single(model.Decisions).Evidence);
    }

    [Fact]
    public void Record_IsAppendOnly_AndTouchesNoOtherCollection()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Book", Provenance = Provenance.Seed });

        model.Record("Lending", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction);
        model.Record("Fine", DecisionStage.Shaping, DecisionOutcome.Suggested,
            DecisionReasons.NoDefinitionWeRecognize);

        Assert.Equal(2, model.Decisions.Count);
        Assert.Equal(new[] { "Lending", "Fine" }, model.Decisions.Select(d => d.Name));

        // The log is a parallel channel. It must never write to the collections the
        // scaffolder actually reads.
        Assert.Single(model.Entities);
        Assert.Empty(model.Suggestions);
        Assert.Empty(model.Rejected);
    }
}
