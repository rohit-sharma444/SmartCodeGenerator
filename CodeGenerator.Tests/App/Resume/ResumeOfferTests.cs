using CodeGenerator.App.Resume;

namespace CodeGenerator.Tests.App.Resume;

[TestClass]
public class ResumeOfferTests
{
    [TestMethod]
    public void The_three_answers_and_their_letters()
    {
        Assert.AreEqual(ResumeChoice.Resume, ResumeOffer.Parse("r"));
        Assert.AreEqual(ResumeChoice.Resume, ResumeOffer.Parse("resume"));
        Assert.AreEqual(ResumeChoice.Fresh, ResumeOffer.Parse("f"));
        Assert.AreEqual(ResumeChoice.Fresh, ResumeOffer.Parse("fresh"));
        Assert.AreEqual(ResumeChoice.Discard, ResumeOffer.Parse("d"));
        Assert.AreEqual(ResumeChoice.Discard, ResumeOffer.Parse("discard"));
    }

    [TestMethod]
    public void An_answer_is_read_however_it_is_cased_or_spaced()
        => Assert.AreEqual(ResumeChoice.Resume, ResumeOffer.Parse("  RESUME  "));

    /// <summary>
    /// Blank and end-of-input start fresh. Nothing is deleted either way, so the hazard is not
    /// lost work — it is someone pressing Enter without reading and then generating an app from a
    /// stale model. Fresh is also what the tool did before any of this existed.
    /// </summary>
    [TestMethod]
    public void Enter_starts_fresh_and_deletes_nothing()
    {
        Assert.AreEqual(ResumeChoice.Fresh, ResumeOffer.Parse(string.Empty));
        Assert.AreEqual(ResumeChoice.Fresh, ResumeOffer.Parse(null));
        Assert.AreEqual(ResumeChoice.Fresh, ResumeOffer.Parse("   "));
    }

    /// <summary>Unrecognised returns null so the caller re-asks. A default here would make a typo
    /// choose for the user — the reasoning OverwritePrompt already sets out.</summary>
    [TestMethod]
    public void Something_else_is_not_an_answer()
    {
        Assert.IsNull(ResumeOffer.Parse("yes"));
        Assert.IsNull(ResumeOffer.Parse("x"));
    }

    [TestMethod]
    public void The_offer_says_what_is_waiting()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "a shop",
            AppType = "wpf",
            Stage = "Generate",
            SavedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
            Entities = [new BuildSnapshot.Entity { Name = "Book" }],
        };

        var text = string.Join("\n", ResumeOffer.Lines(snapshot, DateTimeOffset.UtcNow));

        StringAssert.Contains(text, "a shop");
        StringAssert.Contains(text, "Generate");
        StringAssert.Contains(text, "1 class");
        StringAssert.Contains(text, "20 minutes ago");
    }

    /// <summary>A build abandoned before discovery ran has no classes, and must not claim "0
    /// classes reviewed" as though it had been through review.</summary>
    [TestMethod]
    public void An_offer_with_no_model_yet_does_not_mention_classes()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "a shop", AppType = "wpf", Stage = "Data",
            SavedAt = DateTimeOffset.UtcNow,
        };

        var text = string.Join("\n", ResumeOffer.Lines(snapshot, DateTimeOffset.UtcNow));

        Assert.DoesNotContain("class", text);
        StringAssert.Contains(text, "a shop");
    }

    [TestMethod]
    public void A_build_with_no_description_is_still_describable()
    {
        var snapshot = new BuildSnapshot
        {
            Description = string.Empty, AppType = "wpf", Stage = "Describe",
            SavedAt = DateTimeOffset.UtcNow,
        };

        var text = string.Join("\n", ResumeOffer.Lines(snapshot, DateTimeOffset.UtcNow));

        Assert.IsNotEmpty(text);
        StringAssert.Contains(text, "Describe");
    }
}
