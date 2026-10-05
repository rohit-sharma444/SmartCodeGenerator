using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerator.App.Scripts;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.App.Scripts;

// Real pipeline on a temp workspace. Serial: the pipeline and ValidationRegistry are static.
[TestClass]
[DoNotParallelize]
public sealed class ScriptRunnerTests
{
    private string _root = string.Empty;
    private readonly NaturalLanguagePipeline _pipeline = new();
    private ProjectMetaData? _project;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "cg_run_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "Book.cs"), "namespace S { public class Book { } }");
        ProjectManager.CurrentProject = null;
        ValidationRegistry.Clear();
        _project = null;
    }

    [TestCleanup]
    public void Cleanup()
    {
        ValidationRegistry.Clear();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private bool Execute(ScriptStep step) =>
        _pipeline.Handle(((CommandStep)step).Text, ref _project, new List<PropertyRule>(), _root,
            p => ProjectManager.CurrentProject = p).Handled;

    private ScriptOutcome Run(string script, List<string>? said = null) =>
        ScriptRunner.Run("t.cgs", ScriptFile.Parse(script).Steps, _root, Execute,
            (said ?? []).Add, confirmSnapshot: () => true);

    [TestMethod]
    public void Every_line_runs_in_order_and_is_shown()
    {
        var said = new List<string>();

        var outcome = Run("add property Pages int to Book\nadd property Title string to Book", said);

        Assert.IsTrue(outcome.Succeeded);
        Assert.AreEqual("Ran 2 commands from t.cgs.", outcome.Message);
        CollectionAssert.AreEqual(
            new[] { "[1/2] line 1: add property Pages int to Book", "[2/2] line 2: add property Title string to Book" }, said.ToArray());
        CollectionAssert.AreEqual(
            new[] { "add property Pages int to Book", "add property Title string to Book" }, outcome.Ran.ToArray());
        var book = File.ReadAllText(Path.Combine(_root, "Book.cs"));
        StringAssert.Contains(book, "Pages");
        StringAssert.Contains(book, "Title");
    }

    [TestMethod]
    public void A_failing_line_puts_back_what_the_earlier_lines_did()   // Review Focus 1
    {
        var outcome = Run("add property Pages int to Book\nadd property Pages int to Ghost\nadd property Title string to Book");

        Assert.IsFalse(outcome.Succeeded);
        Assert.AreEqual("Line 2 failed. Everything the script did was put back.", outcome.Message);
        Assert.IsEmpty(outcome.Ran);
        Assert.AreEqual("namespace S { public class Book { } }", File.ReadAllText(Path.Combine(_root, "Book.cs")));
    }

    // S5: a build in the run left App/obj behind the rollback, so the folder survived it and undo
    // offered "Undid: run t.cgs", which only removed the leftovers.
    [TestMethod]
    public void A_run_that_failed_and_was_put_back_leaves_nothing_to_undo()
    {
        var shadowParent = Path.Combine(_root, "..", "cg_run_sh_" + Guid.NewGuid().ToString("N"));
        using var journal = new CodeGenerationEngine.Core.Undo.UndoJournal(_root, shadowParent: shadowParent);
        try
        {
            ScriptOutcome outcome;
            using (journal.Begin("run t.cgs"))
            {
                outcome = ScriptRunner.Run("t.cgs", ScriptFile.Parse("create class A\ncreate class B").Steps, _root,
                    step =>
                    {
                        if (step.Line == 2)
                            return false;
                        Directory.CreateDirectory(Path.Combine(_root, "App", "obj"));
                        File.WriteAllText(Path.Combine(_root, "App", "App.csproj"), "<Project/>");
                        File.WriteAllText(Path.Combine(_root, "App", "obj", "project.assets.json"), "{}");
                        return true;
                    },
                    _ => { }, confirmSnapshot: () => true);
            }

            Assert.IsFalse(outcome.Succeeded);
            Assert.IsFalse(Directory.Exists(Path.Combine(_root, "App")));
            Assert.IsFalse(journal.CanUndo, journal.NextUndoLabel);
        }
        finally
        {
            journal.Dispose();
            try { Directory.Delete(shadowParent, recursive: true); } catch (IOException) { }
        }
    }

    [TestMethod]
    public void A_rule_added_before_the_failure_is_gone_too()   // Review Focus 5
    {
        var outcome = Run("add validation Title is required in Book\nadd property Pages int to Ghost");

        Assert.IsFalse(outcome.Succeeded);
        Assert.IsEmpty(ValidationRegistry.Get("Book"));
    }

    [TestMethod]
    public void A_step_that_throws_fails_with_the_reason()
    {
        var outcome = ScriptRunner.Run("t.cgs", ScriptFile.Parse("create class A").Steps, _root,
            _ => throw new InvalidOperationException("boom"), _ => { }, () => true);

        Assert.AreEqual("Line 1 failed: InvalidOperationException: boom. Everything the script did was put back.",
            outcome.Message);
    }

    // M4: Ctrl-C between lines stops the run and puts back what it did, rather than leaving it half done.
    [TestMethod]
    public void A_stop_asked_for_between_lines_puts_back_what_ran()
    {
        var stop = false;

        var outcome = ScriptRunner.Run("t.cgs",
            ScriptFile.Parse("add property Pages int to Book\nadd property Title string to Book").Steps, _root,
            step => { var ok = Execute(step); stop = true; return ok; }, _ => { }, () => true,
            stopRequested: () => stop);

        Assert.IsFalse(outcome.Succeeded);
        Assert.AreEqual("Stopped before line 2. Everything the script did was put back.", outcome.Message);
        Assert.AreEqual("namespace S { public class Book { } }", File.ReadAllText(Path.Combine(_root, "Book.cs")));
    }

    // M5: when putting the files back fails, the in-memory rules still go back, and it says so.
    [TestMethod]
    public void A_rollback_that_cannot_write_a_file_still_puts_the_rules_back()
    {
        FileStream? held = null;
        try
        {
            var outcome = ScriptRunner.Run("t.cgs",
                ScriptFile.Parse("add validation Title is required in Book\nadd property Pages int to Book\nlist classes").Steps,
                _root,
                step =>
                {
                    if (((CommandStep)step).Text == "list classes")
                    {
                        // Book.cs changed on line 2; holding it open stops the restore writing it.
                        held = new FileStream(Path.Combine(_root, "Book.cs"), FileMode.Open, FileAccess.Read, FileShare.None);
                        return false;
                    }
                    return Execute(step);
                },
                _ => { }, () => true);

            StringAssert.Contains(outcome.Message, "Putting it back failed too");
            StringAssert.Contains(outcome.Message, "The validation rules and the current project were put back.");
            Assert.IsEmpty(ValidationRegistry.Get("Book"));
        }
        finally { held?.Dispose(); }
    }

    [TestMethod]
    public void A_snapshot_refused_runs_nothing()
    {
        var ran = false;

        var outcome = ScriptRunner.Run("t.cgs", ScriptFile.Parse("create class A").Steps, _root,
            _ => ran = true, _ => { }, confirmSnapshot: () => false);

        Assert.IsFalse(ran);
        Assert.IsFalse(outcome.Succeeded);
        Assert.AreEqual("Nothing was run.", outcome.Message);
    }

    [TestMethod]
    public void Load_reports_a_missing_file_an_empty_one_and_a_refusal()
    {
        Assert.AreEqual("No script at nope.cgs. `run` replays a .cgs script made with save session or by hand; see help run. Nothing was run.", RunCommand.Load("run nope", _root).Problem);   // M1

        File.WriteAllText(Path.Combine(_root, "empty.cgs"), "# nothing\n");
        Assert.AreEqual("empty.cgs has no commands. Nothing was run.", RunCommand.Load("run empty", _root).Problem);

        File.WriteAllText(Path.Combine(_root, "bad.cgs"), "create class A\nundo\n");
        Assert.AreEqual("Line 2: 'undo' can't be used in a script. Nothing was run.", RunCommand.Load("run bad", _root).Problem);

        Assert.AreEqual("Name the script to run, for example: run library.cgs", RunCommand.Load("run", _root).Problem);

        File.WriteAllText(Path.Combine(_root, "good.cgs"), "create class A\n");
        var good = RunCommand.Load("run good", _root);
        Assert.IsNull(good.Problem);
        Assert.AreEqual("good.cgs", good.Name);
        Assert.HasCount(1, good.Steps!);
    }
}
