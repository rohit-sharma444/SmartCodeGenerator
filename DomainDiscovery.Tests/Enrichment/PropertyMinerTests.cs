using DomainDiscovery.Enrichment;
using Xunit;

namespace DomainDiscovery.Tests.Enrichment;

public class PropertyMinerTests
{
    [Fact]
    public void Mines_a_bulleted_list_that_follows_a_containment_lead_in()
    {
        var article = """
            An invoice is a commercial document.
            An invoice typically contains:
            * invoice number
            * date of issue
            * total amount
            """;

        var names = PropertyMiner.Mine(article, "Invoice").Select(f => f.Name).ToList();

        Assert.Contains("InvoiceNumber", names);
        Assert.Contains("DateOfIssue", names);
        Assert.Contains("TotalAmount", names);
    }

    [Fact]
    public void Ignores_a_bulleted_list_with_no_containment_lead_in()
    {
        // A "See also" list is not a property list. Without the lead-in rule, every article's
        // navigation furniture becomes fields.
        var article = """
            An invoice is a commercial document.
            See also:
            * Receipt
            * Purchase order
            """;

        Assert.Empty(PropertyMiner.Mine(article, "Invoice"));
    }

    [Fact]
    public void Mines_a_prose_enumeration_after_a_containment_verb()
    {
        var article = "A receipt includes the date of sale, the amount paid and the payment method.";

        var names = PropertyMiner.Mine(article, "Receipt").Select(f => f.Name).ToList();

        Assert.Contains("DateOfSale", names);
        Assert.Contains("AmountPaid", names);
        Assert.Contains("PaymentMethod", names);
    }

    [Fact]
    public void Infers_a_type_for_each_mined_property()
    {
        var article = """
            An invoice contains:
            * total amount
            * date of issue
            """;

        var fields = PropertyMiner.Mine(article, "Invoice");

        Assert.Contains(fields, f => f.Name == "TotalAmount" && f.Type == "decimal");
        Assert.Contains(fields, f => f.Name == "DateOfIssue" && f.Type == "DateTime");
    }

    [Fact]
    public void Never_mines_a_property_named_Id()
    {
        var article = """
            A record contains:
            * id
            * name
            """;

        Assert.DoesNotContain(PropertyMiner.Mine(article, "Record"), f => f.Name == "Id");
    }

    // class Invoice { public string Invoice { get; set; } } is CS0542.
    [Fact]
    public void Never_mines_a_property_named_after_its_own_class()
    {
        var article = """
            An invoice contains:
            * invoice
            * total amount
            """;

        Assert.DoesNotContain(PropertyMiner.Mine(article, "Invoice"), f => f.Name == "Invoice");
    }

    [Fact]
    public void Rejects_a_phrase_too_long_to_be_a_property_name()
    {
        var article = """
            An invoice contains:
            * a detailed explanation of every term and condition that may apply
            * total amount
            """;

        var names = PropertyMiner.Mine(article, "Invoice").Select(f => f.Name).ToList();

        Assert.Equal(new[] { "TotalAmount" }, names);
    }

    [Fact]
    public void Does_not_mine_the_same_property_twice()
    {
        var article = """
            An invoice contains:
            * total amount
            * total amount
            """;

        Assert.Single(PropertyMiner.Mine(article, "Invoice"));
    }

    [Fact]
    public void Caps_how_many_properties_one_article_may_contribute()
    {
        var items = string.Join('\n', Enumerable.Range(1, 30).Select(n => $"* field number {n}"));
        var article = "A record contains:\n" + items;

        Assert.True(PropertyMiner.Mine(article, "Record").Count <= PropertyMiner.MaxMinedFields);
    }

    // Measured against the real Invoice and Receipt articles, the prose path mined
    // "InAncientMesopotamia", "FunctionedAsContracts", "OfGoods", "DidNotReceive" and
    // "ForTheirAgreement" -- sentence fragments the enumeration split mid-clause, not properties.
    // A property is a NOUN PHRASE, so a phrase opening with a preposition, a conjunction or an
    // auxiliary verb is a fragment and is not one.
    [Theory]
    [InlineData("in ancient Mesopotamia")]
    [InlineData("of goods")]
    [InlineData("for their agreement")]
    [InlineData("did not receive")]
    [InlineData("that the buyer")]
    public void Rejects_a_sentence_fragment_that_is_not_a_noun_phrase(string fragment)
    {
        var article = $"An invoice contains:\n* {fragment}\n* total amount";

        var names = PropertyMiner.Mine(article, "Invoice").Select(f => f.Name).ToList();

        Assert.Equal(new[] { "TotalAmount" }, names);
    }

    // ... but a real noun phrase that merely happens to start with a participle survives.
    [Fact]
    public void Keeps_a_noun_phrase_that_opens_with_a_participle()
    {
        var article = "An order contains:\n* agreed prices for products";

        Assert.Contains(
            PropertyMiner.Mine(article, "Order"), f => f.Name == "AgreedPricesForProducts");
    }

    /// <summary>
    /// Half the containment verbs are also common nouns — a record, a list, a show, a state — and
    /// the miner used to take the noun for the verb, then swallow the real verb into the property
    /// name: "An inventory record contains X" yielded a field called ContainsTheQuantityHeld.
    /// </summary>
    [Fact]
    public void Does_not_mistake_the_subject_noun_for_the_containment_verb()
    {
        var article = "An inventory record contains the quantity held and the unit cost.";

        var fields = PropertyMiner.Mine(article, "Inventory").Select(f => f.Name).ToList();

        Assert.Contains("QuantityHeld", fields);
        Assert.Contains("UnitCost", fields);
        Assert.DoesNotContain("ContainsTheQuantityHeld", fields);
    }

    [Fact]
    public void Reads_past_a_chain_of_noun_verbs_to_the_real_one()
    {
        // "list" is a noun here and "includes" is the verb.
        var article = "The shopping list includes bread and milk.";

        var fields = PropertyMiner.Mine(article, "Basket").Select(f => f.Name).ToList();

        Assert.Contains("Bread", fields);
        Assert.Contains("Milk", fields);
        Assert.DoesNotContain("IncludesBread", fields);
    }

    [Fact]
    public void Still_reads_a_containment_verb_that_is_genuinely_the_verb()
    {
        // "records" is the verb, and nothing follows it that could be mistaken for one.
        var article = "A receipt records the sales tax and the amount paid.";

        var fields = PropertyMiner.Mine(article, "Receipt").Select(f => f.Name).ToList();

        Assert.Contains("SalesTax", fields);
        Assert.Contains("AmountPaid", fields);
    }

    [Fact]
    public void Keeps_a_hyphenated_word_whole()
    {
        // The Invoice article's lead says "agreed-upon prices", and the gloss stripper used to cut
        // at the hyphen and leave a field called "Agreed".
        var article = "An invoice contains agreed-upon prices.";

        Assert.Contains(
            PropertyMiner.Mine(article, "Invoice"), f => f.Name == "AgreedUponPrices");
    }

    [Fact]
    public void Still_strips_a_gloss_that_a_dash_sets_off()
    {
        // A dash with space around it introduces an aside; a hyphen inside a word does not.
        var article = "An invoice contains:\n* date of issue - the day the invoice was raised";

        Assert.Contains(PropertyMiner.Mine(article, "Invoice"), f => f.Name == "DateOfIssue");
    }

    // The no-op contract: an article with nothing to say must leave the class exactly as
    // EntityShaper already shaped it, never worse.
    [Fact]
    public void Mines_nothing_from_an_article_with_no_property_evidence()
    {
        Assert.Empty(PropertyMiner.Mine("A point of sale is where a sale is completed.", "PointOfSale"));
    }

    [Fact]
    public void Mines_nothing_from_empty_input()
    {
        Assert.Empty(PropertyMiner.Mine("", "Invoice"));
        Assert.Empty(PropertyMiner.Mine(null!, "Invoice"));
    }
}
