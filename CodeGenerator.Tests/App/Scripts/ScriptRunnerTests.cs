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
            new[] { "[1/2] add property Pages int to Book", "[2/2] add property Title string to Book" }, said.ToArray());
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
        Assert.AreEqual("No script at nope.cgs. Nothing was run.", RunCommand.Load("run nope", _root).Problem);

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
