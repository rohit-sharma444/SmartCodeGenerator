using System;
using System.Linq;
using CodeGenerator.App;
using DomainDiscovery.Model;
using DomainDiscovery.Review;

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

        // A compound the pipeline can really file: wikilink names arrive through
        // ToCompoundEntityName and seed/harvested ones through ToEntityName, so a Rejected or
        // Suggested record always holds a separator-free token. This is the spelling that
        // reaches the `add` suggestion in production, and the one that used to be flattened.
        var model = new CandidateDomainModel();
        model.Record("CashRegister", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction);

        StringAssert.Contains(
            string.Join("\n", DecisionReport.ForName(model, "CashRegister")),
            "add CashRegister");
    }

    [TestMethod]
    public void ForName_ConvertsASeparatorBearingNameIntoOneTheLoopAccepts()
    {
        // Defensive: only a Skipped record can hold raw typed text with a space in it, and those
        // get no `add` line. Kept anyway so the conversion is pinned if that ever changes.
        StringAssert.Contains(
            string.Join("\n", DecisionReport.ForName(ModelWithATrail(), "Cash register")),
            "add CashRegister");
    }

    [TestMethod]
    public void ForName_DoesNotOfferToAddSomethingThatIsAlreadyAClass()
    {
        // The trail is history and stays as it was, but the closing line is advice about NOW.
        // Once the user has taken the name back, `add` would fail as a duplicate.
        var model = new CandidateDomainModel();
        model.Record("Collection", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction);
        ModelEditor.AddEntity(model, "Collection");

        var text = string.Join("\n", DecisionReport.ForName(model, "Collection"));

        StringAssert.Contains(text, DecisionReasons.DefinitionResolvesToAnAbstraction);
        Assert.DoesNotContain("add Collection", text, "it is a class already");
        StringAssert.Contains(text, "in the model now");
    }

    [TestMethod]
    public void ForName_StillOffersToAddSomethingTheModelDoesNotHave()
    {
        var model = new CandidateDomainModel();
        model.Record("Collection", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction);

        StringAssert.Contains(
            string.Join("\n", DecisionReport.ForName(model, "Collection")),
            "add Collection");
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
    public void Summary_LabelsSuggestionsAndSkipsWithTheirOwnPrefixes()
    {
        var model = new CandidateDomainModel();
        model.Record("Archive", DecisionStage.Enrichment, DecisionOutcome.Suggested,
            DecisionReasons.LeadDoesNotMentionIt);
        model.Record("tracks", DecisionStage.SeedExtraction, DecisionOutcome.Skipped,
            DecisionReasons.NotRecognizedAsAThing);

        var summary = DecisionReport.Summary(model);

        // Each outcome gets its own prefix. Asserting the whole line pins the prefix, the
        // separator and the reason at once — swapping two prefixes has to fail here.
        Assert.AreEqual(
            $"Also found (add <name> to include): Archive — {DecisionReasons.LeadDoesNotMentionIt}",
            summary.Single(l => l.StartsWith("Also found")));

        Assert.AreEqual(
            $"Ignored: tracks — {DecisionReasons.NotRecognizedAsAThing}",
            summary.Single(l => l.StartsWith("Ignored:")));
    }

    [TestMethod]
    public void Summary_StopsAdvertisingASuggestionTheUserHasAlreadyAdded()
    {
        // `add` promotes a suggestion into the model but writes no decision — the log is
        // append-only. Left unfiltered the summary goes on telling the user to add a name that
        // is already a class, and the advice now fails as a duplicate.
        var model = new CandidateDomainModel();
        model.Record("LibraryMakerspace", DecisionStage.Enrichment, DecisionOutcome.Suggested,
            DecisionReasons.LeadDoesNotMentionIt);
        model.Entities.Add(new CandidateEntity
        {
            Name = "LibraryMakerspace",
            Provenance = Provenance.Wiki
        });

        Assert.IsFalse(
            DecisionReport.Summary(model).Any(l => l.StartsWith("Also found")),
            "a suggestion that is now a class must not still be advertised");
    }

    [TestMethod]
    public void Summary_DropsARejectionTheUserOverrode()
    {
        // Same append-only problem from the other side: `add Fairtrade` after a rejection must
        // stop the model calling it "not a class". The spellings differ, so this only works if
        // the live model is matched canonically.
        var model = new CandidateDomainModel();
        model.Record("FairTrade", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction);
        model.Entities.Add(new CandidateEntity { Name = "Fairtrade", Provenance = Provenance.Seed });

        Assert.IsEmpty(DecisionReport.Summary(model));
    }

    [TestMethod]
    public void Summary_ReportsOnlyANamesLastDecision()
    {
        // Enrichment demotes a name the seed already claims, so it collects a Suggested record
        // and then a Skipped one. Reporting both puts one name under two contradictory headings
        // in the same redraw.
        var model = new CandidateDomainModel();
        model.Record("Book", DecisionStage.Enrichment, DecisionOutcome.Suggested,
            DecisionReasons.LeadDoesNotMentionIt);
        model.Record("Book", DecisionStage.Enrichment, DecisionOutcome.Skipped,
            DecisionReasons.AlreadyAClassFromYourDescription);

        var summary = DecisionReport.Summary(model);

        Assert.IsFalse(
            summary.Any(l => l.StartsWith("Also found")),
            "the superseded Suggested record must not be reported");
        StringAssert.Contains(
            summary.Single(l => l.StartsWith("Ignored:")),
            DecisionReasons.AlreadyAClassFromYourDescription);
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
    public void TheAddCommandItPrints_ProducesExactlyTheNameItShowed()
    {
        // The invariant this whole fix exists to establish: a name `why` shows, and the `add`
        // command it prints for that name, must agree — with each other and with the model that
        // results. Asserting either half alone cannot catch them drifting apart.
        var model = new CandidateDomainModel();
        model.Record("FairTrade", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction);

        var lines = DecisionReport.ForName(model, "FairTrade");

        var shown = lines[0];
        var printed = lines.Single(l => l.Contains("add "));
        var typed = printed[(printed.IndexOf("add ", StringComparison.Ordinal) + 4)..].Trim();

        Assert.IsTrue(ModelEditor.AddEntity(model, typed, out var created));
        Assert.AreEqual(shown, created, "running the printed command must produce the shown name");
    }

    [TestMethod]
    public void All_GroupsEveryDecisionByOutcome()
    {
        // The fixture's "lending" is kept at seed extraction and rejected at shaping, so it is
        // reported once, as Rejected. This test previously asserted it appeared under BOTH,
        // which is the defect that reading only each name's final outcome fixes.
        var text = string.Join("\n", DecisionReport.All(ModelWithATrail()));

        StringAssert.Contains(text, "Rejected (1): Lending");
        StringAssert.Contains(text, "Suggested (1): Cash register");
        Assert.DoesNotContain("Kept", text, "seed extraction's verdict no longer stands");
    }

    [TestMethod]
    public void All_CountsNamesOnce_AndFilesEachUnderItsFinalOutcome()
    {
        // The header counted raw records while the groups counted distinct names, so the two
        // never reconciled; and a name kept early then rejected later was listed under both.
        var model = new CandidateDomainModel();
        model.Record("Lending", DecisionStage.SeedExtraction, DecisionOutcome.Kept,
            DecisionReasons.YouNamedIt);
        model.Record("Lending", DecisionStage.Shaping, DecisionOutcome.Rejected,
            DecisionReasons.DefinitionResolvesToAnAbstraction);

        var text = string.Join("\n", DecisionReport.All(model));

        StringAssert.Contains(text, "Decisions (1):");
        StringAssert.Contains(text, "Rejected (1): Lending");
        Assert.DoesNotContain("Kept", text, "the superseded Kept record must not be reported");
    }

    [TestMethod]
    public void ForName_ExplainsARenamedEntity_RatherThanCallingItHandAdded()
    {
        var model = new CandidateDomainModel();
        model.Record("Member", DecisionStage.SeedExtraction, DecisionOutcome.Kept,
            DecisionReasons.YouNamedIt);
        model.Entities.Add(new CandidateEntity { Name = "Member" });
        ModelEditor.RenameEntity(model, "Member", "Borrower");

        var text = string.Join("\n", DecisionReport.ForName(model, "Borrower"));

        StringAssert.Contains(text, DecisionReasons.YouRenamedIt);
        StringAssert.Contains(text, "Member");
        Assert.DoesNotContain("added in the review loop", text,
            "a renamed entity was discovered, not hand-added");
    }

    [TestMethod]
    public void Summary_ShowsEveryNameAtTheExactCap()
    {
        // The boundary the cap turns on: 8 shows all 8 with no tail, 9 would cut to 8.
        var model = new CandidateDomainModel();
        for (var i = 1; i <= DecisionReport.MaxNamesPerGroup; i++)
        {
            model.Record($"Name{i}", DecisionStage.Shaping, DecisionOutcome.Rejected,
                DecisionReasons.DefinitionResolvesToAnAbstraction);
        }

        var line = DecisionReport.Summary(model).Single(l => l.StartsWith("Not classes:"));

        StringAssert.Contains(line, $"Name{DecisionReport.MaxNamesPerGroup}");
        Assert.DoesNotContain("more — try", line, "nothing is hidden at exactly the cap");
    }

    [TestMethod]
    public void All_SaysSoWhenTheTrailIsEmpty()
        => StringAssert.Contains(
            string.Join("\n", DecisionReport.All(new CandidateDomainModel())),
            "No decisions recorded.");
}
