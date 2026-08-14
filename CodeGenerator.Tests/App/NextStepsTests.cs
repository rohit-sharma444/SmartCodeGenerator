using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// What the CLI tells the user to do with the app it just wrote. Every target has to say
/// something useful: a generated project the user cannot work out how to start is a folder,
/// not an app.
/// </summary>
[TestClass]
public class NextStepsTests
{
    private const string ProjectPath = @"D:\Work\Shop";

    private static readonly string[] NoFiles = [];

    private static string[] SeedFiles =>
        [Path.Combine(ProjectPath, "Data", "Seed", "Order.json")];

    private static string Steps(
        string key, string? testProjectPath = null, IReadOnlyList<string>? filesWritten = null)
    {
        Assert.IsTrue(TargetScaffolders.TryResolve(key, out var target), $"no target '{key}'");

        return string.Join(
            "\n", NextSteps.Lines(target, ProjectPath, testProjectPath, filesWritten ?? NoFiles));
    }

    [TestMethod]
    public void EveryTargetSaysSomethingBeyondTheHeading()
    {
        foreach (var key in TargetScaffolders.Keys)
        {
            var lines = NextSteps.Lines(
                TargetScaffolders.ByKey[key], ProjectPath, null, NoFiles);

            Assert.IsGreaterThan(1, lines.Count, $"'{key}' offered no next step");
            Assert.Contains(ProjectPath, string.Join("\n", lines), $"'{key}' never named the path");
        }
    }

    [TestMethod]
    public void RunnableTargets_GiveTheExactDotnetRunLine()
    {
        foreach (var key in new[] { "wpf", "winforms", "console", "webapi", "worker" })
            Assert.Contains($@"dotnet run --project {ProjectPath}", Steps(key), $"'{key}'");
    }

    [TestMethod]
    public void ClassLibrary_IsNotOfferedAsSomethingToRun()
    {
        // `dotnet run` on a class library fails with MSB4057. Printing it as a next step
        // sends the user to an error we already know they will hit.
        var steps = Steps("classlib");

        Assert.DoesNotContain("dotnet run", steps);
        Assert.Contains("reference", steps.ToLowerInvariant());
    }

    [TestMethod]
    public void WebApi_SaysWhereToFindTheUrl()
    {
        // The port is chosen at startup, so it cannot be printed here — but a user staring at
        // a running API with no address is stuck for no reason.
        Assert.Contains("url", Steps("webapi").ToLowerInvariant());
        Assert.DoesNotContain("url", Steps("console").ToLowerInvariant());
    }

    [TestMethod]
    public void SeedData_IsMentionedOnlyWhenSomeWasWritten()
    {
        // `noseed` with no imported rows writes no Data/Seed folder at all, so pointing at
        // one would send the user to a path that does not exist.
        Assert.Contains(Path.Combine(ProjectPath, "Data", "Seed"), Steps("console", filesWritten: SeedFiles));
        Assert.DoesNotContain("Seed", Steps("console"));
    }

    [TestMethod]
    public void TestProject_IsMentionedOnlyWhenOneWasGenerated()
    {
        Assert.Contains(@"dotnet test D:\Work\Shop.Tests", Steps("console", @"D:\Work\Shop.Tests"));
        Assert.DoesNotContain("dotnet test", Steps("console"));
    }
}
