using DataIngestion.Normalization;
using Xunit;

namespace DataIngestion.Tests.Normalization;

public class NonAtomicValueTests
{
    private static DataIngestion.Schema.TabularTable Table(string[] header, params string?[][] rows)
        => RepeatingGroupTests.Table("Orders", header, rows);

    [Fact]
    public void ARepeatedCommaSeparatedListIsNonAtomic()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "Tags"],
            ["ORD-1", "rush,gift"],
            ["ORD-2", "rush,fragile"],
            ["ORD-3", "gift,fragile"]));

        var finding = Assert.Single(
            report.Findings, f => f.Kind == NormalizationFindingKind.NonAtomicValues);

        Assert.Equal(new[] { "Tags" }, finding.Columns);
        Assert.Equal("Tag", finding.ProposedEntity);
        Assert.Equal(',', finding.Separator);
        Assert.Contains("Tags", finding.Headline);
    }

    [Theory]
    [InlineData(';')]
    [InlineData('|')]
    public void SemicolonsAndPipesAreSeparatorsToo(char separator)
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "Tags"],
            ["ORD-1", $"rush{separator}gift"],
            ["ORD-2", $"rush{separator}fragile"],
            ["ORD-3", $"gift{separator}fragile"]));

        Assert.Equal(separator, report.Findings
            .Single(f => f.Kind == NormalizationFindingKind.NonAtomicValues).Separator);
    }

    /// <summary>
    /// Prose is full of commas. Its split parts never recur, which is the whole test -- without
    /// it every free-text column in every export would be "offered" as an entity.
    /// </summary>
    [Fact]
    public void ProseIsNotNonAtomic_BecauseItsPartsNeverRecur()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "Notes"],
            ["ORD-1", "Call the customer, then dispatch"],
            ["ORD-2", "Left with neighbour, signed for"],
            ["ORD-3", "Damaged in transit, refunded"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.NonAtomicValues);
    }

    /// <summary>A separator in a minority of rows is punctuation, not structure.</summary>
    [Fact]
    public void ASeparatorInOnlyOneRowOfManyIsNotStructure()
    {
        var rows = Enumerable.Range(1, 9)
            .Select(i => new string?[] { $"ORD-{i}", "rush" })
            .Append(["ORD-10", "rush,gift"])
            .ToArray();

        var report = NormalizationAnalyzer.Analyze(Table(["Ref", "Tags"], rows));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.NonAtomicValues);
    }

    /// <summary>Only string columns can be non-atomic; a decimal column's "1,50" is a number.</summary>
    [Fact]
    public void ANumericColumnIsNeverNonAtomic()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "Total"], ["ORD-1", "9.99"], ["ORD-2", "4.50"], ["ORD-3", "1.00"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.NonAtomicValues);
    }

    [Fact]
    public void EvidenceShowsThePartsThatRecur()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "Tags"],
            ["ORD-1", "rush,gift"],
            ["ORD-2", "rush,fragile"],
            ["ORD-3", "gift,fragile"]));

        var finding = report.Findings.Single(
            f => f.Kind == NormalizationFindingKind.NonAtomicValues);

        Assert.Contains(finding.Evidence, line => line.Contains("rush"));
    }
}
