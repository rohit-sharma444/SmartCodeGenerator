using DataIngestion.Normalization;
using Xunit;

namespace DataIngestion.Tests.Normalization;

public class NormalizationPromptTests
{
    private static NormalizationFinding Finding(string headline, string proposal, string evidence)
        => new()
        {
            Kind = NormalizationFindingKind.RepeatingGroup,
            TableName = "Orders",
            SourceLabel = "orders.csv",
            Columns = ["Item1", "Item2"],
            ProposedEntity = "OrderItem",
            Headline = headline,
            Proposal = proposal,
            Evidence = [evidence],
        };

    private static NormalizationReport Report(params NormalizationFinding[] findings)
        => new(findings, []);

    private static NormalizationReport ThreeFindings() => Report(
        Finding("Repeating group: Item1, Item2", "split into OrderItem", "Nut | Bolt"),
        Finding("Partial dependency: CustomerName, CustomerCity", "split into Customer", "Ada -> London"),
        Finding("Non-atomic: \"Tags\"", "split into Tag", "rush appears 4 times"));

    [Fact]
    public void Lines_NumberEachFindingAndShowItsProposal()
    {
        var text = string.Join("\n", NormalizationPrompt.Lines(ThreeFindings(), "Orders.csv", 500));

        Assert.Contains("Normalization check on Orders.csv (500 rows)", text);
        Assert.Contains("1 ! Repeating group: Item1, Item2", text);
        Assert.Contains("-> split into OrderItem", text);
        Assert.Contains("3 ! Non-atomic:", text);
    }

    /// <summary>Evidence belongs behind `d`, not in the summary -- the summary has to stay
    /// scannable.</summary>
    [Fact]
    public void Lines_DoNotIncludeTheEvidence()
        => Assert.DoesNotContain("Nut | Bolt",
            string.Join("\n", NormalizationPrompt.Lines(ThreeFindings(), "Orders.csv", 500)));

    /// <summary>A skipped check must say so. Printing nothing would read as "no problems found",
    /// which is a different claim.</summary>
    [Fact]
    public void Lines_ShowTheNotes()
    {
        var report = new NormalizationReport([], [NormalizationAnalyzer.TooFewRowsNote]);

        Assert.Contains(NormalizationAnalyzer.TooFewRowsNote,
            string.Join("\n", NormalizationPrompt.Lines(report, "Orders.csv", 3)));
    }

    [Fact]
    public void Lines_SayNothingWasFoundWhenNothingWas()
        => Assert.Contains("No normalization problems found",
            string.Join("\n", NormalizationPrompt.Lines(new NormalizationReport([], []), "t.csv", 9)));

    [Fact]
    public void N_AppliesEveryFinding()
    {
        var report = ThreeFindings();

        var chosen = NormalizationPrompt.Choose(report, new StringReader("n\n"), new StringWriter());

        Assert.Equal(3, chosen.Count);
    }

    [Fact]
    public void N_WithNumbers_AppliesOnlyThose()
    {
        var report = ThreeFindings();

        var chosen = NormalizationPrompt.Choose(report, new StringReader("n 1 3\n"), new StringWriter());

        Assert.Equal(2, chosen.Count);
        Assert.Same(report.Findings[0], chosen[0]);
        Assert.Same(report.Findings[2], chosen[1]);
    }

    [Fact]
    public void K_AppliesNothing()
        => Assert.Empty(NormalizationPrompt.Choose(
            ThreeFindings(), new StringReader("k\n"), new StringWriter()));

    /// <summary>A finding the user cannot inspect is a finding they cannot trust, so `d` prints the
    /// rows behind it and then asks again.</summary>
    [Fact]
    public void D_PrintsThatFindingsEvidenceThenAsksAgain()
    {
        var writer = new StringWriter();

        var chosen = NormalizationPrompt.Choose(
            ThreeFindings(), new StringReader("d 2\nk\n"), writer);

        Assert.Contains("Ada -> London", writer.ToString());
        Assert.DoesNotContain("Nut | Bolt", writer.ToString());
        Assert.Empty(chosen);
    }

    [Theory]
    [InlineData("d 9")]
    [InlineData("d 0")]
    [InlineData("d x")]
    [InlineData("d")]
    public void D_WithABadNumber_SaysSoAndAsksAgain(string line)
    {
        var writer = new StringWriter();

        NormalizationPrompt.Choose(ThreeFindings(), new StringReader(line + "\nk\n"), writer);

        Assert.Contains("no finding", writer.ToString());
    }

    [Fact]
    public void UnrecognizedInput_SaysSoAndAsksAgain()
    {
        var writer = new StringWriter();

        var chosen = NormalizationPrompt.Choose(
            ThreeFindings(), new StringReader("what?\nn\n"), writer);

        Assert.Contains("Unrecognized", writer.ToString());
        Assert.Equal(3, chosen.Count);
    }

    /// <summary>An out-of-range number in `n` is reported rather than silently ignored, so the
    /// user is never told a split was applied that was not.</summary>
    [Fact]
    public void N_WithAnOutOfRangeNumber_SaysSoAndAsksAgain()
    {
        var writer = new StringWriter();

        NormalizationPrompt.Choose(ThreeFindings(), new StringReader("n 1 9\nk\n"), writer);

        Assert.Contains("no finding", writer.ToString());
    }

    /// <summary>Closed stdin must not busy-loop. The review loop already treats EOF as cancel;
    /// here the safe reading is "change nothing".</summary>
    [Fact]
    public void ClosedInput_KeepsTheDataAsItIs()
        => Assert.Empty(NormalizationPrompt.Choose(
            ThreeFindings(), TextReader.Null, new StringWriter()));

    /// <summary>With nothing to decide there is nothing to ask.</summary>
    [Fact]
    public void NoFindings_NeverPrompts()
    {
        var reader = new StringReader("n\n");

        Assert.Empty(NormalizationPrompt.Choose(
            new NormalizationReport([], []), reader, new StringWriter()));

        // The reader was not consumed, so the next thing that reads stdin still sees "n".
        Assert.Equal("n", reader.ReadLine());
    }
}
