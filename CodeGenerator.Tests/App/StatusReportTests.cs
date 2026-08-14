using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The full system report. Everything startup stopped printing has to be reachable here, or
/// going quiet at startup would mean losing it rather than moving it.
/// </summary>
[TestClass]
public class StatusReportTests
{
    private static readonly string[] StoreLines =
        ["Wikipedia store: dump 20260812. (Updater service: running — idle.)"];

    private static string Report(
        int projects = 3,
        string? active = "Shop",
        int rules = 0,
        IReadOnlyList<string>? store = null,
        string ai = "enabled (retrieval)")
        => string.Join("\n", StatusReport.Lines(
            @"D:\Work", projects, active, rules, store ?? StoreLines, ai, "1.0.0", "on"));

    [TestMethod]
    public void ReportsTheWorkspaceRoot()
    {
        // The single most common confusion in this tool is files landing somewhere unexpected
        // because PROJECTPATH is set. `status` is where that question gets answered.
        Assert.Contains(@"D:\Work", Report());
    }

    [TestMethod]
    public void ReportsTheProjectCountAndWhichIsActive()
    {
        var report = Report(projects: 3, active: "Shop");

        Assert.Contains("3", report);
        Assert.Contains("Shop", report);
    }

    [TestMethod]
    public void SaysSoWhenThereAreNoProjects()
    {
        // Not a blank or a bare "0" — a new user in an empty folder should be told this is normal.
        var report = Report(projects: 0, active: null);

        Assert.Contains("none", report.ToLowerInvariant());
    }

    [TestMethod]
    public void ReportsTheStoreLinesVerbatim()
    {
        // Passed in rather than rebuilt, so `status` and the startup warning cannot disagree
        // about what the store is doing.
        foreach (var line in StoreLines)
            Assert.Contains(line, Report());
    }

    [TestMethod]
    public void ReportsTheAiState()
    {
        Assert.Contains("enabled (retrieval)", Report());
        Assert.Contains("off (disabled)", Report(ai: "off (disabled)"));
    }

    [TestMethod]
    public void MentionsRulesOnlyWhenSomeAreLoaded()
    {
        // rules.json is rare; a "0 rules" line would be noise in the report for everyone else.
        Assert.Contains("2", Report(rules: 2));
        Assert.DoesNotContain("rule", Report(rules: 0).ToLowerInvariant());
    }

    [TestMethod]
    public void ReportsTheVersion()
    {
        // The first thing anyone needs when describing a problem to someone else.
        Assert.Contains("1.0.0", Report());
    }

    [TestMethod]
    public void ReportsWhetherColourIsOn()
    {
        // "I can't see coloured text" has to be answerable without reading the source.
        Assert.Contains("Colour", Report());
    }

    [TestMethod]
    public void FitsOnAScreenAndDoesNotWrap()
    {
        var lines = StatusReport.Lines(
            @"D:\Work", 3, "Shop", 2, StoreLines, "enabled (retrieval)", "1.0.0", "on");

        Assert.IsLessThan(20, lines.Count);

        foreach (var line in lines)
            Assert.IsLessThan(81, line.TrimEnd().Length, $"wraps at 80: {line}");
    }
}
