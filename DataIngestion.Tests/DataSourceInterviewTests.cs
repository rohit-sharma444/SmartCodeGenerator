using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests;

public class DataSourceInterviewTests
{
    private static string TempCsv()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, "Id,Name\n1,Ada\n");
        return path;
    }

    private static InterviewOutcome Run(string input, out string written)
    {
        var writer = new StringWriter();
        var outcome = DataSourceInterview.Resolve(new StringReader(input), writer);
        written = writer.ToString();
        return outcome;
    }

    [Theory]
    [InlineData("n\n")]
    [InlineData("N\n")]
    [InlineData("no\n")]
    [InlineData("\n")]   // blank takes the default, which is No
    public void SayingNo_MeansNoData(string input)
    {
        var outcome = Run(input, out var written);

        Assert.Equal(InterviewDecision.NoData, outcome.Decision);
        Assert.Null(outcome.Source);
        Assert.Contains("Do you have existing data?", written);
    }

    [Fact]
    public void SayingYesThenPickingCsv_AsksForThePathAndReturnsIt()
    {
        var path = TempCsv();

        try
        {
            var outcome = Run($"y\ncsv\n{path}\n", out var written);

            Assert.Equal(InterviewDecision.Source, outcome.Decision);
            Assert.Equal(DataSourceKind.Csv, outcome.Source!.Kind);
            Assert.Equal(path, outcome.Source.Location);
            Assert.Contains("sql/excel/txt/json/csv", written);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>The menu offers all five formats, so a user is never left wondering whether Excel
    /// was considered.</summary>
    [Fact]
    public void TheFormatPromptOffersEveryFormat()
    {
        Run("y\nc\n", out var written);

        foreach (var key in DataSourceReaders.Keys)
            Assert.Contains(key, written);
    }

    [Fact]
    public void AnUnknownFormat_ReAsksRatherThanFailing()
    {
        var path = TempCsv();

        try
        {
            var outcome = Run($"y\npostgres\ncsv\n{path}\n", out var written);

            Assert.Equal(InterviewDecision.Source, outcome.Decision);
            Assert.Contains("Unknown format", written);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Excel and SQL are on the menu but have no reader here. The user gets the reason and
    /// the alternative, then another go -- not a crash and not a silent fallback.</summary>
    [Theory]
    [InlineData("excel")]
    [InlineData("sql")]
    public void AFormatWithNoReader_SaysSoAndReAsks(string format)
    {
        var path = TempCsv();

        try
        {
            var outcome = Run($"y\n{format}\ncsv\n{path}\n", out var written);

            Assert.Equal(InterviewDecision.Source, outcome.Decision);
            Assert.Contains("not in this build yet", written);
            Assert.Contains("csv", written);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A mistyped path is the most likely thing to go wrong here, so it re-asks -- the same
    /// way ResolveTarget re-asks on an unknown target key.</summary>
    [Fact]
    public void AMissingFile_ReAsksForThePath()
    {
        var path = TempCsv();

        try
        {
            var outcome = Run($"y\ncsv\nD:\\nope\\missing.csv\n{path}\n", out var written);

            Assert.Equal(InterviewDecision.Source, outcome.Decision);
            Assert.Equal(path, outcome.Source!.Location);
            Assert.Contains("No file at", written);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AQuotedPathLosesItsQuotes()
    {
        var path = TempCsv();

        try
        {
            var outcome = Run($"y\ncsv\n\"{path}\"\n", out _);

            Assert.Equal(path, outcome.Source!.Location);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Cancelling is NOT the same as having no data: a user who backs out must not be
    /// handed a Wikipedia build they never asked for.</summary>
    [Theory]
    [InlineData("y\nc\n")]
    [InlineData("y\ncancel\n")]
    [InlineData("y\ncsv\nc\n")]
    public void Cancelling_IsDistinctFromHavingNoData(string input)
    {
        var outcome = Run(input, out _);

        Assert.Equal(InterviewDecision.Cancelled, outcome.Decision);
        Assert.Null(outcome.Source);
    }

    /// <summary>Closed stdin must not busy-loop at any of the three prompts.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("y\n")]
    [InlineData("y\ncsv\n")]
    public void ClosedInput_Cancels(string input)
        => Assert.Equal(InterviewDecision.Cancelled, Run(input, out _).Decision);
}
