using DataIngestion.Normalization;
using Xunit;

namespace DataIngestion.Tests.Normalization;

/// <summary>
/// A column equal to the product or sum of two others. Reported as a NOTE, never a finding: the
/// generator cannot emit a computed property, so "drop it and compute it" dropped the column and
/// computed nothing, and the user's values were gone from the app. The column is kept as stored.
/// </summary>
public class DerivedColumnTests
{
    private static DataIngestion.Schema.TabularTable Table(string[] header, params string?[][] rows)
        => RepeatingGroupTests.Table("Orders", header, rows);

    private static bool IsDerivedNote(string note) => note.Contains("is always");

    [Fact]
    public void AProductOfTwoColumnsIsNotedAndKept()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Qty", "UnitPrice", "Total"],
            ["2", "5.00", "10.00"],
            ["3", "2.50", "7.50"],
            ["4", "1.25", "5.00"],
            ["5", "2.00", "10.00"],
            ["6", "0.50", "3.00"]));

        Assert.DoesNotContain(report.Findings, f => f.Kind == NormalizationFindingKind.DerivedColumn);

        var note = Assert.Single(report.Notes, IsDerivedNote);
        Assert.Contains("\"Total\" is always Qty x UnitPrice", note);
        Assert.Contains("kept", note);
        Assert.DoesNotContain("compute it", note);
    }

    /// <summary>Choosing every finding (`normalize`) leaves the derived column and its values in
    /// the table.</summary>
    [Fact]
    public void ApplyingEveryFindingKeepsTheDerivedColumn()
    {
        var table = Table(
            ["Qty", "UnitPrice", "Total"],
            ["2", "5.00", "10.00"],
            ["3", "2.50", "7.50"],
            ["4", "1.25", "5.00"],
            ["5", "2.00", "10.00"],
            ["6", "0.50", "3.00"]);

        var applied = NormalizationPlan.Apply([table], NormalizationAnalyzer.Analyze(table).Findings);

        var order = Assert.Single(applied);
        Assert.Equal(new[] { "Qty", "UnitPrice", "Total" }, order.Columns.Select(c => c.Name));
        Assert.Equal("7.50", order.Value(1, "Total"));
    }

    [Fact]
    public void ASumOfTwoColumnsIsNoted()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Net", "Tax", "Gross"],
            ["10.00", "2.00", "12.00"],
            ["20.00", "4.00", "24.00"],
            ["5.00", "1.00", "6.00"],
            ["8.00", "1.60", "9.60"],
            ["3.00", "0.60", "3.60"]));

        Assert.Contains(report.Notes, n => n.Contains("\"Gross\" is always Net + Tax"));
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

        Assert.DoesNotContain(report.Notes, IsDerivedNote);
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

        Assert.DoesNotContain(report.Notes, IsDerivedNote);
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

        Assert.DoesNotContain(report.Notes, IsDerivedNote);
    }
}
