using System.ComponentModel;
using System.Text;
using CodeGenerationEngine.Helper;
using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class BuildFixLoopTests
{
    private static string Err(string file, int line, string code, string msg = "msg")
        => $"{file}({line},1): error {code}: {msg}";

    private static readonly ProcessResult Green = new(0, "Build succeeded.", "", false);

    /// <summary>A rule that fixes CS0001 by deleting the marker "BAD" from the file.</summary>
    private sealed class StripBad : IBuildFixRule
    {
        public string Name => "strip";

        public bool Fix(BuildDiagnostic d, FixSession s)
        {
            if (d.Code != "CS0001") return false;
            var text = s.Read(d.File);
            if (!text.Contains("BAD")) return false;
            s.Write(d.File, text.Replace("BAD", ""), Name);
            return true;
        }
    }

    /// <summary>A rule that edits the file and reports success, whatever the build then says.</summary>
    private sealed class Rewrite(string with) : IBuildFixRule
    {
        public string Name => "rewrite";

        public bool Fix(BuildDiagnostic d, FixSession s)
        {
            if (d.Code != "CS0001") return false;
            s.Write(d.File, with, Name);
            return true;
        }
    }

    /// <summary>A rule that writes, then fails.</summary>
    private sealed class WriteThenThrow : IBuildFixRule
    {
        public string Name => "boom";

        public bool Fix(BuildDiagnostic d, FixSession s)
        {
            s.Write(d.File, "half done", Name);
            throw new NotSupportedException("boom");
        }
    }

    private sealed class FakeAi(Func<BuildDiagnostic, string, string?> propose, bool up = true) : IBuildFixAi
    {
        public int Asks;
        public bool IsAvailable() => up;
        public string? Propose(BuildDiagnostic d, string excerpt) { Asks++; return propose(d, excerpt); }
    }

    private sealed class OutsideWriter(string outside) : IBuildFixRule
    {
        public string Name => "bad";
        public bool Fix(BuildDiagnostic d, FixSession s) { s.Write(outside, "x", Name); return true; }
    }

    private static byte[] Bytes(string path) => File.ReadAllBytes(path);

    private sealed class FakeClock
    {
        public DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public Func<DateTime> Read => () => Now;
    }

    /// <summary>A model whose chosen asks time out: it says so the way the real client does.</summary>
    private sealed class TimeoutAi(Func<BuildDiagnostic, string, string?> propose, Func<BuildDiagnostic, bool> timesOut) : IBuildFixAi
    {
        public int Asks;
        public bool LastTimedOut { get; private set; }
        public bool IsAvailable() => true;

        public string? Propose(BuildDiagnostic d, string excerpt)
        {
            Asks++;
            LastTimedOut = timesOut(d);

            return LastTimedOut ? null : propose(d, excerpt);
        }
    }

    /// <summary>A rule that edits the file, then lets the given action run (the clock moves, the user cancels).</summary>
    private sealed class EditThen(Action then) : IBuildFixRule
    {
        public string Name => "edit-then";

        public bool Fix(BuildDiagnostic d, FixSession s)
        {
            if (d.Code != "CS0001") return false;
            s.Write(d.File, "int edited;\n", Name);
            then();
            return true;
        }
    }

    [TestMethod]
    public void Run_TheDefaultTimeBudget_IsTenMinutes()
    {
        Assert.AreEqual(TimeSpan.FromMinutes(10), BuildFixLoop.DefaultBudget);
    }

    [TestMethod]
    public void Run_StopsBeforeTheNextRound_WhenTheTimeLimitPassed_AndKeepsTheCheckedRound()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int BAD;\nint BAD;\n");
        var red = FixTestSupport.Result(Err(file, 1, "CS0001") + "\n" + Err(file, 2, "CS0001"));
        var fewer = FixTestSupport.Result(Err(file, 1, "CS0002"));
        var clock = new FakeClock();
        var rebuilds = 0;

        var outcome = new BuildFixLoop([new StripBad()], null, clock: clock.Read).Run(root, red, () =>
        {
            rebuilds++;
            clock.Now += TimeSpan.FromMinutes(11);
            return fewer;
        });

        Assert.AreEqual(1, rebuilds);
        Assert.AreEqual(1, outcome.Rounds);
        Assert.IsFalse(outcome.Green);
        Assert.Contains(BuildFixLoop.TimeLimitNote, outcome.Notes);
        Assert.IsFalse(outcome.Edits[0].Reverted, "the round was checked by a build, so it is kept");
        Assert.AreEqual("int ;\nint ;\n", File.ReadAllText(file));
        Assert.HasCount(1, outcome.Remaining);
    }

    [TestMethod]
    public void Run_StopsBeforeTheRebuild_WhenTheTimeLimitPassedDuringTheRound_AndUndoesTheRound()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int BAD;\n");
        var original = Bytes(file);
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));
        var clock = new FakeClock();

        var outcome = new BuildFixLoop(
            [new EditThen(() => clock.Now += TimeSpan.FromMinutes(11))], null, clock: clock.Read)
            .Run(root, red, () => throw new InvalidOperationException("must not rebuild"));

        CollectionAssert.AreEqual(original, Bytes(file));
        Assert.IsTrue(outcome.Edits.All(e => e.Reverted));
        Assert.Contains(BuildFixLoop.TimeLimitNote, outcome.Notes);
        Assert.Contains("Round 1 was undone because its edits were not checked.", outcome.Notes);
        Assert.IsFalse(outcome.Green);
        Assert.HasCount(1, outcome.Remaining);
    }

    [TestMethod]
    public void Run_StopsBeforeTheNextAiAsk_WhenTheTimeLimitPassed_AndUndoesTheRound()
    {
        var root = FixTestSupport.TempProject();
        var a = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var b = FixTestSupport.Write(root, "B.cs", "int b = 1\n");
        var original = Bytes(a);
        var red = FixTestSupport.Result(Err(a, 1, "CS1002") + "\n" + Err(b, 1, "CS1002"));
        var clock = new FakeClock();
        var ai = new FakeAi((d, ex) =>
        {
            clock.Now += TimeSpan.FromMinutes(11);
            return ex + ";";
        });

        var outcome = new BuildFixLoop([], ai, clock: clock.Read)
            .Run(root, red, () => throw new InvalidOperationException("must not rebuild"));

        Assert.AreEqual(1, ai.Asks);
        CollectionAssert.AreEqual(original, Bytes(a));
        Assert.IsTrue(outcome.Edits.All(e => e.Reverted));
        Assert.Contains(BuildFixLoop.TimeLimitNote, outcome.Notes);
        Assert.Contains("Round 1 was undone because its edits were not checked.", outcome.Notes);
    }

    [TestMethod]
    public void Run_AfterOneAiTimeout_NoFurtherAskIsMade_InThisRoundOrLater()
    {
        var root = FixTestSupport.TempProject();
        var a = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var b = FixTestSupport.Write(root, "B.cs", "int b = 1\n");
        var c = FixTestSupport.Write(root, "C.cs", "int c = 1\n");
        var red = FixTestSupport.Result(Err(a, 1, "CS1002") + "\n" + Err(b, 1, "CS1002") + "\n" + Err(c, 1, "CS1002"));
        var fewer = FixTestSupport.Result(Err(b, 1, "CS1002") + "\n" + Err(c, 1, "CS1002"));
        var ai = new TimeoutAi((d, ex) => ex + ";", d => d.File == b);

        var outcome = new BuildFixLoop([], ai).Run(root, red, () => fewer);

        Assert.AreEqual(2, ai.Asks, "A is answered, B times out, C is never asked, and round 2 asks nothing");
        Assert.Contains("The AI timed out earlier, so it was not asked again.", outcome.Notes);
        Assert.AreEqual(2, outcome.Rounds, "round 2 began but made no ask");
        Assert.IsFalse(outcome.Edits[0].Reverted);
    }

    [TestMethod]
    public void Run_AnAiTimeoutOnTheFirstAsk_SaysSo_AndAsksNoMore()
    {
        var root = FixTestSupport.TempProject();
        var a = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var b = FixTestSupport.Write(root, "B.cs", "int b = 1\n");
        var red = FixTestSupport.Result(Err(a, 1, "CS1002") + "\n" + Err(b, 1, "CS1002"));
        var ai = new TimeoutAi((d, ex) => ex + ";", d => true);

        var outcome = new BuildFixLoop([], ai).Run(root, red, () => throw new InvalidOperationException("must not rebuild"));

        Assert.AreEqual(1, ai.Asks);
        Assert.Contains("The AI timed out, so it will not be asked again.", outcome.Notes);
        Assert.HasCount(0, outcome.Edits);
    }

    [TestMethod]
    public void Run_ACancelledToken_StopsBeforeAnyRound()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int BAD;\n");
        var original = Bytes(file);
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var outcome = new BuildFixLoop([new StripBad()], null)
            .Run(root, red, () => throw new InvalidOperationException("must not rebuild"), null, cts.Token);

        Assert.AreEqual(0, outcome.Rounds);
        Assert.HasCount(0, outcome.Edits);
        Assert.Contains(BuildFixLoop.CancelledNote, outcome.Notes);
        CollectionAssert.AreEqual(original, Bytes(file));
        Assert.AreSame(red, outcome.LastBuild);
    }

    [TestMethod]
    public void Run_CancelDuringTheRound_UndoesItBeforeAnyRebuild()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int BAD;\n");
        var original = Bytes(file);
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));
        using var cts = new CancellationTokenSource();

        var outcome = new BuildFixLoop([new EditThen(cts.Cancel)], null)
            .Run(root, red, () => throw new InvalidOperationException("must not rebuild"), null, cts.Token);

        CollectionAssert.AreEqual(original, Bytes(file));
        Assert.IsTrue(outcome.Edits.All(e => e.Reverted));
        Assert.Contains(BuildFixLoop.CancelledNote, outcome.Notes);
        Assert.Contains("Round 1 was undone because its edits were not checked.", outcome.Notes);
    }

    [TestMethod]
    public void Run_CancelDuringTheRebuild_UndoesTheRound_WhenTheBuildFailed()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int BAD;\n");
        var original = Bytes(file);
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));
        using var cts = new CancellationTokenSource();

        var outcome = new BuildFixLoop([new StripBad()], null).Run(root, red, () =>
        {
            cts.Cancel();
            return FixTestSupport.Result(Err(file, 1, "CS0009"));
        }, null, cts.Token);

        CollectionAssert.AreEqual(original, Bytes(file));
        Assert.IsTrue(outcome.Edits.All(e => e.Reverted));
        Assert.Contains(BuildFixLoop.CancelledNote, outcome.Notes);
        Assert.AreSame(red, outcome.LastBuild);
        Assert.IsFalse(outcome.Green);
    }

    [TestMethod]
    public void Run_CancelDuringTheRebuild_KeepsTheRound_WhenTheBuildStillSucceeded()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int BAD;\n");
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));
        using var cts = new CancellationTokenSource();

        var outcome = new BuildFixLoop([new StripBad()], null).Run(root, red, () =>
        {
            cts.Cancel();
            return Green;
        }, null, cts.Token);

        Assert.IsTrue(outcome.Green);
        Assert.IsFalse(outcome.Edits[0].Reverted);
    }

    [TestMethod]
    public void Run_AFailedRevertAfterAWorseBuild_NeverClaimsTheRoundWasReverted()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int BAD;\n");
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));
        var worse = FixTestSupport.Result(Err(file, 1, "CS0002") + "\n" + Err(file, 1, "CS0003"));
        FileStream? held = null;
        FixOutcome outcome;

        try
        {
            outcome = new BuildFixLoop([new StripBad()], null).Run(root, red, () =>
            {
                held = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return worse;
            });
        }
        finally
        {
            held?.Dispose();
        }

        Assert.IsFalse(outcome.Notes.Any(n => n.Contains("was reverted") || n.Contains("was undone")), string.Join(" | ", outcome.Notes));
        Assert.IsTrue(outcome.Notes.Any(n => n == "Round 1 could not be fully undone: the errors rose from 1 to 2."), string.Join(" | ", outcome.Notes));
        Assert.IsTrue(outcome.Notes.Any(n => n.Contains("A.cs") && n.Contains("not restored")));
    }

    [TestMethod]
    public void Run_AFailedRevertAfterARebuildThatThrows_NeverClaimsTheRoundWasReverted()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int BAD;\n");
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));
        FileStream? held = null;
        FixOutcome outcome;

        try
        {
            outcome = new BuildFixLoop([new StripBad()], null).Run(root, red, () =>
            {
                held = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                throw new InvalidOperationException("x");
            });
        }
        finally
        {
            held?.Dispose();
        }

        Assert.IsFalse(outcome.Notes.Any(n => n.Contains("was reverted") || n.Contains("was undone")), string.Join(" | ", outcome.Notes));
        Assert.IsTrue(outcome.Notes.Any(n => n.Contains("round 1 could not be fully undone")), string.Join(" | ", outcome.Notes));
    }

    [TestMethod]
    public void Run_AppliesARule_Rebuilds_AndReportsGreen()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int BAD;\n");
        var first = FixTestSupport.Result(Err(file, 1, "CS0001"));

        var outcome = new BuildFixLoop([new StripBad()], null).Run(root, first, () => Green);

        Assert.IsTrue(outcome.Green);
        Assert.AreEqual(1, outcome.Rounds);
        Assert.HasCount(1, outcome.Edits);
        Assert.AreEqual("int ;\n", File.ReadAllText(file));
        Assert.IsTrue(outcome.Notes.Any(n => n.Contains("green")));
    }

    [TestMethod]
    public void Run_StopsAtThreeRounds_WhenARuleKeepsFiringButTheErrorStays()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "BAD");
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));
        var rebuilds = 0;

        // The rule fires only while the file still has BAD; re-add it each rebuild so it always fires.
        var outcome = new BuildFixLoop([new StripBad()], null).Run(root, red, () =>
        {
            rebuilds++;
            File.WriteAllText(file, "BAD");
            return red;
        });

        Assert.IsFalse(outcome.Green);
        Assert.AreEqual(BuildFixLoop.MaxRounds, outcome.Rounds);
        Assert.AreEqual(BuildFixLoop.MaxRounds, rebuilds);
        Assert.HasCount(1, outcome.Remaining);
        Assert.IsFalse(outcome.Notes.Any(n => n.Contains("is green")));
    }

    [TestMethod]
    public void Run_UsesTheAi_WhenNoRuleApplies_AndKeepsAnEditThatLowersTheErrors()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var first = FixTestSupport.Result(Err(file, 1, "CS1002", "; expected"));
        var ai = new FakeAi((d, ex) => "int a = 1;");

        var outcome = new BuildFixLoop([], ai).Run(root, first, () => Green);

        Assert.IsTrue(outcome.Green);
        Assert.AreEqual("int a = 1;\n", File.ReadAllText(file));
        Assert.AreEqual("AI", outcome.Edits[0].By);
        Assert.IsFalse(outcome.Edits[0].Reverted);
    }

    [TestMethod]
    public void Run_KeepsAnAiEdit_ThatLowersTheErrorsWithoutReachingGreen()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var other = FixTestSupport.Write(root, "B.cs", "int b = 2\n");
        var first = FixTestSupport.Result(Err(file, 1, "CS1002") + "\n" + Err(other, 1, "CS1002"));
        var second = FixTestSupport.Result(Err(other, 1, "CS1002"));
        var ai = new FakeAi((d, ex) => d.File == file ? "int a = 1;" : null);
        var rebuilds = 0;

        var outcome = new BuildFixLoop([], ai).Run(root, first, () => { rebuilds++; return second; });

        Assert.IsFalse(outcome.Green);
        Assert.AreEqual("int a = 1;\n", File.ReadAllText(file));
        Assert.HasCount(1, outcome.Remaining);
        Assert.IsFalse(outcome.Edits[0].Reverted);
        Assert.IsFalse(outcome.Notes.Any(n => n.Contains("is green")));
    }

    [TestMethod]
    public void Run_RevertsAnAiEdit_ThatDoesNotLowerTheErrorCount()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var original = Bytes(file);
        var red = FixTestSupport.Result(Err(file, 1, "CS1002", "; expected"));
        var ai = new FakeAi((d, ex) => "int a = 1; // changed");

        var outcome = new BuildFixLoop([], ai).Run(root, red, () => red);

        Assert.IsFalse(outcome.Green);
        CollectionAssert.AreEqual(original, Bytes(file));
        Assert.HasCount(1, outcome.Edits);
        Assert.IsTrue(outcome.Edits.All(e => e.Reverted));
        Assert.IsTrue(outcome.Notes.Any(n => n.Contains("reverted")));
        Assert.HasCount(1, outcome.Remaining);
    }

    [TestMethod]
    public void Run_RevertsAnAiEdit_ThatRaisesTheErrorCount()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var original = Bytes(file);
        var red = FixTestSupport.Result(Err(file, 1, "CS1002"));
        var worse = FixTestSupport.Result(Err(file, 1, "CS1002") + "\n" + Err(file, 2, "CS0103"));

        var outcome = new BuildFixLoop([], new FakeAi((d, ex) => "int a = 2")).Run(root, red, () => worse);

        CollectionAssert.AreEqual(original, Bytes(file));
        Assert.IsTrue(outcome.Edits.All(e => e.Reverted));
        Assert.HasCount(1, outcome.Remaining);
    }

    [TestMethod]
    public void Run_RevertsARuleEdit_ThatRaisesTheErrorCount()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "BAD");
        var original = Bytes(file);
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));
        var worse = FixTestSupport.Result(Err(file, 1, "CS0103") + "\n" + Err(file, 2, "CS0103"));

        var outcome = new BuildFixLoop([new Rewrite("WORSE")], null).Run(root, red, () => worse);

        Assert.IsFalse(outcome.Green);
        CollectionAssert.AreEqual(original, Bytes(file));
        Assert.IsTrue(outcome.Edits.All(e => e.Reverted));
        Assert.AreEqual(1, outcome.Rounds);
        Assert.HasCount(1, outcome.Remaining);
        Assert.AreEqual("CS0001", outcome.Remaining[0].Code);
    }

    [TestMethod]
    public void Run_RevertsAnEdit_ThatBringsErrorsIntoANewFile()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "BAD");
        var fresh = FixTestSupport.Write(root, "C.cs", "class C {}");
        var original = Bytes(file);
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));
        // One error before, one after: the count is not higher, but it moved into another file.
        var moved = FixTestSupport.Result(Err(fresh, 1, "CS0103"));

        var outcome = new BuildFixLoop([new Rewrite("WORSE")], null).Run(root, red, () => moved);

        CollectionAssert.AreEqual(original, Bytes(file));
        Assert.IsTrue(outcome.Edits.All(e => e.Reverted));
        Assert.AreEqual(file, outcome.Remaining[0].File);
    }

    [TestMethod]
    public void Run_RevertsAnEdit_WhenTheRebuildFailsWithErrorsThatCannotBeRead()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var original = Bytes(file);
        var red = FixTestSupport.Result(Err(file, 1, "CS1002"));
        var timedOut = new ProcessResult(-1, "", "", true);

        var outcome = new BuildFixLoop([], new FakeAi((d, ex) => "int a = 1;")).Run(root, red, () => timedOut);

        Assert.IsFalse(outcome.Green);
        CollectionAssert.AreEqual(original, Bytes(file));
        Assert.IsTrue(outcome.Edits.All(e => e.Reverted));
        Assert.HasCount(1, outcome.Remaining);
    }

    [TestMethod]
    public void Run_RejectsAnAiEdit_ThatDoesNotParse()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var red = FixTestSupport.Result(Err(file, 1, "CS1002"));
        var ai = new FakeAi((d, ex) => "int a = = ;;; {{{");
        var rebuilt = false;

        var outcome = new BuildFixLoop([], ai).Run(root, red, () => { rebuilt = true; return red; });

        Assert.AreEqual(1, ai.Asks);
        Assert.IsFalse(rebuilt);
        Assert.AreEqual("int a = 1\n", File.ReadAllText(file));
        Assert.HasCount(0, outcome.Edits);
    }

    [TestMethod]
    public void Run_AcceptsAnAiEdit_WhenTheFileHasOtherSyntaxErrorsFarAway()
    {
        var root = FixTestSupport.TempProject();
        var text = "int a = 1\n" + string.Concat(Enumerable.Repeat("// pad\n", 40)) + "int z = 3\n";
        var file = FixTestSupport.Write(root, "A.cs", text);
        var red = FixTestSupport.Result(Err(file, 1, "CS1002") + "\n" + Err(file, 42, "CS1002"));
        var left = FixTestSupport.Result(Err(file, 42, "CS1002"));
        var ai = new FakeAi((d, ex) => ex.Replace("int a = 1\n", "int a = 1;\n"));

        var outcome = new BuildFixLoop([], ai).Run(root, red, () => left);

        Assert.HasCount(1, outcome.Edits);
        Assert.AreEqual(text.Replace("int a = 1\n", "int a = 1;\n"), File.ReadAllText(file));
    }

    [TestMethod]
    public void Run_RejectsAnAiAnswerThatChangesNothing()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var red = FixTestSupport.Result(Err(file, 1, "CS1002"));
        var ai = new FakeAi((d, ex) => ex);
        var rebuilt = false;

        var outcome = new BuildFixLoop([], ai).Run(root, red, () => { rebuilt = true; return red; });

        Assert.AreEqual(1, ai.Asks);
        Assert.IsFalse(rebuilt);
        Assert.HasCount(0, outcome.Edits);
        Assert.AreEqual("int a = 1\n", File.ReadAllText(file));
    }

    [TestMethod]
    public void Run_RejectsAnAiAnswerThatNamesAnotherFile()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var red = FixTestSupport.Result(Err(file, 1, "CS1002"));
        var ai = new FakeAi((d, ex) => "// File: B.cs\nint a = 1;");

        var outcome = new BuildFixLoop([], ai).Run(root, red, () => red);

        Assert.AreEqual(1, ai.Asks);
        Assert.HasCount(0, outcome.Edits);
        Assert.AreEqual("int a = 1\n", File.ReadAllText(file));
    }

    [TestMethod]
    public void Run_SurvivesAnAiThatThrows_AndAnAiThatIsNull()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var red = FixTestSupport.Result(Err(file, 1, "CS1002"));

        var thrown = new BuildFixLoop([], new FakeAi((d, ex) => throw new InvalidOperationException("model"))).Run(root, red, () => red);
        var none = new BuildFixLoop([], new FakeAi((d, ex) => null)).Run(root, red, () => red);

        Assert.HasCount(0, thrown.Edits);
        Assert.HasCount(0, none.Edits);
        Assert.AreEqual("int a = 1\n", File.ReadAllText(file));
        Assert.IsTrue(none.Notes.Any(n => n.Contains("no usable fix")));
    }

    [TestMethod]
    public void Run_SkipsAFileOutsideTheProject_WithoutAskingTheAi()
    {
        var root = FixTestSupport.TempProject();
        var outside = FixTestSupport.Write(FixTestSupport.TempProject(), "B.cs", "int b = 1\n");
        var red = FixTestSupport.Result(Err(outside, 1, "CS1002"));
        var ai = new FakeAi((d, ex) => "int b = 1;");

        var outcome = new BuildFixLoop([], ai).Run(root, red, () => red);

        Assert.AreEqual(0, ai.Asks);
        Assert.HasCount(0, outcome.Edits);
        Assert.AreEqual("int b = 1\n", File.ReadAllText(outside));
    }

    [TestMethod]
    public void Run_SkipsTheAi_AndSaysSo_WhenOllamaIsDown()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "x");
        var red = FixTestSupport.Result(Err(file, 1, "CS1002"));
        var ai = new FakeAi((d, ex) => "y", up: false);

        var outcome = new BuildFixLoop([], ai).Run(root, red, () => red);

        Assert.AreEqual(0, ai.Asks);
        Assert.IsTrue(outcome.Notes.Any(n => n.Contains("Ollama is not running")));
        Assert.AreEqual("x", File.ReadAllText(file));
    }

    [TestMethod]
    public void Run_NeverAsksTheAi_WhenARuleFixedSomething()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "BAD");
        var other = FixTestSupport.Write(root, "B.cs", "x");
        var red = FixTestSupport.Result(Err(file, 1, "CS0001") + "\n" + Err(other, 1, "CS1002"));
        var ai = new FakeAi((d, ex) => "y");

        new BuildFixLoop([new StripBad()], ai).Run(root, red, () => Green);

        Assert.AreEqual(0, ai.Asks);
    }

    [TestMethod]
    public void Run_AsksTheAiForAtMostFiveErrorsPerRound()
    {
        var root = FixTestSupport.TempProject();
        var files = Enumerable.Range(1, 9)
            .Select(i => FixTestSupport.Write(root, $"F{i}.cs", "int a = 1\n")).ToList();
        var red = FixTestSupport.Result(string.Join("\n", files.Select(f => Err(f, 1, "CS1002"))));
        var ai = new FakeAi((d, ex) => ex + " // x");

        new BuildFixLoop([], ai).Run(root, red, () => red);

        // Every ask gets a usable edit, so only the cap can stop the sixth.
        Assert.AreEqual(BuildFixLoop.MaxAiAsksPerRound, ai.Asks);
    }

    [TestMethod]
    public void Run_IsBoundedByRoundsAndAsks_EvenWhenTheAiAlwaysProposesAndTheErrorsFall()
    {
        var root = FixTestSupport.TempProject();
        var files = Enumerable.Range(1, 20)
            .Select(i => FixTestSupport.Write(root, $"F{i}.cs", "int a = 1\n")).ToList();
        var rebuilds = 0;
        ProcessResult Failing(int count)
            => FixTestSupport.Result(string.Join("\n", files.Take(count).Select(f => Err(f, 1, "CS1002"))));
        var counts = new[] { 18, 16, 14, 12, 10 };
        var ai = new FakeAi((d, ex) => ex + " //");

        var outcome = new BuildFixLoop([], ai).Run(root, Failing(20), () => Failing(counts[rebuilds++]));

        Assert.AreEqual(BuildFixLoop.MaxRounds, outcome.Rounds);
        Assert.AreEqual(BuildFixLoop.MaxRounds, rebuilds);
        Assert.AreEqual(BuildFixLoop.MaxRounds * BuildFixLoop.MaxAiAsksPerRound, ai.Asks);
        Assert.IsFalse(outcome.Green);
        Assert.HasCount(14, outcome.Remaining);
    }

    [TestMethod]
    public void Run_AsksOnlyOnce_AboutTwoErrorsInOneFile()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\nint b = 2\n");
        var red = FixTestSupport.Result(Err(file, 1, "CS1002") + "\n" + Err(file, 2, "CS1002"));
        var ai = new FakeAi((d, ex) => ex.Replace("int a = 1\n", "int a = 1;\n"));

        new BuildFixLoop([], ai).Run(root, red, () => red);

        // After the first edit the second error's line number is stale; asking again could damage the file.
        Assert.AreEqual(1, ai.Asks);
    }

    [TestMethod]
    public void Run_KeepsCrlf_AndDoesNotInventALineEndingOnTheLastLine()
    {
        var root = FixTestSupport.TempProject();
        var file = Path.Combine(root, "A.cs");
        File.WriteAllBytes(file, new UTF8Encoding(false).GetBytes("int a = 1;\r\nint b = 2"));
        var first = FixTestSupport.Result(Err(file, 2, "CS1002"));
        var ai = new FakeAi((d, ex) => "int a = 1;\nint b = 2;");

        new BuildFixLoop([], ai).Run(root, first, () => Green);

        Assert.AreEqual("int a = 1;\r\nint b = 2;", File.ReadAllText(file));
    }

    [TestMethod]
    public void Run_KeepsCrlfAndTheTrailingNewline()
    {
        var root = FixTestSupport.TempProject();
        var file = Path.Combine(root, "A.cs");
        File.WriteAllBytes(file, new UTF8Encoding(false).GetBytes("int a = 1;\r\nint b = 2\r\n"));
        var first = FixTestSupport.Result(Err(file, 2, "CS1002"));
        var ai = new FakeAi((d, ex) => "int a = 1;\r\nint b = 2;");

        new BuildFixLoop([], ai).Run(root, first, () => Green);

        Assert.AreEqual("int a = 1;\r\nint b = 2;\r\n", File.ReadAllText(file));
    }

    [TestMethod]
    public void Run_NeverAsksTheAiToEditANonCsFile()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Main.xaml", "<Grid/>");
        var red = FixTestSupport.Result(Err(file, 1, "MC3000"));
        var ai = new FakeAi((d, ex) => "<Grid></Grid>");

        new BuildFixLoop([], ai).Run(root, red, () => red);

        Assert.AreEqual(0, ai.Asks);
    }

    [TestMethod]
    public void Run_SurvivesARuleThatWritesOutsideTheProject()
    {
        var root = FixTestSupport.TempProject();
        var outside = FixTestSupport.Write(FixTestSupport.TempProject(), "B.cs", "keep");
        var file = FixTestSupport.Write(root, "A.cs", "x");
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));

        var outcome = new BuildFixLoop([new OutsideWriter(outside)], null).Run(root, red, () => red);

        Assert.AreEqual("keep", File.ReadAllText(outside));
        Assert.IsFalse(outcome.Green);
        Assert.HasCount(0, outcome.Edits);
    }

    [TestMethod]
    public void Run_SurvivesARuleThatWritesAndThenThrows_AndLeavesTheFileAsItWas()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var original = Bytes(file);
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));
        var ai = new FakeAi((d, ex) => null);

        var outcome = new BuildFixLoop([new WriteThenThrow()], ai).Run(root, red, () => red);

        Assert.IsFalse(outcome.Green);
        CollectionAssert.AreEqual(original, Bytes(file));
        Assert.IsTrue(outcome.Edits.All(e => e.Reverted));
        Assert.IsTrue(outcome.Notes.Any(n => n.Contains("boom") && n.Contains("skipped")));
    }

    [TestMethod]
    public void Run_StopsWithANote_WhenDotnetCannotStart()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "BAD");
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));

        var outcome = new BuildFixLoop([new StripBad()], null).Run(root, red, () => throw new Win32Exception());

        Assert.IsFalse(outcome.Green);
        Assert.IsTrue(outcome.Notes.Any(n => n.Contains("dotnet")));
        Assert.IsFalse(outcome.Notes.Any(n => n.Contains("is green")));
    }

    [TestMethod]
    public void Run_RevertsAnAiEdit_WhenDotnetCannotStart()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var original = Bytes(file);
        var red = FixTestSupport.Result(Err(file, 1, "CS1002"));

        var outcome = new BuildFixLoop([], new FakeAi((d, ex) => "int a = 1;"))
            .Run(root, red, () => throw new Win32Exception());

        CollectionAssert.AreEqual(original, Bytes(file));
        Assert.IsTrue(outcome.Edits.All(e => e.Reverted));
    }

    [TestMethod]
    public void Run_DoesNothing_ForAGreenBuild()
    {
        var root = FixTestSupport.TempProject();

        var outcome = new BuildFixLoop([new StripBad()], null).Run(root, Green, () => Green);

        Assert.IsTrue(outcome.Green);
        Assert.AreEqual(0, outcome.Rounds);
    }

    [TestMethod]
    public void Run_ChangesNothing_WhenTheFailureHasNoReadableErrors()
    {
        var root = FixTestSupport.TempProject();
        var failed = new ProcessResult(1, "Something went wrong.", "", false);
        var asked = false;

        var outcome = new BuildFixLoop([new StripBad()], new FakeAi((d, ex) => { asked = true; return null; }))
            .Run(root, failed, () => Green);

        Assert.IsFalse(outcome.Green);
        Assert.AreEqual(0, outcome.Rounds);
        Assert.IsFalse(asked);
        Assert.IsTrue(outcome.Notes.Any(n => n.Contains("no compiler errors")));
    }

    /// <summary>A rule that writes, then says it fixed nothing.</summary>
    private sealed class WriteThenFalse : IBuildFixRule
    {
        public string Name => "half";

        public bool Fix(BuildDiagnostic d, FixSession s)
        {
            if (d.Code != "CS0002") return false;
            s.Write(d.File, "half done", Name);
            return false;
        }
    }

    /// <summary>A rule that edits whatever file the diagnostic names.</summary>
    private sealed class EditNamedFile : IBuildFixRule
    {
        public string Name => "named";

        public bool Fix(BuildDiagnostic d, FixSession s)
        {
            s.Write(d.File, "EDITED", Name);
            return true;
        }
    }

    [TestMethod]
    public void Run_RevertsRuleEdits_AndDoesNotThrow_WhenTheRebuildThrowsAnything()
    {
        Exception[] failures =
        [
            new InvalidOperationException(@"C:\secret\path.cs"),
            new IOException(@"C:\secret\path.cs"),
            new OperationCanceledException(),
        ];

        foreach (var failure in failures)
        {
            var root = FixTestSupport.TempProject();
            var file = FixTestSupport.Write(root, "A.cs", "BAD");
            var original = Bytes(file);
            var red = FixTestSupport.Result(Err(file, 1, "CS0001"));

            var outcome = new BuildFixLoop([new StripBad()], null).Run(root, red, () => throw failure);

            Assert.IsFalse(outcome.Green);
            CollectionAssert.AreEqual(original, Bytes(file));
            Assert.IsTrue(outcome.Edits.All(e => e.Reverted));
            Assert.IsTrue(
                outcome.Notes.Any(n => n.Contains("rebuild") && n.Contains("reverted") && n.Contains(failure.GetType().Name)),
                failure.GetType().Name);
            Assert.IsFalse(outcome.Notes.Any(n => n.Contains("secret")));
            Assert.HasCount(1, outcome.Remaining);
        }
    }

    [TestMethod]
    public void Run_RevertsAiEdits_AndDoesNotThrow_WhenTheRebuildThrowsAnything()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var original = Bytes(file);
        var red = FixTestSupport.Result(Err(file, 1, "CS1002"));

        var outcome = new BuildFixLoop([], new FakeAi((d, ex) => "int a = 1;"))
            .Run(root, red, () => throw new InvalidOperationException("x"));

        CollectionAssert.AreEqual(original, Bytes(file));
        Assert.IsTrue(outcome.Edits.All(e => e.Reverted));
        Assert.IsTrue(outcome.Notes.Any(n => n.Contains("rebuild") && n.Contains("InvalidOperationException")));
    }

    [TestMethod]
    public void Run_NamesAFileThatCouldNotBeRestored_AndStillRestoresTheOthers()
    {
        var root = FixTestSupport.TempProject();
        var a = FixTestSupport.Write(root, "A.cs", "aaa");
        var b = FixTestSupport.Write(root, Path.Combine("Sub", "B.cs"), "bbb");
        var origA = Bytes(a);
        var red = FixTestSupport.Result(Err(a, 1, "CS0001") + "\n" + Err(b, 1, "CS0001"));
        FileStream? held = null;
        FixOutcome outcome;

        try
        {
            outcome = new BuildFixLoop([new EditNamedFile()], null).Run(root, red, () =>
            {
                held = new FileStream(b, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                throw new IOException("boom");
            });
        }
        finally
        {
            held?.Dispose();
        }

        CollectionAssert.AreEqual(origA, Bytes(a));
        Assert.AreEqual("EDITED", File.ReadAllText(b));
        Assert.IsTrue(outcome.Notes.Any(n => n.Contains(Path.Combine("Sub", "B.cs")) && n.Contains("not restored")));
        Assert.IsTrue(outcome.Edits.Single(e => e.File == a).Reverted);
        Assert.IsFalse(outcome.Edits.Single(e => e.File == b).Reverted);
    }

    [TestMethod]
    public void Run_NeverAsksTheAi_AboutAFileUnderObjBinOrCodegen()
    {
        foreach (var folder in new[] { "obj", "bin", ".codegen", "OBJ", Path.Combine("Sub", "Bin", "Debug") })
        {
            var root = FixTestSupport.TempProject();
            var file = FixTestSupport.Write(root, Path.Combine(folder, "G.cs"), "int a = 1\n");
            var original = Bytes(file);
            var red = FixTestSupport.Result(Err(file, 1, "CS1002"));
            var ai = new FakeAi((d, ex) => "int a = 1;");
            var rebuilt = false;

            var outcome = new BuildFixLoop([], ai).Run(root, red, () => { rebuilt = true; return Green; });

            Assert.AreEqual(0, ai.Asks, folder);
            Assert.IsFalse(rebuilt, folder);
            CollectionAssert.AreEqual(original, Bytes(file), folder);
            Assert.HasCount(0, outcome.Edits);
            Assert.IsTrue(outcome.Notes.Any(n => n.Contains("Skipped")), folder);
        }
    }

    [TestMethod]
    public void Run_NeverLetsARuleEditAFileUnderObj()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, Path.Combine("obj", "Debug", "G.cs"), "keep");
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));

        var outcome = new BuildFixLoop([new EditNamedFile()], null).Run(root, red, () => red);

        Assert.AreEqual("keep", File.ReadAllText(file));
        Assert.HasCount(0, outcome.Edits);
    }

    [TestMethod]
    public void Run_NeverLetsARuleEditAFileOutsideTheProject_AndKeepsTheOtherRuleFixes()
    {
        var root = FixTestSupport.TempProject();
        var outside = FixTestSupport.Write(FixTestSupport.TempProject(), "B.cs", "BAD");
        var inside = FixTestSupport.Write(root, "A.cs", "BAD");
        var red = FixTestSupport.Result(Err(outside, 1, "CS0001") + "\n" + Err(inside, 1, "CS0001"));

        var outcome = new BuildFixLoop([new StripBad()], null).Run(root, red, () => Green);

        Assert.AreEqual("BAD", File.ReadAllText(outside));
        Assert.AreEqual("", File.ReadAllText(inside));
        Assert.IsTrue(outcome.Green);
    }

    [TestMethod]
    public void Run_UndoesAPartialAiRound_WhenALaterWriteFails()
    {
        var root = FixTestSupport.TempProject();
        var a = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var b = FixTestSupport.Write(root, "B.cs", "int b = 1\n");
        var origA = Bytes(a);
        var origB = Bytes(b);
        File.SetAttributes(b, FileAttributes.ReadOnly);
        var red = FixTestSupport.Result(Err(a, 1, "CS1002") + "\n" + Err(b, 1, "CS1002"));
        var rebuilt = false;
        FixOutcome outcome;

        try
        {
            outcome = new BuildFixLoop([], new FakeAi((d, ex) => ex + ";"))
                .Run(root, red, () => { rebuilt = true; return Green; });
        }
        finally
        {
            File.SetAttributes(b, FileAttributes.Normal);
        }

        CollectionAssert.AreEqual(origA, Bytes(a));
        CollectionAssert.AreEqual(origB, Bytes(b));
        Assert.IsFalse(rebuilt);
        Assert.IsTrue(outcome.Edits.All(e => e.Reverted));
        Assert.IsFalse(outcome.Green);
    }

    [TestMethod]
    public void Run_UndoesTheHalfEditOfARuleThatWroteAndSaidNo_ButKeepsTheOtherRuleFix()
    {
        var root = FixTestSupport.TempProject();
        var a = FixTestSupport.Write(root, "A.cs", "BAD");
        var b = FixTestSupport.Write(root, "B.cs", "int b = 1\n");
        var origB = Bytes(b);
        var red = FixTestSupport.Result(Err(a, 1, "CS0001") + "\n" + Err(b, 1, "CS0002"));
        var ai = new FakeAi((d, ex) => "never");

        var outcome = new BuildFixLoop([new StripBad(), new WriteThenFalse()], ai).Run(root, red, () => Green);

        Assert.AreEqual("", File.ReadAllText(a));
        CollectionAssert.AreEqual(origB, Bytes(b));
        Assert.AreEqual(0, ai.Asks);
        Assert.IsTrue(outcome.Green);
        Assert.IsTrue(outcome.Edits.Single(e => e.File == b).Reverted);
        Assert.IsFalse(outcome.Edits.Single(e => e.File == a).Reverted);
    }

    [TestMethod]
    public void Run_UndoesTheHalfEditOfARuleThatThrew_ButKeepsTheOtherRuleFix()
    {
        var root = FixTestSupport.TempProject();
        var a = FixTestSupport.Write(root, "A.cs", "BAD");
        var b = FixTestSupport.Write(root, "B.cs", "int b = 1\n");
        var origB = Bytes(b);
        var red = FixTestSupport.Result(Err(b, 1, "CS0002") + "\n" + Err(a, 1, "CS0001"));

        var outcome = new BuildFixLoop([new StripBad(), new WriteThenThrow()], null).Run(root, red, () => Green);

        Assert.AreEqual("", File.ReadAllText(a));
        CollectionAssert.AreEqual(origB, Bytes(b));
        Assert.IsTrue(outcome.Green);
    }

    [TestMethod]
    public void Run_RevertsOnlyRoundTwo_WhenRoundOneEditsWereGood()
    {
        var root = FixTestSupport.TempProject();
        var a = FixTestSupport.Write(root, "A.cs", "BAD");
        var b = FixTestSupport.Write(root, "B.cs", "int b = 1\n");
        var origB = Bytes(b);
        var first = FixTestSupport.Result(Err(a, 1, "CS0001") + "\n" + Err(b, 1, "CS1002"));
        var second = FixTestSupport.Result(Err(b, 1, "CS1002"));
        var worse = FixTestSupport.Result(Err(b, 1, "CS1002") + "\n" + Err(b, 2, "CS0103"));
        var builds = new Queue<ProcessResult>([second, worse]);

        var outcome = new BuildFixLoop([new StripBad()], new FakeAi((d, ex) => "int b = 2; // x"))
            .Run(root, first, () => builds.Dequeue());

        Assert.AreEqual(2, outcome.Rounds);
        Assert.AreEqual("", File.ReadAllText(a));
        CollectionAssert.AreEqual(origB, Bytes(b));
        Assert.IsFalse(outcome.Edits.Single(e => e.Round == 1).Reverted);
        Assert.IsTrue(outcome.Edits.Single(e => e.Round == 2).Reverted);
        Assert.HasCount(1, outcome.Remaining);
    }

    [TestMethod]
    public void Run_SkipsADiagnosticPastTheEndOfTheFile()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a\n");
        var original = Bytes(file);
        var red = FixTestSupport.Result(Err(file, 99, "CS1002"));
        var ai = new FakeAi((d, ex) => "x");

        var outcome = new BuildFixLoop([], ai).Run(root, red, () => red);

        Assert.AreEqual(0, ai.Asks);
        CollectionAssert.AreEqual(original, Bytes(file));
        Assert.HasCount(0, outcome.Edits);
    }

    [TestMethod]
    public void Run_FixesASingleLineFileWithNoLineEnding()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1");
        var red = FixTestSupport.Result(Err(file, 1, "CS1002"));

        new BuildFixLoop([], new FakeAi((d, ex) => "int a = 1;")).Run(root, red, () => Green);

        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("int a = 1;"), Bytes(file));
    }

    [TestMethod]
    public void Run_HandlesAnEmptyFile()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "");
        var red = FixTestSupport.Result(Err(file, 1, "CS8803"));

        var outcome = new BuildFixLoop([], new FakeAi((d, ex) => "class A { }")).Run(root, red, () => Green);

        Assert.IsTrue(outcome.Green);
        Assert.AreEqual("class A { }", File.ReadAllText(file));
    }

    [TestMethod]
    public void Run_KeepsTheBomOfAFileTheAiEdits()
    {
        var root = FixTestSupport.TempProject();
        var file = Path.Combine(root, "A.cs");
        File.WriteAllBytes(file, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("int a = 1\r\n")]);
        var red = FixTestSupport.Result(Err(file, 1, "CS1002"));

        new BuildFixLoop([], new FakeAi((d, ex) => "int a = 1;")).Run(root, red, () => Green);

        byte[] expected = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("int a = 1;\r\n")];
        CollectionAssert.AreEqual(expected, Bytes(file));
    }

    [TestMethod]
    public void Run_RevertsTheRound_AndReturnsItsEdits_WhenReadingTheRebuildThrows()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int BAD;\n");
        var before = Bytes(file);
        var first = FixTestSupport.Result(Err(file, 1, "CS0001"));
        var poisoned = FixTestSupport.Result("bad\0name.cs(1,1): error CS0001: x");
        var loop = new BuildFixLoop([new StripBad()], null);

        var outcome = loop.Run(root, first, () => poisoned);

        CollectionAssert.AreEqual(before, Bytes(file));
        Assert.IsFalse(outcome.Green);
        Assert.HasCount(1, outcome.Edits);
        Assert.IsTrue(outcome.Edits[0].Reverted);
        Assert.HasCount(1, outcome.Remaining);
        Assert.AreEqual("CS0001", outcome.Remaining[0].Code);
        Assert.Contains(n => n.Contains("ArgumentException", StringComparison.Ordinal), outcome.Notes);
        Assert.AreSame(loop.LastSession!.Edits, outcome.Edits);

        var lines = BuildFixReport.Lines(outcome, root);
        Assert.AreEqual("Nothing was kept: all 1 edit made was undone.", lines[0]);
        Assert.Contains("  The original of each edited file is kept under " + BuildFixReport.BackupLocation, lines);
    }

    [TestMethod]
    public void Run_AnAiEdit_RecordsTheFirstChangedLine_WithoutLineEndings()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1;\r\nint b = ;\r\nint c = 3;\r\n");
        var first = FixTestSupport.Result(Err(file, 2, "CS1525", "bad"));
        var ai = new FakeAi((d, ex) => "int a = 1;\nint b = 0;\nint c = 3;");

        var outcome = new BuildFixLoop([], ai).Run(root, first, () => Green);

        Assert.IsTrue(outcome.Green);
        var change = outcome.Edits[0].Change;
        Assert.IsNotNull(change);
        Assert.AreEqual(2, change.Line);
        Assert.AreEqual("int b = ;", change.Before);
        Assert.AreEqual("int b = 0;", change.After);
        Assert.AreEqual(1, change.Lines);
        Assert.AreEqual("int a = 1;\r\nint b = 0;\r\nint c = 3;\r\n", File.ReadAllText(file));
    }

    [TestMethod]
    public void Run_AnAiEditOfSeveralLines_CountsThem()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = ;\nint b = ;\nint c = 3;\n");
        var first = FixTestSupport.Result(Err(file, 1, "CS1525", "bad") + "\n" + Err(file, 2, "CS1525", "bad"));
        var ai = new FakeAi((d, ex) => "int a = 0;\nint b = 0;\nint c = 3;");

        var outcome = new BuildFixLoop([], ai).Run(root, first, () => Green);

        var change = outcome.Edits[0].Change;
        Assert.IsNotNull(change);
        Assert.AreEqual(1, change.Line);
        Assert.AreEqual("int a = ;", change.Before);
        Assert.AreEqual("int a = 0;", change.After);
        Assert.AreEqual(2, change.Lines);
    }

    [TestMethod]
    public void Run_ARuleEdit_HasNoChangeRecord()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int BAD;\n");
        var first = FixTestSupport.Result(Err(file, 1, "CS0001"));

        var outcome = new BuildFixLoop([new StripBad()], null).Run(root, first, () => Green);

        Assert.IsNull(outcome.Edits[0].Change);
    }
}
