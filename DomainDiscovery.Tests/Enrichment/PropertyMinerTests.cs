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
