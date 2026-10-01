using CodeGenerationEngine.Core.Undo;
using CodeGenerator.App.Scripts;

namespace CodeGenerator.Tests.App.Scripts;

[TestClass]
public sealed class SaveSessionCommandTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 10, 42, 0);
    private string _root = string.Empty;
    private string _shadowParent = string.Empty;
    private UndoJournal _journal = null!;
    private SessionRecorder _recorder = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "cg_save_" + Guid.NewGuid().ToString("N"));
        _shadowParent = _root + "_sh";
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_shadowParent);
        _journal = new UndoJournal(_root, shadowParent: _shadowParent);
        _recorder = new SessionRecorder(_journal);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _journal.Dispose();
        foreach (var d in new[] { _root, _shadowParent })
            try { Directory.Delete(d, recursive: true); } catch (IOException) { }
    }

    private void Command(string line)
    {
        using var scope = _journal.Begin(line);
        Thread.Sleep(20);
        File.WriteAllText(Path.Combine(_root, Guid.NewGuid().ToString("N") + ".cs"), line);
        scope.Keep([line.StartsWith("build app") ? ScriptNotes.InteractiveBuildApp(line) : line]);
    }

    private SaveResult Save(string input) => SaveSessionCommand.Handle(input, _root, _recorder, Now);

    [TestMethod]
    public void Writes_a_header_then_the_commands()
    {
        Command("create class Book");
        Command("add property Pages int to Book");

        var result = Save("save session library");

        Assert.IsTrue(result.Saved);
        Assert.AreEqual("Saved 2 commands to library.cgs.", result.Message);
        CollectionAssert.AreEqual(
            new[] { $"# Saved 2026-10-01 10:42 from {_root}", "create class Book", "add property Pages int to Book" },
            File.ReadAllLines(Path.Combine(_root, "library.cgs")));
    }

    [TestMethod]
    public void The_file_is_utf8_without_a_byte_order_mark()
    {
        Command("create class Café");

        Save("save session library");

        var bytes = File.ReadAllBytes(Path.Combine(_root, "library.cgs"));
        Assert.AreNotEqual(0xEF, bytes[0]);
        StringAssert.Contains(File.ReadAllText(Path.Combine(_root, "library.cgs")), "Café");
    }

    [TestMethod]
    public void Nothing_kept_means_nothing_written()   // Review Focus 2
    {
        Command("create class Book");
        _journal.Undo();

        var result = Save("save session library");

        Assert.IsFalse(result.Saved);
        Assert.AreEqual("Nothing to save — no command in this session changed anything.", result.Message);
        Assert.IsFalse(File.Exists(Path.Combine(_root, "library.cgs")));
    }

    [TestMethod]
    public void An_existing_file_is_kept_unless_replace_is_given()
    {
        Command("create class Book");
        File.WriteAllText(Path.Combine(_root, "library.cgs"), "my recipe");

        var refused = Save("save session library.cgs");
        Assert.IsFalse(refused.Saved);
        Assert.AreEqual("library.cgs already exists. Add 'replace' to overwrite it: save session library.cgs replace",
            refused.Message);
        Assert.AreEqual("my recipe", File.ReadAllText(Path.Combine(_root, "library.cgs")));

        Assert.IsTrue(Save("save session library.cgs replace").Saved);
        StringAssert.Contains(File.ReadAllText(Path.Combine(_root, "library.cgs")), "create class Book");
    }

    [TestMethod]
    public void A_missing_folder_is_created()
    {
        Command("create class Book");

        Assert.IsTrue(Save(@"save session scripts\library").Saved);
        Assert.IsTrue(File.Exists(Path.Combine(_root, "scripts", "library.cgs")));
    }

    [TestMethod]
    public void No_file_name_says_how()
    {
        Assert.AreEqual("Name the file to save to, for example: save session library.cgs", Save("save session").Message);
    }

    [TestMethod]
    public void An_interactive_build_is_a_note_with_its_secret_masked()
    {
        Command("build app from sql \"Server=.;Database=Shop;User Id=sa;Password=hunter2\" tables Orders");

        Save("save session shop");

        var text = File.ReadAllText(Path.Combine(_root, "shop.cgs"));
        StringAssert.Contains(text, "# build app from sql");
        StringAssert.Contains(text, "(built interactively; add its answers below to replay it)");
        Assert.DoesNotContain("hunter2", text);
    }

    [TestMethod]
    public void Only_notes_still_saves_and_counts_them_apart()
    {
        Command("build app: a library");

        // M6: run refuses a file with no commands, so the message says so rather than counting zero.
        Assert.AreEqual("Saved 1 note to shop.cgs. It has no commands yet, so run will refuse it until you add some.",
            Save("save session shop").Message);
    }
}
