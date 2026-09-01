using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests;

/// <summary>
/// The interview asked as drop-downs. The chooser arrives as a delegate because this library
/// cannot see the CLI that paints one — the same reason the application-type menu is passed in.
/// </summary>
public class DataSourceInterviewMenuTests
{
    private const int Keep = -1;

    private sealed class FakeChooser(params int?[] answers)
    {
        private readonly Queue<int?> _answers = new(answers);

        public List<(string Title, IReadOnlyList<string> Labels, int Default)> Asked { get; } = [];

        public int? Choose(string title, IReadOnlyList<string> labels, int defaultIndex)
        {
            Asked.Add((title, labels, defaultIndex));

            Assert.True(_answers.Count > 0, $"unscripted question: {title}");

            var answer = _answers.Dequeue();

            return answer == Keep ? defaultIndex : answer;
        }
    }

    private static string TempCsv()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, "Id,Name\n1,Ada\n");
        return path;
    }

    private static (InterviewOutcome Outcome, FakeChooser Chooser, string Written) Run(
        string typed, params int?[] answers)
    {
        var chooser = new FakeChooser(answers);
        var writer = new StringWriter();

        var outcome = DataSourceInterview.Resolve(
            new StringReader(typed), writer, chooser.Choose);

        return (outcome, chooser, writer.ToString());
    }

    /// <summary>The row for a format, by its key.</summary>
    private static int Row(string key) => DataSourceReaders.Keys.ToList().IndexOf(key);

    [Fact]
    public void TheGate_IsAYesNoMenu_DefaultingToNo()
    {
        var (outcome, chooser, _) = Run(string.Empty, 1);

        var gate = chooser.Asked[0];

        Assert.Equal("Do you have existing data?", gate.Title);
        Assert.Equal(new[] { "Yes", "No" }, gate.Labels);

        // The same answer a blank line gave the typed prompt.
        Assert.Equal(1, gate.Default);
        Assert.Equal(InterviewDecision.NoData, outcome.Decision);
    }

    [Fact]
    public void EscapeAtTheGate_Cancels()
    {
        // Bracketed: a lone `null` argument would be the whole params array, not one answer.
        var (outcome, _, _) = Run(string.Empty, [null]);

        // Deliberately not NoData: someone who backs out must not be handed a Wikipedia build
        // they never asked for.
        Assert.Equal(InterviewDecision.Cancelled, outcome.Decision);
    }

    [Fact]
    public void TheFormatMenu_OffersEveryFormat_AndDefaultsToOneThatWorks()
    {
        var path = TempCsv();

        try
        {
            var (outcome, chooser, _) = Run(path + "\n", 0, Keep);

            var format = chooser.Asked[1];

            Assert.Equal("Format", format.Title);
            Assert.Equal(DataSourceReaders.Keys.Count, format.Labels.Count);

            // csv rather than the first row: `sql` heads the catalogue and is the one format this
            // build cannot read, so pre-selecting it would make Enter the wrong answer.
            Assert.Equal(Row("csv"), format.Default);

            Assert.Equal(InterviewDecision.Source, outcome.Decision);
            Assert.Equal(DataSourceKind.Csv, outcome.Source!.Kind);
            Assert.Equal(path, outcome.Source.Location);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// SQL is offered plainly now that it has a reader. This asserted the opposite until the
    /// SQL Server branch, and the row is built by FormatLabel, which appends the caveat only
    /// for a kind TryCreate cannot build - so a bare row is the evidence the reader is wired
    /// all the way to the menu.
    /// </summary>
    [Fact]
    public void EveryFormatIsOfferedWithoutACaveat()
    {
        var (_, chooser, _) = Run(string.Empty, 0, null);

        var labels = chooser.Asked[1].Labels;

        Assert.Equal("sql", labels[Row("sql")]);
        Assert.DoesNotContain(labels, label => label.Contains("not in this build"));
    }

    /// <summary>
    /// Choosing SQL now goes straight on to the location question instead of being refused
    /// and re-asked. What replaces the old assertion is that the format menu is asked ONCE:
    /// a second Format round would mean the refusal path had fired.
    /// </summary>
    [Fact]
    public void ChoosingSqlGoesStraightOnToTheLocation()
    {
        var (outcome, chooser, written) = Run(
            "Server=.;Database=Northwind;Trusted_Connection=True\n", 0, Row("sql"));

        Assert.DoesNotContain("not in this build", written);

        // The gate, then Format once. A third round would be the refusal re-asking.
        Assert.Equal(2, chooser.Asked.Count);
        Assert.Equal(DataSourceKind.Sql, outcome.Source!.Kind);
    }

    [Fact]
    public void EscapeAtTheFormatMenu_Cancels()
    {
        var (outcome, _, _) = Run(string.Empty, 0, null);

        Assert.Equal(InterviewDecision.Cancelled, outcome.Decision);
    }

    [Fact]
    public void ThePath_IsStillTyped_AndAMissingOneIsAskedAgain()
    {
        var path = TempCsv();

        try
        {
            // A path is not a choice, so it stays a prompt — and a mistyped one re-asks rather
            // than abandoning the interview.
            var (outcome, _, written) = Run("nowhere.csv\n" + path + "\n", 0, Row("csv"));

            Assert.Contains("No file at nowhere.csv", written);
            Assert.Equal(path, outcome.Source!.Location);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WithNoChooser_TheTypedPromptsAreUnchanged()
    {
        var writer = new StringWriter();

        var outcome = DataSourceInterview.Resolve(new StringReader("n\n"), writer);

        Assert.Equal(InterviewDecision.NoData, outcome.Decision);
        Assert.Contains("[y/N]", writer.ToString());
    }
}
