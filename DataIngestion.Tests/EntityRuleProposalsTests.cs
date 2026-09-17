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
    public void A_blank_cell_in_either_column_is_skipped_not_counted_as_a_break()
    {
        var entity = new CandidateEntity { Name = "Booking" };
        entity.Fields.Add(new CandidateField { Name = "StartDate", Type = "DateTime" });
        entity.Fields.Add(new CandidateField { Name = "EndDate", Type = "DateTime" });

        var table = TableOf("Booking", ["StartDate", "EndDate"],
        [
            ["2026-01-01", "2026-01-10"],
            [null, "2026-02-01"],
            ["2026-03-01", null],
        ]);

        var proposals = EntityRuleProposals.For(entity, table);

        Assert.Single(proposals, p => p.Kind == EntityRuleProposalKind.CrossField);
    }
}
