using CodeGenerator.BuildFix;
using DomainDiscovery;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class BuildFixReportTests
{
    private const string BackupLine1 = "  The original of each edited file is kept under .codegen\\fix-backups";
    private const string BackupLine2 = "  (newest folder, then r1, r2 and r3 for each round).";

    /// <summary>
    /// <paramref name="kept"/> edits in round 1 stay; <paramref name="undone"/> edits in round 2 are reverted.
    /// </summary>
    private static (FixOutcome Outcome, string Root) Outcome(
        bool green, int remaining, int kept, int undone, params string[] notes)
    {
        var root = FixTestSupport.TempProject();
        var session = new FixSession(root);
        session.BeginRound(1);
        for (var i = 0; i < kept; i++)
            session.Write(FixTestSupport.Write(root, $"F{i}.cs", "a"), "b", "make the class partial");

        session.BeginRound(2);
        for (var i = 0; i < undone; i++)
            session.Write(FixTestSupport.Write(root, $"U{i}.cs", "a"), "b", "AI");
        session.RevertRound(2);

        var left = Enumerable.Range(1, remaining)
            .Select(i => new BuildDiagnostic(Path.Combine(root, "A.cs"), i, 1, "CS0103", "The name 'x' does not exist"))
            .ToList();

        return (new FixOutcome(green, 2, session.Edits, left, notes, FixTestSupport.Result("", green ? 0 : 1)), root);
    }

    private static void AreLines(string[] expected, IReadOnlyList<string> actual)
        => CollectionAssert.AreEqual(expected, actual.ToArray(), string.Join("\n", actual));

    [TestMethod]
    public void Options_AreYesFirst_SoYesIsTheDefault()
    {
        Assert.AreEqual("Yes, fix them", BuildFixReport.Options[0]);
        Assert.AreEqual("No, leave them", BuildFixReport.Options[1]);
        Assert.HasCount(2, BuildFixReport.Options);
        Assert.IsTrue(BuildFixReport.Accepted(BuildFixReport.DefaultIndex));
        Assert.IsTrue(BuildFixReport.Accepted(0));
        Assert.IsFalse(BuildFixReport.Accepted(1));
        Assert.IsFalse(BuildFixReport.Accepted(null));
    }

    [TestMethod]
    public void Title_CountsTheErrors_WithTheRightPlural()
    {
        Assert.AreEqual("The build failed with 1 error. Fix them?", BuildFixReport.Title(1));
        Assert.AreEqual("The build failed with 4 errors. Fix them?", BuildFixReport.Title(4));
    }

    [TestMethod]
    public void Lines_Green_SaysTheBuildSucceeded_AndWhatWasFixed()
    {
        var (outcome, root) = Outcome(true, 0, 2, 0, "The build is green after 1 round(s).");

        AreLines(
            [
                "The build fixed itself: it builds now (2 edits kept).",
                "  Edited F0.cs: make the class partial",
                "  Edited F1.cs: make the class partial",
                "  The build is green after 1 round(s).",
                BackupLine1,
                BackupLine2,
            ],
            BuildFixReport.Lines(outcome, root));
    }

    [TestMethod]
    public void Lines_Green_WithOneEdit_UsesTheSingular()
    {
        var (outcome, root) = Outcome(true, 0, 1, 0);

        Assert.AreEqual("The build fixed itself: it builds now (1 edit kept).", BuildFixReport.Lines(outcome, root)[0]);
    }

    [TestMethod]
    public void Lines_Green_WithNoEdits_SaysNothingWasNeeded_AndNoBackupLine()
    {
        var (outcome, root) = Outcome(true, 0, 0, 0);

        AreLines(["The build succeeds. No edits were needed."], BuildFixReport.Lines(outcome, root));
    }

    [TestMethod]
    public void Lines_PartlyFixed_SaysSoAndListsWhatRemains_NeverSuccess()
    {
        var (outcome, root) = Outcome(false, 2, 1, 1, "Ollama is not running, so only the fix rules ran.");

        AreLines(
            [
                "Partly fixed: 1 edit kept.",
                "The build still fails: 2 errors remain.",
                "  Edited F0.cs: make the class partial",
                "  1 other edit was undone.",
                "  A.cs(1,1): CS0103: The name 'x' does not exist",
                "  A.cs(2,1): CS0103: The name 'x' does not exist",
                "  Ollama is not running, so only the fix rules ran.",
                BackupLine1,
                BackupLine2,
            ],
            BuildFixReport.Lines(outcome, root));
    }

    [TestMethod]
    public void Lines_PartlyFixed_WithOneErrorLeft_UsesTheSingular()
    {
        var (outcome, root) = Outcome(false, 1, 3, 0);

        var lines = BuildFixReport.Lines(outcome, root);

        Assert.AreEqual("Partly fixed: 3 edits kept.", lines[0]);
        Assert.AreEqual("The build still fails: 1 error remains.", lines[1]);
    }

    [TestMethod]
    public void Lines_NothingFixable_SaysNothingWasFixed_AndHasNoBackupLine()
    {
        var (outcome, root) = Outcome(false, 1, 0, 0, "No local AI is set up, so only the fix rules ran.");

        AreLines(
            [
                "Nothing could be fixed.",
                "The build still fails: 1 error remains.",
                "  A.cs(1,1): CS0103: The name 'x' does not exist",
                "  No local AI is set up, so only the fix rules ran.",
            ],
            BuildFixReport.Lines(outcome, root));
    }

    [TestMethod]
    public void Lines_EverythingReverted_SaysNothingWasKept_AndStillPointsAtTheBackups()
    {
        var (outcome, root) = Outcome(false, 3, 0, 2, "Round 1 was reverted: the errors rose from 3 to 4.");

        AreLines(
            [
                "Nothing was kept: all 2 edits made were undone.",
                "The build still fails: 3 errors remain.",
                "  A.cs(1,1): CS0103: The name 'x' does not exist",
                "  A.cs(2,1): CS0103: The name 'x' does not exist",
                "  A.cs(3,1): CS0103: The name 'x' does not exist",
                "  Round 1 was reverted: the errors rose from 3 to 4.",
                BackupLine1,
                BackupLine2,
            ],
            BuildFixReport.Lines(outcome, root));
    }

    [TestMethod]
    public void Lines_EverythingReverted_WithOneEdit_UsesTheSingular()
    {
        var (outcome, root) = Outcome(false, 3, 0, 1);

        Assert.AreEqual("Nothing was kept: all 1 edit made was undone.", BuildFixReport.Lines(outcome, root)[0]);
    }

    [TestMethod]
    public void Lines_RebuildFailed_WithNoReadableErrors_SaysSo()
    {
        var (outcome, root) = Outcome(false, 0, 0, 0, "The build failed, but no compiler errors could be read.");

        AreLines(
            [
                "Nothing could be fixed.",
                "The build still fails: its errors could not be read.",
                "  The build failed, but no compiler errors could be read.",
            ],
            BuildFixReport.Lines(outcome, root));
    }

    [TestMethod]
    public void Lines_NeverClaimSuccess_UnlessGreen()
    {
        foreach (var (kept, undone) in new[] { (0, 0), (2, 0), (0, 2), (1, 1) })
        {
            var (outcome, root) = Outcome(false, 1, kept, undone);
            var text = string.Join("\n", BuildFixReport.Lines(outcome, root));

            Assert.DoesNotContain("builds now", text);
            Assert.DoesNotContain("fixed itself", text);
            Assert.DoesNotContain("succeeds", text);
            Assert.Contains("still fails", text);
        }
    }

    [TestMethod]
    public void Lines_CapsTheRemainingErrorsShown_AtTen()
    {
        var (outcome, root) = Outcome(false, 15, 0, 0);

        var lines = BuildFixReport.Lines(outcome, root);

        Assert.AreEqual(10, lines.Count(l => l.Contains("CS0103", StringComparison.Ordinal)));
        Assert.AreEqual("  ... and 5 more", lines[12]);
        Assert.IsFalse(lines.Any(l => l.Contains("A.cs(11,1)", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Lines_ExactlyTenErrors_HaveNoMoreLine()
    {
        var (outcome, root) = Outcome(false, 10, 0, 0);

        Assert.IsFalse(BuildFixReport.Lines(outcome, root).Any(l => l.Contains("more", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Lines_ShowRelativePathsOnly_NeverTheProjectFolder()
    {
        var (outcome, root) = Outcome(false, 2, 1, 1);
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "Other.cs");
        var withOutside = outcome with
        {
            Remaining =
            [
                new BuildDiagnostic(Path.Combine(root, "Models", "Customer.cs"), 7, 3, "CS0246", "Missing " + Path.Combine(root, "Models", "X.cs")),
                new BuildDiagnostic(outside, 1, 1, "CS0001", "other"),
                new BuildDiagnostic(string.Empty, 0, 0, "MSB4018", "no file"),
            ],
            Notes = [$"{Path.Combine(root, "Models", "Y.cs")} was not restored; check it by hand."],
        };

        var lines = BuildFixReport.Lines(withOutside, root);
        var text = string.Join("\n", lines);

        Assert.DoesNotContain(root, text);
        Assert.DoesNotContain(Path.GetTempPath(), text);
        Assert.Contains("  Models\\Customer.cs(7,3): CS0246: Missing Models\\X.cs", lines);
        Assert.Contains("  Other.cs(1,1): CS0001: other", lines);
        Assert.Contains("  MSB4018: no file", lines);
        Assert.Contains("  Models\\Y.cs was not restored; check it by hand.", lines);
    }

    [TestMethod]
    public void Lines_NameTheBackupFolder_ExactlyAsFixSessionMakesIt()
    {
        var root = FixTestSupport.TempProject();
        var session = new FixSession(root);
        session.BeginRound(1);
        session.Write(FixTestSupport.Write(root, "A.cs", "a"), "b", "rule");

        var backups = Path.Combine(root, ".codegen", "fix-backups");

        Assert.AreEqual(BuildFixReport.BackupLocation, Path.GetRelativePath(root, backups));
        Assert.IsTrue(Directory.Exists(Path.Combine(Directory.GetDirectories(backups).Single(), "r1")));
    }

    [TestMethod]
    public void Lines_AreShortAndPlain_EvenForLongMessagesNotesAndPaths()
    {
        var (outcome, root) = Outcome(false, 1, 1, 1);
        var longMsg = string.Join(" ", Enumerable.Repeat("The type or namespace name 'Customer' could not be found", 4));
        var longWord = new string('W', 200);
        var wide = outcome with
        {
            Remaining =
            [
                new BuildDiagnostic(Path.Combine(root, "Models", new string('d', 50), new string('f', 40) + ".cs"), 12, 5, "CS0246", longMsg),
                new BuildDiagnostic(string.Empty, 0, 0, "CS0001", longWord),
            ],
            Notes = [longMsg, "Round 1: rules fixed 2 error(s): make the class partial, remove a stale hook, and more."],
        };

        var lines = BuildFixReport.Lines(wide, root);

        Assert.IsGreaterThan(8, lines.Count);
        foreach (var line in lines)
        {
            Assert.IsLessThanOrEqualTo(79, line.Length, $"{line.Length} wide: {line}");
            Assert.IsTrue(line.All(c => c is >= ' ' and <= '~'), $"not plain ASCII: {line}");
            Assert.AreEqual(line.TrimEnd(), line);
        }

        // Nothing is lost by the wrapping.
        Assert.AreEqual(200, string.Concat(lines).Count(c => c == 'W'));
        Assert.Contains("could not be found The type or namespace", string.Join(" ", lines.Select(l => l.Trim())));
    }

    [TestMethod]
    public void Lines_Continuations_AreIndentedUnderTheirFirstLine()
    {
        var (outcome, root) = Outcome(false, 0, 0, 0);
        var wide = outcome with
        {
            Remaining = [new BuildDiagnostic(string.Empty, 0, 0, "CS0001", string.Join(" ", Enumerable.Repeat("word", 40)))],
        };

        var lines = BuildFixReport.Lines(wide, root);

        Assert.StartsWith("  CS0001: word", lines[2]);
        Assert.StartsWith("      word", lines[3]);
    }

    // ---- BuildFixer wiring (no Ollama, no dotnet) ----

    private sealed class FakeAi : IBuildFixAi, IDisposable
    {
        public bool Disposed { get; private set; }
        public int Asked { get; private set; }
        public bool IsAvailable() => true;
        public string? Propose(BuildDiagnostic d, string excerpt) { Asked++; return null; }
        public void Dispose() => Disposed = true;
    }

    [TestMethod]
    public void Fixer_Ollama_MakesTheAiFromTheLoadedConfig()
    {
        var config = new DiscoveryConfig { Source = DiscoveryConfig.OllamaSource, Model = "m1" };
        DiscoveryConfig? seen = null;
        var ai = new FakeAi();

        var made = new BuildFixer(() => config, c => { seen = c; return ai; }).CreateAi();

        Assert.AreSame(ai, made);
        Assert.AreSame(config, seen);
    }

    [TestMethod]
    public void Fixer_OllamaSource_IsMatchedIgnoringCase()
    {
        var made = new BuildFixer(() => new DiscoveryConfig { Source = "ollama" }, _ => new FakeAi()).CreateAi();

        Assert.IsNotNull(made);
    }

    [TestMethod]
    public void Fixer_OtherSource_MakesNoAi_AndNeverBuildsOne()
    {
        var calls = 0;
        var fixer = new BuildFixer(
            () => new DiscoveryConfig { Source = DiscoveryConfig.WikipediaSource }, _ => { calls++; return new FakeAi(); });

        Assert.IsNull(fixer.CreateAi());
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void Fixer_NullSource_MakesNoAi()
    {
        var made = new BuildFixer(() => new DiscoveryConfig { Source = null! }, _ => new FakeAi()).CreateAi();

        Assert.IsNull(made);
    }

    [TestMethod]
    public void Fixer_ConfigThatThrows_OrIsMissing_MakesNoAi()
    {
        Assert.IsNull(new BuildFixer(() => throw new InvalidOperationException("bad json"), _ => new FakeAi()).CreateAi());
        Assert.IsNull(new BuildFixer(() => null!, _ => new FakeAi()).CreateAi());
        Assert.IsNull(new BuildFixer(() => new DiscoveryConfig(), _ => throw new IOException("no client")).CreateAi());
    }

    [TestMethod]
    public void Fixer_TheRules_AreTheThree_InOrder()
    {
        var types = BuildFixer.Rules().Select(r => r.GetType()).ToArray();

        CollectionAssert.AreEqual(
            new[] { typeof(DuplicatePartialRule), typeof(StaleCustomHookRule), typeof(NoLoginAuthUsingRule) }, types);
    }

    private static string Project(out string file)
    {
        var root = FixTestSupport.TempProject();
        file = FixTestSupport.Write(root, "A.cs", "int a = 1;\n");
        return root;
    }

    [TestMethod]
    public void Fixer_Fix_GivesTheLoopTheAi_AndDisposesIt()
    {
        var root = Project(out var file);
        var ai = new FakeAi();
        var failed = FixTestSupport.Result($"{file}(1,1): error CS0103: The name 'x' does not exist");

        var outcome = new BuildFixer(() => new DiscoveryConfig(), _ => ai)
            .Fix(root, failed, () => throw new InvalidOperationException("must not rebuild"), _ => { });

        Assert.AreEqual(1, ai.Asked);
        Assert.IsTrue(ai.Disposed);
        Assert.IsFalse(outcome.Green);
        Assert.Contains("The AI had no usable fix for the remaining errors.", outcome.Notes);
    }

    [TestMethod]
    public void Fixer_Fix_WithoutAi_SaysOnlyRulesRan_AndDoesNotThrowWhenConfigThrows()
    {
        var root = Project(out var file);
        var failed = FixTestSupport.Result($"{file}(1,1): error CS0103: The name 'x' does not exist");

        var outcome = new BuildFixer(() => throw new IOException("locked"), _ => new FakeAi())
            .Fix(root, failed, () => throw new InvalidOperationException("must not rebuild"), _ => { });

        Assert.IsFalse(outcome.Green);
        Assert.Contains("No local AI is set up, so only the fix rules ran.", outcome.Notes);
        Assert.HasCount(0, outcome.Edits);
        Assert.AreEqual("int a = 1;\n", File.ReadAllText(file));
    }

    [TestMethod]
    public void Fixer_Fix_NeverThrows_EvenWhenTheLoopCannotStart()
    {
        var ai = new FakeAi();
        var failed = FixTestSupport.Result("boom");

        var outcome = new BuildFixer(() => new DiscoveryConfig(), _ => ai)
            .Fix("\0bad", failed, () => FixTestSupport.Result("", 0), _ => { });

        Assert.IsFalse(outcome.Green);
        Assert.StartsWith("The fix stopped (", outcome.Notes[0]);
        Assert.Contains(BuildFixReport.BackupLocation, outcome.Notes[0]);
        Assert.IsTrue(ai.Disposed);
    }
}
