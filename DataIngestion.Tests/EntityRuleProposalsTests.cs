using CodeGenerationEngine.Models;
using DataIngestion;
using DataIngestion.Schema;
using DomainDiscovery.Model;
using Xunit;

namespace DataIngestion.Tests;

public class EntityRuleProposalsTests
{
    private static TabularTable TableOf(string name, string column, params string[] values)
        => TableOf(name, [column], values.Select(v => new[] { v }).ToArray());

    private static TabularTable TableOf(string name, string[] columns, string?[][] rows)
    {
        var table = new TabularTable { Name = name };
        foreach (var column in columns)
            table.Columns.Add(new TabularColumn { Name = column });
        foreach (var row in rows)
            table.Rows.Add(row);
        return table;
    }

    [Fact]
    public void Proposes_a_cross_field_rule_when_left_never_exceeds_right()
    {
        var entity = new CandidateEntity { Name = "Booking" };
        entity.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        entity.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });

        var table = TableOf("Booking", ["StartDate", "EndDate"],
        [
            ["2026-01-01", "2026-01-10"],
            ["2026-02-01", "2026-02-01"],
            ["2026-03-01", "2026-03-15"],
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        var crossField = Assert.Single(proposals, p => p.Kind == EntityRuleProposalKind.CrossField
            && p.Properties[0] == "StartDate" && p.Properties[1] == "EndDate");
        Assert.False(crossField.Accepted);
        Assert.Contains("StartDate", crossField.Evidence);
    }

    [Fact]
    public void One_counter_example_across_the_full_table_blocks_the_proposal()
    {
        var entity = new CandidateEntity { Name = "Booking" };
        entity.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        entity.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });

        var table = TableOf("Booking", ["StartDate", "EndDate"],
        [
            ["2026-01-01", "2026-01-10"],
            ["2026-05-01", "2026-01-01"], // StartDate after EndDate: the counter-example
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        Assert.DoesNotContain(proposals, p => p.Kind == EntityRuleProposalKind.CrossField
            && p.Properties[0] == "StartDate" && p.Properties[1] == "EndDate");
    }

    [Fact]
    public void A_column_that_never_strictly_precedes_the_other_is_not_proposed()
    {
        var entity = new CandidateEntity { Name = "Snapshot" };
        entity.Fields.Add(new CandidateField { Name = "A", Type = "int" });
        entity.Fields.Add(new CandidateField { Name = "B", Type = "int" });

        var table = TableOf("Snapshot", ["A", "B"],
        [
            ["5", "5"],
            ["7", "7"],
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        Assert.Empty(proposals);
    }

    [Fact]
    public void A_blank_start_is_judged_as_the_app_loads_it_and_holds()
    {
        var entity = new CandidateEntity { Name = "Booking" };
        entity.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        entity.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });

        var table = TableOf("Booking", ["StartDate", "EndDate"],
        [
            ["2026-01-01", "2026-01-10"],
            [null, "2026-02-01"],
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        // The blank StartDate loads as 0001-01-01, which is on or before any EndDate.
        Assert.Single(proposals, p => p.Kind == EntityRuleProposalKind.CrossField);
    }

    [Fact]
    public void Numbers_are_compared_in_number_words_not_date_words()
    {
        // "Quantity is always on or before UnitPrice" - date wording for two numbers.
        var entity = new CandidateEntity { Name = "Line" };
        entity.Fields.Add(new CandidateField { Name = "Quantity", Type = "int" });
        entity.Fields.Add(new CandidateField { Name = "UnitPrice", Type = "int" });

        var table = TableOf("Line", ["Quantity", "UnitPrice"],
        [
            ["1", "10"],
            ["2", "20"],
        ]);

        var crossField = Assert.Single(EntityRuleProposals.For(entity, table),
            p => p.Kind == EntityRuleProposalKind.CrossField);

        Assert.StartsWith("Quantity is never more than UnitPrice", crossField.Evidence);
        Assert.Equal("Quantity must not be greater than UnitPrice", crossField.Rule.Describe());
    }

    [Fact]
    public void A_blank_end_is_judged_as_the_app_loads_it_and_breaks_the_rule()
    {
        var entity = new CandidateEntity { Name = "Booking" };
        entity.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        entity.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });

        var table = TableOf("Booking", ["StartDate", "EndDate"],
        [
            ["2026-01-01", "2026-01-10"],
            ["2026-03-01", null],
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        // The blank EndDate loads as 0001-01-01, which the row's StartDate is after: skipping the
        // row offered "StartDate must not be after EndDate", and the app then refused the row.
        Assert.DoesNotContain(proposals, p => p.Kind == EntityRuleProposalKind.CrossField);
    }

    [Fact]
    public void A_field_renamed_off_a_raw_snake_case_column_still_gets_the_rule()
    {
        // "start_date"/"end_date" are the RAW source headers. NameNormalizer.ToFieldName turns
        // each into "StartDate"/"EndDate" — the same translation ResolveColumns/ColumnFor apply
        // for every other proposal path. The table below never has a column literally named
        // "StartDate", so a scan that looks cells up by the field name instead of the resolved
        // column name finds nothing and silently proposes zero Cross-Field rules.
        var entity = new CandidateEntity { Name = "Booking" };
        entity.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        entity.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });

        var table = TableOf("Booking", ["start_date", "end_date"],
        [
            ["2026-01-01", "2026-01-10"],
            ["2026-02-01", "2026-02-01"],
            ["2026-03-01", "2026-03-15"],
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        var crossField = Assert.Single(proposals, p => p.Kind == EntityRuleProposalKind.CrossField
            && p.Properties[0] == "StartDate" && p.Properties[1] == "EndDate");
        Assert.Contains("StartDate", crossField.Evidence);
    }

    [Fact]
    public void A_mixed_format_date_pair_does_not_crash_the_scan()
    {
        // "20260101" parses cleanly as a decimal (it is just eight digits) but NOT as a DateTime
        // under DateTimeStyles.None/InvariantCulture. A parser that tries decimal first, with no
        // regard for the field's actual type, would read this column as decimal while the
        // normally-formatted column below reads as DateTime — and comparing a decimal to a
        // DateTime via IComparable.CompareTo throws ArgumentException. Both fields are declared
        // DateTime here, so a type-safe scan must use the DateTime-only parser for both and never
        // reach that mixed-type comparison, whether or not it ends up proposing anything.
        var entity = new CandidateEntity { Name = "Snapshot" };
        entity.Fields.Add(new CandidateField { Name = "SnapshotDate", Type = "DateTime" });
        entity.Fields.Add(new CandidateField { Name = "AsOfDate", Type = "DateTime" });

        var table = TableOf("Snapshot", ["SnapshotDate", "AsOfDate"],
        [
            ["20260101", "2026-01-10"],
            ["20260201", "2026-02-15"],
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        Assert.Empty(proposals);
    }

    [Fact]
    public void Proposes_a_sum_calculation_when_it_holds_in_every_row()
    {
        var entity = new CandidateEntity { Name = "OrderLine" };
        entity.Fields.Add(new CandidateField { Name = "LineTotal", Type = "decimal" });
        entity.Fields.Add(new CandidateField { Name = "Goods", Type = "decimal" });
        entity.Fields.Add(new CandidateField { Name = "Tax", Type = "decimal" });

        var table = TableOf("OrderLine", ["LineTotal", "Goods", "Tax"],
        [
            ["120.00", "100.00", "20.00"],
            ["60.00", "50.00", "10.00"],
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        Assert.Single(proposals, p => p.Kind == EntityRuleProposalKind.Calculation
            && p.Properties.SequenceEqual(new[] { "LineTotal", "Goods", "Tax" })
            && p.Evidence.Contains("plus"));
    }

    [Fact]
    public void Proposes_a_percentage_calculation_when_it_holds_in_every_row()
    {
        var entity = new CandidateEntity { Name = "Invoice" };
        entity.Fields.Add(new CandidateField { Name = "Total", Type = "decimal" });
        entity.Fields.Add(new CandidateField { Name = "Net", Type = "decimal" });
        entity.Fields.Add(new CandidateField { Name = "Rate", Type = "decimal" });

        var table = TableOf("Invoice", ["Total", "Net", "Rate"],
        [
            ["120.00", "100.00", "0.20"],
            ["57.50", "50.00", "0.15"],
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        Assert.Single(proposals, p => p.Kind == EntityRuleProposalKind.Calculation
            && p.Properties.SequenceEqual(new[] { "Total", "Net", "Rate" }));
    }

    [Fact]
    public void A_calculation_is_not_proposed_when_one_row_breaks_it()
    {
        var entity = new CandidateEntity { Name = "OrderLine" };
        entity.Fields.Add(new CandidateField { Name = "LineTotal", Type = "decimal" });
        entity.Fields.Add(new CandidateField { Name = "Goods", Type = "decimal" });
        entity.Fields.Add(new CandidateField { Name = "Tax", Type = "decimal" });

        var table = TableOf("OrderLine", ["LineTotal", "Goods", "Tax"],
        [
            ["120.00", "100.00", "20.00"],
            ["999.00", "50.00", "10.00"], // breaks every calculation form
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        Assert.DoesNotContain(proposals, p => p.Kind == EntityRuleProposalKind.Calculation);
    }

    [Fact]
    public void More_than_the_cap_of_numeric_columns_skips_the_calculation_scan()
    {
        var entity = new CandidateEntity { Name = "Wide" };
        var columns = new List<string>();
        for (var i = 0; i < 14; i++)
        {
            entity.Fields.Add(new CandidateField { Name = $"N{i}", Type = "decimal" });
            columns.Add($"N{i}");
        }

        var table = TableOf("Wide", columns.ToArray(), [columns.Select(_ => "1.00").ToArray()]);

        var proposals = EntityRuleProposals.For(entity, table);

        Assert.DoesNotContain(proposals, p => p.Kind == EntityRuleProposalKind.Calculation);
    }

    /// <summary>Issue #131: the entity-level rules say so too.</summary>
    [Fact]
    public void Evidence_from_a_partly_read_table_says_so()
    {
        var entity = new CandidateEntity { Name = "Booking" };
        entity.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        entity.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });

        var table = TableOf("Booking", ["StartDate", "EndDate"],
        [
            ["2026-01-01", "2026-01-10"],
            ["2026-02-01", "2026-02-01"],
        ]);

        var partial = new TabularTable { Name = table.Name, IsPartial = true, SourceRowCount = 9_000 };
        partial.Columns.AddRange(table.Columns);
        partial.Rows.AddRange(table.Rows);

        var crossField = Assert.Single(
            EntityRuleProposals.For(entity, partial), p => p.Kind == EntityRuleProposalKind.CrossField);
        Assert.Contains("read 2 of the table's 9,000 rows", crossField.Evidence);
    }
}
