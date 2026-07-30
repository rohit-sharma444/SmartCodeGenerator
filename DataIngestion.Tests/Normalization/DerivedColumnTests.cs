using DataIngestion.Normalization;
using Xunit;

namespace DataIngestion.Tests.Normalization;

public class DerivedColumnTests
{
    private static DataIngestion.Schema.TabularTable Table(string[] header, params string?[][] rows)
        => RepeatingGroupTests.Table("Orders", header, rows);

    [Fact]
    public void AProductOfTwoColumnsIsDerived()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Qty", "UnitPrice", "Total"],
            ["2", "5.00", "10.00"],
            ["3", "2.50", "7.50"],
            ["4", "1.25", "5.00"],
            ["5", "2.00", "10.00"],
            ["6", "0.50", "3.00"]));

        var finding = Assert.Single(
            report.Findings, f => f.Kind == NormalizationFindingKind.DerivedColumn);

        Assert.Equal(new[] { "Total" }, finding.Columns);
        Assert.Null(finding.ProposedEntity);
        Assert.Contains("Qty", finding.Headline);
        Assert.Contains("UnitPrice", finding.Headline);
    }

    [Fact]
    public void ASumOfTwoColumnsIsDerived()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Net", "Tax", "Gross"],
            ["10.00", "2.00", "12.00"],
            ["20.00", "4.00", "24.00"],
            ["5.00", "1.00", "6.00"],
            ["8.00", "1.60", "9.60"],
            ["3.00", "0.60", "3.60"]));

        Assert.Contains(report.Findings, f =>
            f.Kind == NormalizationFindingKind.DerivedColumn && f.Columns[0] == "Gross");
    }

    /// <summary>One row that does not add up means the column is not derived -- it holds data.</summary>
    [Fact]
    public void OneRowThatDoesNotAddUpMeansItIsNotDerived()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Net", "Tax", "Gross"],
            ["10.00", "2.00", "12.00"],
            ["20.00", "4.00", "24.00"],
            ["5.00", "1.00", "6.00"],
            ["8.00", "1.60", "9.60"],
            ["3.00", "0.60", "99.00"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.DerivedColumn);
    }

    /// <summary>Coincidence protection: with all-zero columns everything is both sum and product
    /// of everything, and reporting that is worse than reporting nothing.</summary>
    [Fact]
    public void AllZeroColumnsAreNotReportedAsDerived()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["A", "B", "C"],
            ["0", "0", "0"],
            ["0", "0", "0"],
            ["0", "0", "0"],
            ["0", "0", "0"],
            ["0", "0", "0"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.DerivedColumn);
    }

    [Fact]
    public void NonNumericColumnsAreNeverDerived()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["A", "B", "C"],
            ["x", "y", "z"],
            ["p", "q", "r"],
            ["s", "t", "u"],
            ["v", "w", "a"],
            ["b", "c", "d"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.DerivedColumn);
    }
}
