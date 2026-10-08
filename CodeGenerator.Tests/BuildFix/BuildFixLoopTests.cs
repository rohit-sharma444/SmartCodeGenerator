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
}
