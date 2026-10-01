using System.Text;
using CodeGenerationEngine.Helper;

namespace CodeGenerator.Tests.App.Scripts;

// Drives the real CodeGenerator.exe from the test bin with piped input: each session is a fresh
// process, which is exactly what "replay in a fresh session" means. No test constructs the
// console loop in-process — its constructor prints and opens the store.
[TestClass]
[DoNotParallelize]
public sealed class SessionScriptEndToEndTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "cg_e2e_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // One whole console session: the lines typed, then exit. Returns everything it printed.
    private string Session(params string[] lines)
    {
        var output = new StringBuilder();
        var result = ProcessRunner.RunStreaming(
            Path.Combine(AppContext.BaseDirectory, "CodeGenerator.exe"),
            string.Empty,
            line => { lock (output) output.AppendLine(line); },
            workingDirectory: _root,
            timeout: TimeSpan.FromMinutes(3),
            standardInput: string.Join("\n", lines.Append("exit")) + "\n",
            environment: new Dictionary<string, string> { ["PROJECTPATH"] = _root },
            outputEncoding: new UTF8Encoding(false));

        Assert.IsFalse(result.TimedOut, output.ToString());
        return output.ToString();
    }

    private string Book => Path.Combine(_root, "Book.cs");

    [TestMethod]
    public void A_saved_session_rebuilds_the_same_files_in_a_fresh_one()
    {
        File.WriteAllText(Book, "namespace S { public class Book { } }");

        var first = Session(
            "add property Pages int to Book",
            "add propery Title to Book",          // typo: not recognised, must not be saved
            "add property Pages int to Ghost",    // fails: "Class 'Ghost' not found."
            "help",
            "add property Title string to Book",
            "save session library");

        StringAssert.Contains(first, "Saved 2 commands to library.cgs.");
        var built = File.ReadAllText(Book);

        File.WriteAllText(Book, "namespace S { public class Book { } }");
        var second = Session("run library");

        StringAssert.Contains(second, "[1/2] add property Pages int to Book");
        StringAssert.Contains(second, "Ran 2 commands from library.cgs.");
        Assert.AreEqual(built, File.ReadAllText(Book));
    }

    [TestMethod]
    public void Undo_after_a_run_reverses_all_of_it_and_drops_it_from_the_save()
    {
        File.WriteAllText(Book, "namespace S { public class Book { } }");
        File.WriteAllText(Path.Combine(_root, "two.cgs"),
            "add property Pages int to Book\nadd property Title string to Book\n");

        var output = Session("run two", "undo", "save session after");

        StringAssert.Contains(output, "Undid: run two");
        Assert.AreEqual("namespace S { public class Book { } }", File.ReadAllText(Book));
        StringAssert.Contains(output, "Nothing to save — no command in this session changed anything.");
    }

    [TestMethod]
    public void A_saved_run_is_written_as_the_lines_it_ran()
    {
        File.WriteAllText(Book, "namespace S { public class Book { } }");
        File.WriteAllText(Path.Combine(_root, "one.cgs"), "add property Pages int to Book\n");

        Session("run one", "save session copy");

        CollectionAssert.AreEqual(new[] { "add property Pages int to Book" },
            File.ReadAllLines(Path.Combine(_root, "copy.cgs")).Skip(1).ToArray());
    }

    [TestMethod]
    public void A_refused_script_runs_nothing()
    {
        File.WriteAllText(Book, "namespace S { public class Book { } }");
        File.WriteAllText(Path.Combine(_root, "bad.cgs"), "add property Pages int to Book\nundo\n");

        var output = Session("run bad");

        StringAssert.Contains(output, "Line 2: 'undo' can't be used in a script. Nothing was run.");
        Assert.AreEqual("namespace S { public class Book { } }", File.ReadAllText(Book));
    }
}
