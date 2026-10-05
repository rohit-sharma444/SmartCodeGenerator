using DomainDiscovery.Bundled;
using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;
using NaturalLanguageEngine.Abstractions.Semantics;
using Xunit;

namespace DomainDiscovery.Tests.SeedExtraction;

/// <summary>
/// The engine types a verb it also knows as a noun as a thing: "that RECORDS orders" gives the
/// entities "that records" and "orders". Before this, the verb became a class (Record, Log,
/// Schedule, Treat, Sign) and the noun after it was taken for a verb - "orders" made an Order
/// "you said it as a verb", which the built-in domains then threw away as one of their own
/// records. The user's own class vanished.
/// </summary>
public class VerbPhraseSeedTests
{
    private static readonly ISemanticAnalyzer Analyzer = EngineBuilder.BuildDefault().Analyzer;

    private static CandidateDomainModel Model(string description)
        => new SeedEntityExtractor().Extract(Analyzer.Analyze(description), description);

    private static IReadOnlyList<string> Seeds(string description)
        => [.. Model(description).Entities.Select(e => e.Name)];

    [Theory]
    [InlineData("a warehouse that records orders for customers", "Record")]
    [InlineData("a warehouse that logs orders for customers", "Log")]
    [InlineData("a warehouse that schedules orders for customers", "Schedule")]
    [InlineData("a warehouse records orders for customers", "Record")]
    public void The_noun_after_a_verb_is_a_class_the_user_named(string description, string verb)
    {
        var model = Model(description);

        Assert.DoesNotContain(verb, model.Entities.Select(e => e.Name));
        Assert.Contains("Order", model.Entities.Select(e => e.Name));
        Assert.Equal(DecisionReasons.YouNamedIt, model.Decisions.Last(d => d.Name == "Order").Reason);
    }

    [Fact]
    public void The_object_of_a_transaction_verb_is_named_not_made()
    {
        var model = Model("a warehouse that ships orders to customers");

        Assert.Equal(DecisionReasons.YouSaidItAsAVerb, model.Decisions.Last(d => d.Name == "Shipment").Reason);
        Assert.Equal(DecisionReasons.YouNamedIt, model.Decisions.Last(d => d.Name == "Order").Reason);
    }

    [Theory]
    [InlineData("a warehouse that records stock", "Stock,Warehouse")]
    [InlineData("a veterinary clinic that treats pets owned by customers", "Clinic,Customer,Pet,Veterinary")]
    [InlineData("a gym that signs up members for classes", "Class,Gym,Member")]
    [InlineData("a clinic that sees patients", "Clinic,Patient")]
    [InlineData("a library of books keeps loans", "Book,Library,Loan")]
    [InlineData("a gym that signs up members for classes and employs staff", "Class,Gym,Member,Staff")]
    [InlineData("a practice that sees patients and bills treatments", "Patient,Practice,Treatment")]
    public void A_verb_the_engine_typed_as_a_thing_is_not_a_class(string description, string expected)
    {
        Assert.Equal(expected.Split(','), Seeds(description).Order());
    }

    [Fact]
    public void Why_says_the_word_was_a_verb()
    {
        var decision = Model("a warehouse that records stock").Decisions.Last(d => d.Name == "Record");

        Assert.Equal(DecisionOutcome.Skipped, decision.Outcome);
        Assert.Equal(DecisionReasons.ItIsAVerbHere, decision.Reason);
        Assert.Contains("that records", decision.Evidence);
    }

    // A compound noun is not a subject and its verb: the word after "records" is the software.
    [Fact]
    public void A_records_system_keeps_its_records()
    {
        Assert.Contains("Record", Seeds("a school records system"));
    }

    [Fact]
    public void The_built_in_domain_keeps_the_class_the_user_named()
    {
        var model = BundledDomainSource.CreateDefault()
            .Discover("a warehouse that records orders for customers").Model;

        Assert.Contains(model.Entities, e => e.Name == "Order");
        Assert.DoesNotContain(model.Entities, e => e.Name == "Record");
    }
}
