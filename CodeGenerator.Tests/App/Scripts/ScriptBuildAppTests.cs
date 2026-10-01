using CodeGenerator.App.Scripts;

namespace CodeGenerator.Tests.App.Scripts;

// Starts the real CodeGenerator.exe from the test bin; ~12 s. Serial: the copy writes the
// project registry beside the exe, which other tests read.
[TestClass]
[DoNotParallelize]
public sealed class ScriptBuildAppTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        // Short: generated apps nest deep, and a long temp path breaks MAX_PATH.
        _root = Path.Combine(Path.GetTempPath(), "sb" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "books.csv"), "Id,Title,Pages\n1,Dune,412\n2,Emma,320\n");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public void A_piped_build_block_generates_the_app_and_says_it_worked()
    {
        var output = new List<string>();

        var ok = ScriptBuildApp.Run(
            ["build app from csv " + Path.Combine(_root, "books.csv"), "approve console"], _root, output.Add);

        Assert.IsTrue(ok, string.Join(Environment.NewLine, output));
        Assert.IsTrue(Directory.Exists(Path.Combine(_root, "Books.Console")));
        Assert.IsTrue(output.Any(l => l.Contains(ScriptBuildApp.SuccessMarker, StringComparison.Ordinal)));
    }

    // S1: the copy's piped lines (and its "exit") are not what the user typed; writing them to the
    // workspace's history polluted the up-arrow and made the run that started it impossible to undo.
    [TestMethod]
    public void The_copy_writes_nothing_to_the_command_history()
    {
        var history = Path.Combine(_root, ".codegen_history");
        File.WriteAllText(history, "run b2\n");

        var ok = ScriptBuildApp.Run(
            ["build app from csv " + Path.Combine(_root, "books.csv"), "approve console"], _root, _ => { });

        Assert.IsTrue(ok);
        Assert.AreEqual("run b2\n", File.ReadAllText(history));
    }

    [TestMethod]
    public void A_build_that_generates_nothing_fails()
    {
        var output = new List<string>();

        var ok = ScriptBuildApp.Run(
            ["build app from csv " + Path.Combine(_root, "missing.csv"), "approve console"], _root, output.Add);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void A_cancelled_build_is_not_a_failure()
    {
        var ok = ScriptBuildApp.Run(
            ["build app from csv " + Path.Combine(_root, "books.csv"), "cancel"], _root, _ => { });

        Assert.IsTrue(ok);
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "Books.Console")));
    }

    // The build never reached its review, so the answer lines ran as commands of their own in the
    // copy — and a "cancel" success would keep whatever they did. Only a real cancel succeeds.
    [TestMethod]
    public void A_cancel_that_never_reached_the_review_fails()
    {
        File.WriteAllText(Path.Combine(_root, "Keep.cs"), "namespace S { public class Keep { } }");

        var ok = ScriptBuildApp.Run(
            ["build app from csv " + Path.Combine(_root, "missing.csv"), "add property Pages int to Keep", "cancel"],
            _root, _ => { });

        Assert.IsFalse(ok);
    }

    // S2: the review ended on "a", so the copy ran the next lines as commands of its own — one
    // failed — and the block used to pass because "Generated project at" appeared somewhere.
    [TestMethod]
    public void Lines_the_copy_ran_after_its_build_had_ended_fail_the_block()
    {
        var output = new List<string>();

        var ok = ScriptBuildApp.Run(
            ["build app from csv " + Path.Combine(_root, "books.csv"), "a console", "add property X int to Nope", "cancel"],
            _root, output.Add);

        Assert.IsFalse(ok, string.Join(Environment.NewLine, output));
        StringAssert.Contains(string.Join(Environment.NewLine, output),
            "'add property X int to Nope' ran after the build had ended");
    }

    // M2: a review answer the build refuses means the app is not the one the script describes.
    [TestMethod]
    public void A_refused_review_answer_fails_the_block()
    {
        var output = new List<string>();

        var ok = ScriptBuildApp.Run(
            ["build app from csv " + Path.Combine(_root, "books.csv"), "rename Ghost to Spirit", "approve console"],
            _root, output.Add);

        Assert.IsFalse(ok, string.Join(Environment.NewLine, output));
        Assert.IsTrue(output.Any(l => l.Contains(ScriptBuildApp.RefusedMarker, StringComparison.Ordinal)));
    }

    // M3: a script never replaces an app that is already there — it may have been edited by hand.
    [TestMethod]
    public void An_app_that_already_exists_is_not_replaced()
    {
        string[] block = ["build app from csv " + Path.Combine(_root, "books.csv"), "approve console"];
        Assert.IsTrue(ScriptBuildApp.Run(block, _root, _ => { }));
        var program = Directory.GetFiles(Path.Combine(_root, "Books.Console"), "*.cs").First();
        File.AppendAllText(program, "// edited by hand");

        var output = new List<string>();
        var ok = ScriptBuildApp.Run(block, _root, output.Add);

        Assert.IsFalse(ok);
        StringAssert.Contains(string.Join(Environment.NewLine, output), "already exists, and a script never replaces an app");
        StringAssert.EndsWith(File.ReadAllText(program), "// edited by hand");
    }

    [TestMethod]
    public void A_tool_that_cannot_start_fails_and_says_why()
    {
        var output = new List<string>();

        var ok = ScriptBuildApp.Run(["build app: x", "approve"], _root, output.Add,
            executable: Path.Combine(_root, "no-such-tool.exe"));

        Assert.IsFalse(ok);
        StringAssert.StartsWith(output.Single(), "Could not start a copy of the tool:");
    }
}
