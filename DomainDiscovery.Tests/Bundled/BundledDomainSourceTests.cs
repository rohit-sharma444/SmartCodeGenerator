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
    public void TheRejectionIsTheLastWordOnTheSystemsName()
    {
        // `why` reports each name's LAST decision. The seed extractor kept "Library"; if its record
        // came after the rejection, the summary would list Library as kept — found by running it.
        var outcome = new BundledDomainSource(Catalog, _ => Seeds("Library", "Book"))
            .Discover("a library that lends books");

        Assert.Equal(DecisionOutcome.Rejected, outcome.Model.Decisions.Last(d => d.Name == "Library").Outcome);
        // And the trail reads in the order things happened: the user named Book, then the domain had it.
        var book = outcome.Model.Decisions.Where(d => d.Name == "Book").Select(d => d.Stage).ToList();
        Assert.Equal(new[] { DecisionStage.SeedExtraction, DecisionStage.BuiltInDomain }, book);
    }

    private static readonly BundledDomainCatalog Restaurant = new([BundledDomain.Parse("""
        { "name": "Restaurant", "terms": { "restaurant": 3, "librarian": 3 },
          "classes": [
            { "name": "MenuItem", "role": "Item", "properties": [ { "name": "Name", "type": "string" } ] },
            { "name": "DiningTable", "role": "Place", "properties": [ { "name": "Seats", "type": "int" } ] } ] }
        """)]);

    [Fact]
    public void ASeedThatIsAWordOfADomainClassIsCoveredByItNotAddedBesideIt()   // review I5
    {
        var outcome = new BundledDomainSource(Restaurant, _ => Seeds("Table", "Menu", "Tables"))
            .Discover("a restaurant with tables and a menu");

        Assert.Equal(new[] { "MenuItem", "DiningTable" }, outcome.Model.Entities.Select(e => e.Name).ToArray());
        var table = outcome.Model.Decisions.Last(d => d.Name == "Table");
        Assert.Equal(DecisionOutcome.Rejected, table.Outcome);
        Assert.Equal("covered by DiningTable in the built-in Restaurant domain", table.Reason);
    }

    [Fact]
    public void AWeightThreeTermThatIsNotTheSystemsNameIsKeptAsAClass()   // review I6
    {
        // "librarian" is weight 3 here but not in Self (which defaults to the name).
        var outcome = new BundledDomainSource(Restaurant, _ => Seeds("Librarian"))
            .Discover("a restaurant");

        Assert.Contains(outcome.Model.Entities, e => e.Name == "Librarian");
    }

    [Fact]
    public void ARecordASeedMadeFromAVerbIsLeftToTheDomain()
    {
        // "a clinic that books patients" seeded Booking beside the domain's Appointment: an empty
        // master nobody asked for, seen in a generated app. The domain defines the records.
        var seeds = Seeds("Fine");
        seeds.Entities.Add(new CandidateEntity { Name = "Booking", Provenance = Provenance.Seed });
        seeds.Record("Booking", DecisionStage.SeedExtraction, DecisionOutcome.Kept, DecisionReasons.YouSaidItAsAVerb);

        var outcome = new BundledDomainSource(Catalog, _ => seeds).Discover("a library that books fines");

        Assert.DoesNotContain(outcome.Model.Entities, e => e.Name == "Booking");
        Assert.Contains(outcome.Model.Entities, e => e.Name == "Fine");
        Assert.Equal(DecisionOutcome.Rejected, outcome.Model.Decisions.Last(d => d.Name == "Booking").Outcome);
    }

    // Measured live with Ollama unreachable: every one of these words became a class beside the
    // built-in domain's, and "System" made the generated app fail to compile (CS0118).
    [Theory]
    [InlineData("a library management system", "Management,System")]
    [InlineData("a point of sale application", "Application")]
    [InlineData("an ecommerce website", "Website")]
    [InlineData("a team project management platform", "Management,Platform")]
    [InlineData("a kanban board app", "App")]
    [InlineData("equipment rental where customers rent tools", "Where")]
    public void AWordForTheSoftwareItselfIsNotAClass(string description, string junk)
    {
        var outcome = BundledDomainSource.CreateDefault().Discover(description);

        foreach (var word in junk.Split(','))
        {
            Assert.DoesNotContain(outcome.Model.Entities, e => e.Name == word);
            Assert.Contains(word, outcome.Model.Rejected);
            Assert.Equal(DecisionOutcome.Rejected, outcome.Model.Decisions.Last(d => d.Name == word).Outcome);
        }
    }

    [Theory]
    [InlineData("a school enrols students in courses", "Enrol")]
    [InlineData("a hotel books rooms for guests", "Book")]
    public void ABareTransactionVerbIsNotAClass(string description, string verb)
    {
        var names = BundledDomainSource.CreateDefault().Discover(description).Model.Entities.Select(e => e.Name).ToList();

        Assert.DoesNotContain(verb, names);
    }

    [Fact]
    public void TheSoftwareWordIsDroppedEvenWhenNoDomainMatches()
    {
        // The seeds ARE the outcome when nothing matches, and the review opens on them if nothing
        // after this succeeds either.
        var outcome = new BundledDomainSource(Catalog, _ => Seeds("Knitting", "Portal", "System"))
            .Discover("a knitting portal system");

        Assert.False(outcome.Succeeded);
        Assert.Equal(new[] { "Knitting" }, outcome.Model.Entities.Select(e => e.Name).ToArray());
        Assert.Equal(DecisionReasons.NamesTheSoftware, outcome.Model.Decisions.Last(d => d.Name == "Portal").Reason);
    }

    [Fact]
    public void AGenuineClassBesideASoftwareWordIsKept()
    {
        var outcome = new BundledDomainSource(Catalog, _ => Seeds("Fine", "Application"))
            .Discover("a library application that tracks fines");

        Assert.Contains(outcome.Model.Entities, e => e.Name == "Fine");
        Assert.DoesNotContain(outcome.Model.Entities, e => e.Name == "Application");
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

    [Fact]
    public void ASeedSpelledTheOtherWayIsTheDomainsOwnClass()
    {
        var names = BundledDomainSource.CreateDefault()
            .Discover("a university that enrolls students and tracks enrollments")
            .Model.Entities.Select(e => e.Name).ToList();

        Assert.Contains("Enrolment", names);
        Assert.DoesNotContain("Enrollment", names);
    }

    // The word that names the system is not a class beside the domain's own.
    [Theory]
    [InlineData("a veterinary practice", "Veterinary")]
    [InlineData("a veterinary clinic", "Clinic")]
    [InlineData("a kanban board app", "Kanban,Board")]
    [InlineData("a point of sale application", "Point")]
    [InlineData("an ecommerce website", "Ecommerce")]
    [InlineData("a university that enrolls students and tracks enrollments", "University")]
    [InlineData("an academy that enrols students in courses", "Academy")]
    public void AWordNamingTheBuiltInSystemIsNotAClass(string description, string words)
    {
        var names = BundledDomainSource.CreateDefault().Discover(description).Model.Entities.Select(e => e.Name).ToList();

        foreach (var word in words.Split(','))
            Assert.DoesNotContain(word, names);
    }

    // Measured live: "customers" became an empty Customer beside the Veterinary domain's Owner,
    // and "treats" an empty Treat - each with screens, tiles and three seeded rows of nothing.
    [Fact]
    public void ASeedThatIsAnotherWordForADomainClassIsThatClass()
    {
        var model = BundledDomainSource.CreateDefault()
            .Discover("a veterinary clinic that treats pets owned by customers").Model;

        var names = model.Entities.Select(e => e.Name).ToList();
        Assert.Contains("Owner", names);
        Assert.Contains("Pet", names);
        Assert.DoesNotContain("Customer", names);
        Assert.DoesNotContain("Treat", names);

        var customer = model.Decisions.Last(d => d.Name == "Customer");
        Assert.Equal(DecisionOutcome.Rejected, customer.Outcome);
        Assert.Equal(DecisionReasons.AnotherWordFor("Owner", "Veterinary"), customer.Reason);
    }

    [Theory]
    [InlineData("an hr system for staff and departments", "Staff", "Employee")]
    [InlineData("a car rental company that rents vehicles to clients", "Vehicle", "Car")]
    [InlineData("a car rental company that rents vehicles to clients", "Client", "Customer")]
    [InlineData("a school where tutors teach pupils", "Pupil", "Student")]
    public void ACommonSynonymMapsOntoTheDomainsClass(string description, string seed, string domainClass)
    {
        var model = BundledDomainSource.CreateDefault().Discover(description).Model;

        Assert.DoesNotContain(model.Entities, e => e.Name == seed);
        Assert.Contains(model.Entities, e => e.Name == domainClass);
    }

    // Measured live: these real records were refused as "the software being built".
    [Theory]
    [InlineData("a recruitment agency that tracks job applications", "Application", "covered by JobApplication in the built-in Recruitment domain")]
    [InlineData("a warehouse that tracks stock movement", "Movement", "covered by StockMovement in the built-in Inventory domain")]
    [InlineData("a fleet company that tracks maintenance of vans", "Maintenance", "another word for ServiceRecord in the built-in Fleet domain")]
    public void AWordTheDomainKeepsUnderItsOwnNameSaysSo(string description, string seed, string reason)
    {
        var model = BundledDomainSource.CreateDefault().Discover(description).Model;

        var decision = model.Decisions.Last(d => d.Name == seed);
        Assert.Equal(DecisionOutcome.Rejected, decision.Outcome);
        Assert.Equal(reason, decision.Reason);
    }

    [Fact]
    public void AnActivityWordIsNotCalledTheSoftware()
    {
        var outcome = new BundledDomainSource(Catalog, _ => Seeds("Knitting", "Maintenance", "Portal"))
            .Discover("a knitting maintenance portal");

        Assert.Equal(DecisionReasons.NamesAnActivity, outcome.Model.Decisions.Last(d => d.Name == "Maintenance").Reason);
        Assert.Equal(DecisionReasons.NamesTheSoftware, outcome.Model.Decisions.Last(d => d.Name == "Portal").Reason);
    }

    [Fact]
    public void ASynonymForAClassTheDomainLacksIsRefusedWhenTheFileLoads()
    {
        var ex = Assert.Throws<FormatException>(() => BundledDomain.Parse("""
            { "name": "Vet", "terms": { "vet": 3 }, "synonyms": { "customer": "Ghost" },
              "classes": [ { "name": "Owner", "role": "Party", "properties": [] } ] }
            """));

        Assert.Contains("Ghost", ex.Message);
    }
}
