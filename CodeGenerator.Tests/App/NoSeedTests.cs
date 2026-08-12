using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>SeedData.EnsureSeeded() is called from four entry points and the generated tests, so
/// dropping SeedData.cs means dropping all of them.</summary>
[TestClass]
public class NoSeedTests
{
    private static readonly AppCustomization NoSeed =
        AppCustomization.Default with { SampleSeedData = false };

    private static string ProgramOf(ITargetScaffolder target, AppCustomization customization)
        => target.FrontEndFiles("Shop", ["Order"], customization)
            .Single(f => f.Name == "Program.cs").Source;

    [TestMethod]
    public void The_console_entry_point_still_seeds_by_default()
        => StringAssert.Contains(
            ProgramOf(new ConsoleTarget(), AppCustomization.Default), "SeedData.EnsureSeeded();");

    [TestMethod]
    public void No_entry_point_seeds_when_seeding_is_off()
    {
        foreach (ITargetScaffolder target in new ITargetScaffolder[]
                 { new ConsoleTarget(), new WebApiTarget(), new WorkerTarget(), new WpfTarget() })
        {
            foreach (var file in target.FrontEndFiles("Shop", ["Order"], NoSeed))
            {
                Assert.IsFalse(
                    file.Source.Contains("SeedData.EnsureSeeded", StringComparison.Ordinal),
                    $"{target.Key}/{file.Name} still seeds.");
            }
        }
    }

    /// <summary>The desktop pair reach MainViewModel through DesktopViewModelFiles, so they are
    /// the ones a flag is easiest to forget to thread — and MainViewModel is where seeding has to
    /// happen before the first EntityViewModel reads its store.</summary>
    [TestMethod]
    public void The_desktop_view_model_still_seeds_by_default()
        => StringAssert.Contains(
            new WpfTarget().FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
                .Single(f => f.Name == "MainViewModel.cs").Source,
            "SeedData.EnsureSeeded();");

    /// <summary>The interaction the spec calls out: imported rows arrive THROUGH SeedData, so a
    /// data-driven build keeps it. `noseed` suppresses sample rows, not the user's own data.</summary>
    [TestMethod]
    public void Imported_data_still_produces_a_seed_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "seed-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            var request = ScaffoldRequest.For(
                    TestModels.OneEntity(), "Shop", root, new ConsoleTarget())
                with
                {
                    Customization = NoSeed,
                    ImportedData = TestModels.OneImportedRow("Order"),
                };

            var result = new AppScaffolder().Scaffold(request);

            Assert.IsTrue(
                File.Exists(Path.Combine(result.ProjectPath, "Data", "SeedData.cs")),
                "Imported rows are loaded by SeedData, so it must survive `noseed`.");

            // Keeping the file but dropping the call would leave the user's imported rows sitting
            // in Data/Seed/*.json and never loaded — the worst of both outcomes.
            StringAssert.Contains(
                File.ReadAllText(Path.Combine(result.ProjectPath, "Program.cs")),
                "SeedData.EnsureSeeded();");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    [TestMethod]
    public void A_seedless_app_has_no_seed_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "seed-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            var request = ScaffoldRequest.For(
                    TestModels.OneEntity(), "Shop", root, new ConsoleTarget())
                with { Customization = NoSeed };

            var result = new AppScaffolder().Scaffold(request);

            Assert.IsFalse(File.Exists(Path.Combine(result.ProjectPath, "Data", "SeedData.cs")));

            // The generated suite's seed test would not compile without it.
            Assert.IsNotNull(result.TestProjectPath);
            Assert.IsFalse(File.Exists(Path.Combine(
                Path.GetDirectoryName(result.TestProjectPath)!, "SeedDataTests.cs")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// As in <see cref="NoValidationTests"/>: the seeding call is a line lifted out of a raw-string
    /// template, and rebuilding it with "\n" would leave the default output one CR per line short
    /// on a CRLF checkout — a difference no substring assertion can see.
    /// </summary>
    /// <remarks>
    /// One entity on purpose. These templates join their per-entity registrations with "\n", which
    /// has always mixed the endings of a multi-entity app; with a single entity there is no
    /// separator to emit, so what is left is the seeding fragment alone.
    /// </remarks>
    [TestMethod]
    public void The_seed_branch_leaves_the_line_endings_alone()
    {
        string[] sources =
        [
            ProgramOf(new ConsoleTarget(), AppCustomization.Default),
            ProgramOf(new ConsoleTarget(), NoSeed),
            ProgramOf(new WebApiTarget(), AppCustomization.Default),
            ProgramOf(new WebApiTarget(), NoSeed),
            ProgramOf(new WorkerTarget(), AppCustomization.Default),
            ProgramOf(new WorkerTarget(), NoSeed),
        ];

        foreach (var source in sources)
        {
            var newlines = source.Count(c => c == '\n');
            var carriageReturns = source.Count(c => c == '\r');

            Assert.IsTrue(
                carriageReturns == 0 || carriageReturns == newlines,
                $"mixed line endings: {newlines} newline(s), {carriageReturns} carriage return(s)");
        }
    }
}
