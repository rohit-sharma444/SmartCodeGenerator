using System.Linq;
using CodeGenerator.App;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class DecisionReportTests
{
    private static CandidateDomainModel ModelWithATrail()
    {
        var model = new CandidateDomainModel();
        model.Record("lending", DecisionStage.SeedExtraction, DecisionOutcome.Kept,
            DecisionReasons.YouNamedIt, "confidence 0.82");
        model.Record("Lending", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction, "head noun: practice");
        model.Record("Cash register", DecisionStage.Enrichment, DecisionOutcome.Suggested,
            DecisionReasons.LeadDoesNotMentionIt, "lead of \"Point of sale\"");
        return model;
    }

    [TestMethod]
    public void Canonical_IgnoresCaseSpacingAndPunctuation()
    {
        Assert.AreEqual("cashregister", DecisionReport.Canonical("Cash register"));
        Assert.AreEqual("cashregister", DecisionReport.Canonical("CashRegister"));
        Assert.AreEqual("cashregister", DecisionReport.Canonical("  cash-register "));
    }

    [TestMethod]
    public void ForName_MatchesAcrossSpellings_AndShowsTheWholeTrailInStageOrder()
    {
        var lines = DecisionReport.ForName(ModelWithATrail(), "LENDING");
        var text = string.Join("\n", lines);

        StringAssert.Contains(text, DecisionReasons.YouNamedIt);
        StringAssert.Contains(text, DecisionReasons.DefinitionResolvesToAnAbstraction);
        StringAssert.Contains(text, "head noun: practice");

        // Seed extraction happened first and must be shown first.
        Assert.IsLessThan(
            text.IndexOf(DecisionReasons.DefinitionResolvesToAnAbstraction),
            text.IndexOf(DecisionReasons.YouNamedIt),
            "the trail must read in stage order");
    }

    [TestMethod]
    public void ForName_FindsARecordWrittenUnderTheArticleSpelling()
    {
        var lines = DecisionReport.ForName(ModelWithATrail(), "CashRegister");

        StringAssert.Contains(string.Join("\n", lines), DecisionReasons.LeadDoesNotMentionIt);
    }

    [TestMethod]
    public void ForName_OffersTheActionThatTakesARejectedOrSuggestedNameBack()
    {
        StringAssert.Contains(
            string.Join("\n", DecisionReport.ForName(ModelWithATrail(), "Lending")),
            "add Lending");

        StringAssert.Contains(
            string.Join("\n", DecisionReport.ForName(ModelWithATrail(), "Cash register")),
            "add CashRegister");
    }

    [TestMethod]
    public void ForName_SaysSoWhenNothingWasRecorded()
    {
        var lines = DecisionReport.ForName(new CandidateDomainModel(), "Nonesuch");

        StringAssert.Contains(string.Join("\n", lines), "No decision recorded for 'Nonesuch'");
    }

    [TestMethod]
    public void ForName_ExplainsAnEntityTheUserAddedByHand()
    {
        // `add Widget` in the review loop leaves no decision - discovery never saw it.
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Widget", Provenance = Provenance.Seed });

        StringAssert.Contains(
            string.Join("\n", DecisionReport.ForName(model, "Widget")),
            "added in the review loop");
    }

    [TestMethod]
    public void Summary_GroupsRejectionsByReason()
    {
        var model = new CandidateDomainModel();
        model.Record("Lending", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction);
        model.Record("Administration", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction);

        var line = DecisionReport.Summary(model).Single(l => l.StartsWith("Not classes:"));

        // The reason is composed from the constant, never copied — a Global Constraint. A test
        // holding its own copy of the phrase goes on passing after the phrase is changed.
        Assert.AreEqual(
            $"Not classes: Lending, Administration — {DecisionReasons.DefinitionResolvesToAnAbstraction}",
            line);
    }

    [TestMethod]
    public void Summary_CapsALongGroup_AndPointsAtTheWhyVerb()
    {
        var model = new CandidateDomainModel();
        for (var i = 1; i <= 20; i++)
        {
            model.Record($"Name{i}", DecisionStage.Shaping, DecisionOutcome.Rejected,
                DecisionReasons.DefinitionResolvesToAnAbstraction);
        }

        var line = DecisionReport.Summary(model).Single(l => l.StartsWith("Not classes:"));

        StringAssert.Contains(line, "Name8");
        Assert.DoesNotContain("Name9", line, "the group must be capped at 8 names");
        StringAssert.Contains(line, "(+12 more — try `why`)");
    }

    [TestMethod]
    public void Summary_IsEmpty_WhenThereIsNothingToReport()
    {
        var model = new CandidateDomainModel();
        model.Record("Book", DecisionStage.SeedExtraction, DecisionOutcome.Kept,
            DecisionReasons.YouNamedIt);

        // Kept candidates are the model itself - the summary explains only what is missing.
        Assert.IsEmpty(DecisionReport.Summary(model));
    }

    [TestMethod]
    public void All_GroupsEveryDecisionByOutcome()
    {
        var text = string.Join("\n", DecisionReport.All(ModelWithATrail()));

        StringAssert.Contains(text, "Kept (1):");
        StringAssert.Contains(text, "Rejected (1):");
        StringAssert.Contains(text, "Suggested (1):");
    }

    [TestMethod]
    public void All_SaysSoWhenTheTrailIsEmpty()
        => StringAssert.Contains(
            string.Join("\n", DecisionReport.All(new CandidateDomainModel())),
            "No decisions recorded.");
}
