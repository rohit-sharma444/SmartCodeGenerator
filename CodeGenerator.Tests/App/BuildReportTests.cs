using CodeGenerationEngine.Helper;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The report shown after the generated app is compiled. Pure, so the console loop that
/// prints it holds no decisions — the pattern <c>StoreStatusReport</c> set.
/// </summary>
[TestClass]
public class BuildReportTests
{
    private const string ProjectPath = @"D:\Work\Shop";

    private static ProcessResult Failed(string stdout)
        => new(1, stdout, string.Empty, TimedOut: false);

    private static string Report(ProcessResult result, double seconds = 4.1)
        => string.Join("\n", BuildReport.Lines(result, TimeSpan.FromSeconds(seconds), ProjectPath));

    [TestMethod]
    public void Succeeded_IsExitZeroAndNoTimeout()
    {
        Assert.IsTrue(BuildReport.Succeeded(new ProcessResult(0, "", "", TimedOut: false)));
        Assert.IsFalse(BuildReport.Succeeded(new ProcessResult(1, "", "", TimedOut: false)));

        // A killed build can still exit 0 on some shells; the flag is what decides.
        Assert.IsFalse(BuildReport.Succeeded(new ProcessResult(0, "", "", TimedOut: true)));
    }

    [TestMethod]
    public void Success_ReportsTheElapsedTimeAndNothingElse()
    {
        var report = Report(new ProcessResult(0, "Build succeeded.", "", TimedOut: false));

        Assert.Contains("4.1s", report);
        Assert.Contains("succeeded", report);

        // No rerun advice on a green build — there is nothing to rerun.
        Assert.DoesNotContain("dotnet build", report);
    }

    [TestMethod]
    public void Elapsed_SwitchesToMinutesPastSixtySeconds()
    {
        var green = new ProcessResult(0, "", "", TimedOut: false);

        Assert.Contains("59.0s", Report(green, 59));
        Assert.Contains("1m 32s", Report(green, 92));
    }

    [TestMethod]
    public void Failure_ListsTheErrorsAndHowToRerun()
    {
        var report = Report(Failed(
            "  Determining projects to restore...\n"
            + @"D:\Work\Shop\Models\Order.cs(14,20): error CS0246: The type 'Custmer' could not be found [D:\Work\Shop\Shop.csproj]"
            + "\nBuild FAILED.\n"));

        Assert.Contains("FAILED", report);
        Assert.Contains("CS0246", report);
        Assert.Contains(@"dotnet build D:\Work\Shop", report);

        // The restore chatter is not an error and must not be quoted as one.
        Assert.DoesNotContain("Determining projects", report);
    }

    [TestMethod]
    public void Failure_StripsTheProjectDirectoryAndTheTrailingCsprojNote()
    {
        var report = Report(Failed(
            @"D:\Work\Shop\Models\Order.cs(14,20): error CS0246: nope [D:\Work\Shop\Shop.csproj]"));

        // The user knows which project they just generated; the absolute prefix and the
        // repeated .csproj only push the part that matters off the edge of the screen.
        Assert.Contains(@"Models\Order.cs(14,20)", report);
        Assert.DoesNotContain(@"D:\Work\Shop\Models", report);
        Assert.DoesNotContain("Shop.csproj", report);
    }

    [TestMethod]
    public void Failure_CountsAndShowsEachErrorOnlyOnce()
    {
        // MSBuild genuinely repeats an error once per referencing project. Reporting
        // "2 errors" for one mistake would send the user hunting for a second.
        var duplicated = @"D:\Work\Shop\Models\Order.cs(14,20): error CS0246: nope [D:\Work\Shop\Shop.csproj]";

        var lines = BuildReport.Lines(
            Failed(duplicated + "\n" + duplicated), TimeSpan.FromSeconds(1), ProjectPath);

        Assert.HasCount(1, lines.Where(l => l.Contains("CS0246")));
        Assert.Contains("1 error", string.Join("\n", lines));
    }

    [TestMethod]
    public void Failure_CapsTheListAndSaysHowManyItWithheld()
    {
        var many = Enumerable.Range(1, BuildReport.MaxErrorsShown + 3)
            .Select(i => $@"D:\Work\Shop\F{i}.cs(1,1): error CS0103: missing {i} [D:\Work\Shop\Shop.csproj]");

        var lines = BuildReport.Lines(
            Failed(string.Join("\n", many)), TimeSpan.FromSeconds(1), ProjectPath);

        Assert.HasCount(BuildReport.MaxErrorsShown, lines.Where(l => l.Contains("error CS")));

        var report = string.Join("\n", lines);
        Assert.Contains($"{BuildReport.MaxErrorsShown + 3} errors", report);
        Assert.Contains("3 more", report);
    }

    [TestMethod]
    public void Failure_ReadsErrorsFromStandardErrorToo()
    {
        var result = new ProcessResult(
            1, string.Empty,
            @"D:\Work\Shop\Program.cs(3,1): error CS1002: ; expected [D:\Work\Shop\Shop.csproj]",
            TimedOut: false);

        Assert.Contains("CS1002", Report(result));
    }

    [TestMethod]
    public void Failure_WithNoRecognizableErrorStillSaysSoAndHowToRerun()
    {
        // An MSBuild crash, a locked file, a missing SDK: exit code non-zero with nothing
        // matching the error pattern. Silence here would read as success.
        var report = Report(Failed("something went wrong in a way we do not parse"));

        Assert.Contains("FAILED", report);
        Assert.Contains(@"dotnet build D:\Work\Shop", report);
    }

    [TestMethod]
    public void Timeout_IsReportedAsItsOwnOutcome()
    {
        var report = Report(new ProcessResult(-1, "", "", TimedOut: true));

        // Not "FAILED": nothing is known to be wrong with the code, so telling the user their
        // app does not compile would be a lie.
        Assert.Contains("timed out", report);
        Assert.DoesNotContain("FAILED", report);
        Assert.Contains(@"dotnet build D:\Work\Shop", report);
    }
}
