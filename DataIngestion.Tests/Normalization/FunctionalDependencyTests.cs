using DataIngestion.Normalization;
using Xunit;

namespace DataIngestion.Tests.Normalization;

public class FunctionalDependencyTests
{
    private static DataIngestion.Schema.TabularTable Table(string[] header, params string?[][] rows)
        => RepeatingGroupTests.Table("Orders", header, rows);

    /// <summary>Two columns determined by CustomerName, which is not the key. Extract Customer.</summary>
    [Fact]
    public void ColumnsDeterminedByANonKeyColumnAreAPartialDependency()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "CustomerName", "CustomerCity", "CustomerPostCode"],
            ["ORD-1", "Ada", "London", "E1"],
            ["ORD-2", "Grace", "York", "YO1"],
            ["ORD-3", "Ada", "London", "E1"],
            ["ORD-4", "Grace", "York", "YO1"],
            ["ORD-5", "Ada", "London", "E1"],
            ["ORD-6", "Grace", "York", "YO1"]));

        var finding = Assert.Single(
            report.Findings, f => f.Kind == NormalizationFindingKind.FunctionalDependency);

        Assert.Equal("CustomerName", finding.Columns[0]);
        Assert.Contains("CustomerCity", finding.Columns);
        Assert.Contains("CustomerPostCode", finding.Columns);
        Assert.Equal("Customer", finding.ProposedEntity);
        Assert.Contains("Order references it", finding.Proposal);
    }

    /// <summary>
    /// Carrying out a split must not respell the user's data. Compared case-blind, "ada" was the
    /// same customer as "Ada", so the order that said "ada" / "LONDON" came back pointing at the
    /// row saying "Ada" / "London" and its own spelling was gone. Two spellings are two values.
    /// </summary>
    [Fact]
    public void ApplyingADependencyKeepsEveryRowsOwnSpelling()
    {
        var table = Table(
            ["Ref", "CustomerName", "CustomerCity"],
            ["ORD-1", "Ada", "London"],
            ["ORD-2", "Grace", "York"],
            ["ORD-3", "Ada", "London"],
            ["ORD-4", "Grace", "York"],
            ["ORD-5", "ada", "LONDON"],
            ["ORD-6", "Ada", "London"],
            ["ORD-7", "Grace", "York"],
            ["ORD-8", "Grace", "York"]);

        var finding = Assert.Single(
            NormalizationAnalyzer.Analyze(table).Findings,
            f => f.Kind == NormalizationFindingKind.FunctionalDependency);

        var tables = NormalizationPlan.Apply([table], [finding]);
        var orders = tables.Single(t => t.Name == "Orders");
        var customers = tables.Single(t => t.Name == finding.ProposedEntity);

        for (var row = 0; row < table.Rows.Count; row++)
        {
            var id = int.Parse(orders.Value(row, customers.Name + "Id")!);

            Assert.Equal(table.Value(row, "CustomerName"), customers.Value(id - 1, "CustomerName"));
            Assert.Equal(table.Value(row, "CustomerCity"), customers.Value(id - 1, "CustomerCity"));
        }
    }

    /// <summary>
    /// A single dependent is only reported when the two columns share a name prefix. Otherwise any
    /// column that happens to determine one other column would be flagged, and the report becomes
    /// noise the user learns to skip.
    /// </summary>
    [Fact]
    public void OneDependentIsReportedWhenTheNamesShareAPrefix()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "CustomerName", "CustomerCity"],
            ["ORD-1", "Ada", "London"],
            ["ORD-2", "Grace", "York"],
            ["ORD-3", "Ada", "London"],
            ["ORD-4", "Grace", "York"],
            ["ORD-5", "Ada", "London"]));

        Assert.Contains(report.Findings,
            f => f.Kind == NormalizationFindingKind.FunctionalDependency);
    }

    [Fact]
    public void OneDependentWithUnrelatedNamesIsNotReported()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "Status", "Colour"],
            ["ORD-1", "open", "red"],
            ["ORD-2", "shut", "blue"],
            ["ORD-3", "open", "red"],
            ["ORD-4", "shut", "blue"],
            ["ORD-5", "open", "red"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.FunctionalDependency);
    }

    /// <summary>
    /// A near-unique column determines everything trivially -- that is what being a key means, and
    /// extracting an entity per row is not normalization.
    /// </summary>
    [Fact]
    public void ANearUniqueColumnIsNotADeterminant()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "RefName", "Other"],
            ["ORD-1", "a", "1"],
            ["ORD-2", "b", "2"],
            ["ORD-3", "c", "3"],
            ["ORD-4", "d", "4"],
            ["ORD-5", "e", "5"],
            ["ORD-6", "f", "6"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.FunctionalDependency);
    }

    /// <summary>One counter-example kills the dependency: Ada must map to one city on EVERY row.</summary>
    [Fact]
    public void OneContradictingRowMeansThereIsNoDependency()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "CustomerName", "CustomerCity"],
            ["ORD-1", "Ada", "London"],
            ["ORD-2", "Grace", "York"],
            ["ORD-3", "Ada", "Leeds"],
            ["ORD-4", "Grace", "York"],
            ["ORD-5", "Ada", "London"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.FunctionalDependency);
    }

    /// <summary>
    /// Three rows cannot evidence a dependency -- any two columns will agree by coincidence. The
    /// analyser says so rather than reporting nothing, which would read as "no problems found".
    /// </summary>
    [Fact]
    public void TooFewRowsIsReportedAsANote_NotAsSilence()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "CustomerName", "CustomerCity"],
            ["ORD-1", "Ada", "London"],
            ["ORD-2", "Grace", "York"]));

        Assert.DoesNotContain(report.Findings,
            f => f.Kind == NormalizationFindingKind.FunctionalDependency);
        Assert.Contains(NormalizationAnalyzer.TooFewRowsNote, report.Notes);
    }

    [Fact]
    public void EvidenceShowsEachDeterminantValueAndWhatItMapsTo()
    {
        var report = NormalizationAnalyzer.Analyze(Table(
            ["Ref", "CustomerName", "CustomerCity"],
            ["ORD-1", "Ada", "London"],
            ["ORD-2", "Grace", "York"],
            ["ORD-3", "Ada", "London"],
            ["ORD-4", "Grace", "York"],
            ["ORD-5", "Ada", "London"]));

        var finding = report.Findings.Single(
            f => f.Kind == NormalizationFindingKind.FunctionalDependency);

        Assert.Contains(finding.Evidence, line => line.Contains("Ada") && line.Contains("London"));
    }
}
