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
