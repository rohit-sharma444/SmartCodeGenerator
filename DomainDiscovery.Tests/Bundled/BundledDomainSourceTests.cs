using DomainDiscovery.Bundled;
using DomainDiscovery.Model;
using Xunit;

namespace DomainDiscovery.Tests.Bundled;

public class BundledDomainSourceTests
{
    private static readonly BundledDomainCatalog Catalog = new([BundledDomain.Parse("""
        { "name": "Library", "terms": { "library": 3, "book": 1, "lend": 1, "member": 1 },
          "classes": [
            { "name": "Book", "role": "Item", "properties": [ { "name": "Title", "type": "string" } ] },
            { "name": "Member", "role": "Party", "properties": [ { "name": "Name", "type": "string" } ] },
            { "name": "Loan", "role": "Transaction", "properties": [
                { "name": "BookId", "type": "int" }, { "name": "MemberId", "type": "int" } ] } ] }
        """)]);

    private static CandidateDomainModel Seeds(params string[] names)
    {
        var model = new CandidateDomainModel();
        foreach (var name in names)
        {
            model.Entities.Add(new CandidateEntity { Name = name, Provenance = Provenance.Seed });
            model.Record(name, DecisionStage.SeedExtraction, DecisionOutcome.Kept, DecisionReasons.YouNamedIt);
        }
        return model;
    }

    [Fact]
    public void AMatchGivesTheDomainsClassesAndSaysWhy()
    {
        var outcome = new BundledDomainSource(Catalog, _ => Seeds())
            .Discover("a library that lends books to members");

        Assert.True(outcome.Succeeded);
        Assert.Equal("the built-in Library domain", outcome.SourceLabel);
        Assert.Equal("Matched: library, lends, books, members.", outcome.Message);
        Assert.Equal(new[] { "Book", "Member", "Loan" }, outcome.Model.Entities.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void ClassesTheUserNamedAreAddedAndDuplicatesAreNot()
    {
        var outcome = new BundledDomainSource(Catalog, _ => Seeds("Book", "Fine"))
            .Discover("a library that lends books and tracks fines");

        var names = outcome.Model.Entities.Select(e => e.Name).ToList();
        Assert.Single(names, n => n == "Book");
        Assert.Contains("Fine", names);
        Assert.Equal(Provenance.Seed, outcome.Model.Entities.Single(e => e.Name == "Fine").Provenance);
        Assert.Contains(outcome.Model.Decisions, d => d.Name == "Fine" && d.Stage == DecisionStage.SeedExtraction);
    }

    [Fact]
    public void ASeedNamingTheSystemItselfIsDroppedWithAReason()   // Review Focus 1
    {
        var outcome = new BundledDomainSource(Catalog, _ => Seeds("Library", "Libraries"))
            .Discover("a library that lends books");

        Assert.DoesNotContain(outcome.Model.Entities, e => e.Name.StartsWith("Librar"));
        Assert.Contains(outcome.Model.Decisions, d => d.Name == "Library"
            && d.Outcome == DecisionOutcome.Rejected && d.Reason == DecisionReasons.NamesTheWholeSystem);
    }

    [Fact]
    public void NoMatchReturnsTheSeedsUnsuccessfullySoTheChainMovesOn()
    {
        var outcome = new BundledDomainSource(Catalog, _ => Seeds("Pattern"))
            .Discover("a knitting pattern tracker");

        Assert.False(outcome.Succeeded);
        Assert.Equal(BundledDomainSource.NoMatchLabel, outcome.SourceLabel);
        Assert.Equal("No built-in domain matches that description.", outcome.Message);
        Assert.Equal("Pattern", Assert.Single(outcome.Model.Entities).Name);
    }

    [Fact]
    public void TheDefaultSourceMatchesTheRealLibraryDomain()
    {
        var outcome = BundledDomainSource.CreateDefault().Discover("a library that lends books to members");

        Assert.True(outcome.Succeeded);
        Assert.Contains(outcome.Model.Entities, e => e.Name == "Book");
        Assert.DoesNotContain(outcome.Model.Entities, e => e.Name == "Library");
    }
}
