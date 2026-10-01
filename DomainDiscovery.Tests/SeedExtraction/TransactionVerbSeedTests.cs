using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;
using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace DomainDiscovery.Tests.SeedExtraction;

/// <summary>
/// A description names its transaction with a verb - a clinic that BOOKS patients - and the
/// record that verb makes is the class the app most needs. Measured on the Wikipedia path before
/// this existed: every one of the three classes it missed across app-check's five description
/// prompts was such a record, and three of the verbs became classes themselves (Book, Rent, Enrol).
/// </summary>
public class TransactionVerbSeedTests
{
    private static readonly ISemanticAnalyzer Analyzer = EngineBuilder.BuildDefault().Analyzer;

    private static IReadOnlyList<string> Seeds(string description)
        => [.. new SeedEntityExtractor().Extract(Analyzer.Analyze(description), description).Entities.Select(e => e.Name)];

    [Theory]
    [InlineData("a clinic that books patients with doctors", "Booking")]
    [InlineData("a library that lends books to members", "Loan")]
    [InlineData("a school that enrols students in courses taught by teachers", "Enrolment")]
    [InlineData("a hotel that reserves rooms for guests", "Reservation")]
    [InlineData("a shop that orders stock from suppliers", "Order")]
    public void The_record_a_transaction_verb_makes_is_a_seed(string description, string record)
    {
        Assert.Contains(record, Seeds(description));
    }

    [Theory]
    [InlineData("a clinic that books patients with doctors", "Book")]
    [InlineData("a car rental company that rents vehicles to customers", "Rent")]
    [InlineData("a school that enrols students in courses taught by teachers", "Enrol")]
    [InlineData("a hotel that reserves rooms for guests", "Reserve")]
    public void The_verb_itself_is_not_a_seed(string description, string verb)
    {
        Assert.DoesNotContain(verb, Seeds(description));
    }

    [Fact]
    public void A_record_already_named_is_not_added_twice()
    {
        var seeds = Seeds("a car rental company that rents vehicles to customers");

        Assert.Single(seeds, s => s == "Rental");
    }

    [Fact]
    public void The_same_word_used_as_a_noun_is_still_a_class()
    {
        // "books" here is what the library lends, not what it does.
        Assert.Contains("Book", Seeds("a library that lends books to members"));
    }

    [Fact]
    public void That_before_a_word_the_table_does_not_know_is_left_alone()
    {
        // "that" is as often a determiner as a relative pronoun; only a known verb decides it.
        var result = new SemanticResult();
        result.Entities.Add(new Entity { Text = "that data", Type = EntityType.Object, Confidence = 0.7 });

        var model = new SeedEntityExtractor().Extract(result);

        Assert.Equal(["Data"], model.Entities.Select(e => e.Name));
    }

    [Fact]
    public void The_trail_says_the_record_came_from_a_verb()
    {
        var result = new SemanticResult();
        result.Entities.Add(new Entity { Text = "lends", Type = EntityType.Action, Confidence = 0.8 });

        var model = new SeedEntityExtractor().Extract(result);

        var entity = Assert.Single(model.Entities);
        Assert.Equal("Loan", entity.Name);
        Assert.Equal(Provenance.Seed, entity.Provenance);

        var record = Assert.Single(model.Decisions, r => r.Name == "Loan");
        Assert.Equal(DecisionOutcome.Kept, record.Outcome);
        Assert.Equal(DecisionReasons.YouSaidItAsAVerb, record.Reason);
        Assert.Contains("lends", record.Evidence);
    }

    [Theory]
    [InlineData("books", "Booking")]
    [InlineData("booked", "Booking")]
    [InlineData("enrolled", "Enrolment")]
    [InlineData("enrolls", "Enrolment")]
    [InlineData("lent", "Loan")]
    [InlineData("borrows", "Loan")]
    [InlineData("paid", "Payment")]
    [InlineData("ships", "Shipment")]
    [InlineData("hires", "Rental")]
    public void Inflected_verbs_find_their_record(string verb, string record)
    {
        Assert.Equal(record, TransactionVerbs.RecordFor(verb));
    }

    [Theory]
    [InlineData("bookshelf")]
    [InlineData("sells")]
    [InlineData("data")]
    [InlineData("booking")]
    [InlineData("")]
    public void Words_that_are_not_transaction_verbs_have_no_record(string word)
    {
        Assert.Null(TransactionVerbs.RecordFor(word));
    }

    // Without "that", the engine types the verb as a thing ("a school ENROLS students" gives the
    // entities "a school", "enrols", "students"), and it became a class named after the verb.
    // Measured live on the built-in-domain path: Enrol, Book, Ship and Pay all reached the review.
    [Theory]
    [InlineData("a school enrols students in courses", "Enrolment", "Enrol")]
    [InlineData("a hotel books rooms for guests", "Booking", "Book")]
    [InlineData("a shop ships parcels to customers", "Shipment", "Ship")]
    [InlineData("a shop pays suppliers", "Payment", "Pay")]
    public void A_bare_transaction_verb_after_the_subject_makes_its_record(string description, string record, string verb)
    {
        var seeds = Seeds(description);

        Assert.Contains(record, seeds);
        Assert.DoesNotContain(verb, seeds);
    }

    [Fact]
    public void The_object_of_a_bare_verb_is_still_a_class()
    {
        var seeds = Seeds("a hotel books rooms for guests");

        Assert.Contains("Room", seeds);
        Assert.Contains("Guest", seeds);
    }

    // Only a word standing straight after the subject noun is read as its verb: "of books" and
    // "with orders" are what the library and the store HAVE.
    [Theory]
    [InlineData("a library of books", "Book,Library")]
    [InlineData("a library of books and dvds", "Book,Dvd,Library")]
    [InlineData("a store with orders and payments", "Order,Payment,Store")]
    public void A_noun_that_merely_looks_like_a_verb_stays_a_noun(string description, string expected)
    {
        Assert.Equal(expected.Split(','), Seeds(description).Order());
    }

    // "hire" is a rental only when what is hired is a thing: a van hire company that hires vans
    // keeps Rentals, a company that hires staff keeps no such record.
    [Theory]
    [InlineData("a company that hires staff and pays salaries")]
    [InlineData("an agency hires staff")]
    [InlineData("a hospital that hires nurses")]
    public void Hiring_people_is_not_a_rental(string description)
    {
        var seeds = Seeds(description);

        Assert.DoesNotContain("Rental", seeds);
        Assert.DoesNotContain("Hire", seeds);
    }

    [Theory]
    [InlineData("a company that hires vans to customers")]
    [InlineData("a shop hires bikes to tourists")]
    public void Hiring_out_a_thing_is_still_a_rental(string description)
    {
        Assert.Contains("Rental", Seeds(description));
    }

    [Fact]
    public void A_second_verb_joined_by_and_makes_its_record_too()
    {
        var seeds = Seeds("a company that hires staff and pays salaries");

        Assert.Contains("Payment", seeds);
        Assert.DoesNotContain("Pay", seeds);
        Assert.Contains("Staff", seeds);
        Assert.Contains("Salary", seeds);
    }

    // "enrolls" makes the UK-spelled record, the noun "enrollments" the US one; both were kept.
    [Fact]
    public void A_record_and_its_other_spelling_are_one_class()
    {
        var seeds = Seeds("a university that enrolls students and tracks enrollments");

        Assert.Single(seeds, s => s is "Enrolment" or "Enrollment");
    }
}
