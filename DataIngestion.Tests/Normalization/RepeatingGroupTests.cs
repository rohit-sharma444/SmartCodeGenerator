using DataIngestion.Normalization;
using DataIngestion.Schema;
using Xunit;

namespace DataIngestion.Tests.Normalization;

public class RepeatingGroupTests
{
    /// <summary>Builds a table from a header row and data rows, typing each column from its values.</summary>
    internal static TabularTable Table(string name, string[] header, params string?[][] rows)
    {
        var table = new TabularTable { Name = name, SourceLabel = name + ".csv" };
        foreach (var column in header)
            table.Columns.Add(new TabularColumn { Name = column });

        foreach (var row in rows)
            table.Rows.Add(row);

        foreach (var column in table.Columns)
            column.Type = ColumnTypeInference.Infer(table.ColumnValues(column.Name), column.Name);

        return table;
    }

    [Fact]
    public void ThreeOrdinalColumnsAreARepeatingGroup()
    {
        var report = NormalizationAnalyzer.Analyze(Table("Orders",
            ["Ref", "Item1", "Item2", "Item3"],
            ["ORD-1", "Nut", "Bolt", "Washer"],
            ["ORD-2", "Nut", null, null]));

        var finding = Assert.Single(
            report.Findings, f => f.Kind == NormalizationFindingKind.RepeatingGroup);

        Assert.Equal(new[] { "Item1", "Item2", "Item3" }, finding.Columns);

        // ProposedEntity is the FINAL entity name, so NormalizationPlan never has to re-derive
        // it from the proposal text. The parent is singularized: Orders.csv is about an Order.
        Assert.Equal("OrderItem", finding.ProposedEntity);
        Assert.Contains("Item1, Item2, Item3", finding.Headline);
        Assert.Contains("many per Order", finding.Proposal);
    }

    [Fact]
    public void UnderscoreSeparatedOrdinalsAreAlsoARepeatingGroup()
    {
        var report = NormalizationAnalyzer.Analyze(Table("Members",
            ["Name", "Phone_1", "Phone_2"],
            ["Ada", "555-1", "555-2"],
            ["Grace", "555-3", null]));

        var finding = Assert.Single(
            report.Findings, f => f.Kind == NormalizationFindingKind.RepeatingGroup);

        Assert.Equal("MemberPhone", finding.ProposedEntity);
    }

    /// <summary>One ordinal column is not a group -- there is nothing repeating about it.</summary>
    [Fact]
    public void ASingleOrdinalColumnIsNotAGroup()
    {
        var report = NormalizationAnalyzer.Analyze(Table("Orders",
            ["Ref", "Line1"], ["ORD-1", "Nut"], ["ORD-2", "Bolt"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.RepeatingGroup);
    }

    /// <summary>Different stems are different columns that happen to end in digits.</summary>
    [Fact]
    public void ColumnsWithDifferentStemsAreNotAGroup()
    {
        var report = NormalizationAnalyzer.Analyze(Table("Orders",
            ["Address1", "Phone2"], ["A", "B"], ["C", "D"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.RepeatingGroup);
    }

    /// <summary>The evidence must show the actual cells, or the user cannot check the claim.</summary>
    [Fact]
    public void EvidenceQuotesRealRows()
    {
        var report = NormalizationAnalyzer.Analyze(Table("Orders",
            ["Ref", "Item1", "Item2"], ["ORD-1", "Nut", "Bolt"], ["ORD-2", "Washer", null]));

        var finding = report.Findings.Single(
            f => f.Kind == NormalizationFindingKind.RepeatingGroup);

        Assert.Contains(finding.Evidence, line => line.Contains("Nut") && line.Contains("Bolt"));
    }
}
