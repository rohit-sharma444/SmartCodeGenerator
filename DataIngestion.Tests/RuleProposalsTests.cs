using CodeGenerationEngine.Models;
using DataIngestion.Schema;
using DomainDiscovery.Model;
using Xunit;

namespace DataIngestion.Tests;

public class RuleProposalsTests
{
    [Fact]
    public void A_small_set_of_short_values_is_proposed_as_a_status()
    {
        var scan = new ColumnScanResult(["Draft", "Posted", "Cancelled"], false, 9, 3, 12_431, 12_431);

        var proposals = RuleProposals.For(scan, "Status", "string", mayBeUnique: true);

        var status = proposals.Single(p => p.Kind == RuleProposalKind.Status);
        Assert.Equal("allowedvalues", status.Rule.Name);
        Assert.Equal(
            new[] { "Draft", "Posted", "Cancelled" }.Order(), status.Rule.AllowedValues.Order());
        Assert.Contains("12,431", status.Evidence);
        Assert.False(status.Accepted, "a proposal arrives unaccepted");
    }

    [Fact]
    public void A_long_valued_column_is_not_a_status()
    {
        // The length bound is what separates a status from a low-cardinality description or a
        // notes column that happens to repeat.
        var scan = new ColumnScanResult(["a short one", new string('x', 31)], false, 31, 2, 40, 40);

        var proposals = RuleProposals.For(scan, "Notes", "string", mayBeUnique: true);

        Assert.DoesNotContain(proposals, p => p.Kind == RuleProposalKind.Status);
    }

    [Fact]
    public void More_than_fifty_values_is_not_a_status()
    {
        var scan = new ColumnScanResult(
            [.. Enumerable.Range(1, 51).Select(i => $"v{i}")], true, 4, 0, 9_000, 9_000);

        var proposals = RuleProposals.For(scan, "Reference", "string", mayBeUnique: true);

        Assert.DoesNotContain(proposals, p => p.Kind == RuleProposalKind.Status);
    }

    [Fact]
    public void A_column_with_no_repeats_is_proposed_as_unique()
    {
        var scan = new ColumnScanResult(["A1", "A2"], true, 2, 0, 12_431, 12_431);

        var unique = RuleProposals.For(scan, "Code", "string", mayBeUnique: true)
            .Single(p => p.Kind == RuleProposalKind.Unique);

        Assert.Equal("unique", unique.Rule.Name);
        Assert.Contains("12,431", unique.Evidence);
        // "share a OrderDate": no article to get wrong.
        Assert.Contains("share the same Code", unique.Evidence);
        Assert.False(unique.Accepted);
    }

    [Fact]
    public void A_column_with_a_repeat_is_not_proposed_as_unique()
    {
        var scan = new ColumnScanResult(["A1", "A1"], false, 2, 1, 2, 2);

        Assert.DoesNotContain(
            RuleProposals.For(scan, "Code", "string", mayBeUnique: true),
            p => p.Kind == RuleProposalKind.Unique);
    }

    [Fact]
    public void A_two_row_boolean_column_is_not_proposed_as_unique()
    {
        // A bool has exactly two values. Two rows that happen to be true and false have no
        // duplicate yet, but the THIRD row -- true or false -- necessarily repeats one, so the
        // rule this would propose is one every future row breaks half the time.
        var scan = new ColumnScanResult(["True", "False"], false, 5, 0, 2, 2);

        Assert.DoesNotContain(
            RuleProposals.For(scan, "IsActive", "bool", mayBeUnique: true),
            p => p.Kind == RuleProposalKind.Unique);
    }

    [Fact]
    public void Uniqueness_is_not_proposed_where_it_may_not_be()
    {
        var scan = new ColumnScanResult(["A1", "A2"], true, 2, 0, 4, 4);

        // A transaction line's product code repeats by design. Proposing uniqueness on a small
        // young table would propose a rule forbidding a second sale of the same product.
        Assert.DoesNotContain(
            RuleProposals.For(scan, "ProductCode", "string", mayBeUnique: false),
            p => p.Kind == RuleProposalKind.Unique);
    }

    [Fact]
    public void A_column_whose_every_value_is_an_email_is_proposed_as_one()
    {
        var scan = new ColumnScanResult(["a@b.com", "c@d.co.uk"], false, 9, 0, 2, 2);

        var pattern = RuleProposals.For(scan, "Contact", "string", mayBeUnique: true)
            .Single(p => p.Kind == RuleProposalKind.Pattern);

        Assert.Equal("pattern", pattern.Rule.Name);
        Assert.Equal("email address", pattern.Rule.CustomRule);
    }

    [Fact]
    public void One_value_that_does_not_match_stops_the_pattern()
    {
        var scan = new ColumnScanResult(["a@b.com", "not an email"], false, 12, 0, 2, 2);

        Assert.DoesNotContain(
            RuleProposals.For(scan, "Contact", "string", mayBeUnique: true),
            p => p.Kind == RuleProposalKind.Pattern);
    }

    [Fact]
    public void A_column_past_the_distinct_cap_can_still_be_unique()
    {
        var scan = new ColumnScanResult(["A1"], true, 2, 0, 1_000_000, 1_000_000);

        // A million distinct references is the case uniqueness matters most for, and it is
        // always past the cap. It is NOT offered a status or a pattern — neither can be judged
        // from the 51 values that were kept — but zero repeats is a complete answer.
        var proposals = RuleProposals.For(scan, "Code", "string", mayBeUnique: true);

        Assert.Contains(proposals, p => p.Kind == RuleProposalKind.Unique);
        Assert.DoesNotContain(proposals, p => p.Kind == RuleProposalKind.Status);
        Assert.DoesNotContain(proposals, p => p.Kind == RuleProposalKind.Pattern);
    }

    [Fact]
    public void A_table_with_no_rows_is_offered_nothing()
    {
        // The shape a discovered domain arrives in: columns but no rows. Wikipedia and Ollama
        // produce no tables at all and never reach this code, but a table read from an empty CSV
        // does — and a rule proposed from zero rows would hold vacuously in all of them.
        var table = new TabularTable { Name = "Book" };
        table.Columns.Add(new TabularColumn { Name = "Title" });

        var model = DataModelBuilder.Build([table]);

        Assert.True(model.Entities.SelectMany(e => e.Fields).All(f => f.Proposals.Count == 0));
    }

    /// <summary>
    /// Issue #131: a rule found in the rows that were read of a partly-read table must not say
    /// "in all N rows" as though N were the table.
    /// </summary>
    [Fact]
    public void Evidence_from_a_partly_read_table_says_so()
    {
        var table = new TabularTable { Name = "Orders", IsPartial = true, SourceRowCount = 47_000_000 };
        table.Columns.Add(new TabularColumn { Name = "Status" });
        table.Rows.Add(["Draft"]);
        table.Rows.Add(["Posted"]);

        var proposals = RuleProposals.For(ColumnScan.Of(table, "Status"), "Status", "string", mayBeUnique: true);

        Assert.NotEmpty(proposals);
        Assert.All(proposals, p => Assert.Contains("read 2 of the table's 47,000,000 rows", p.Evidence));
    }
}
