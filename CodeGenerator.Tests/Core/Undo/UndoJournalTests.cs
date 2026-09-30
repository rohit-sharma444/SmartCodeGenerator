using CodeGenerationEngine.Core.Undo;

namespace CodeGenerator.Tests.Core.Undo;

[TestClass]
public sealed class UndoJournalTests
{
    private string _root = string.Empty;
    private string _shadowParent = string.Empty;
    private UndoJournal _journal = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "cg_undo_ws_" + Guid.NewGuid().ToString("N"));
        _shadowParent = Path.Combine(Path.GetTempPath(), "cg_undo_sh_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_shadowParent);
        Write("Shop/Book.cs", "class Book { }");
        Write("Shop/Old.cs", "class Old { }");
        _journal = new UndoJournal(_root, shadowParent: _shadowParent);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _journal.Dispose();
        foreach (var d in new[] { _root, _shadowParent })
            try { Directory.Delete(d, recursive: true); } catch (IOException) { }
    }

    private string P(string rel) => Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
    private void Write(string rel, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(P(rel))!);
        File.WriteAllText(P(rel), text);
    }
    private string Read(string rel) => File.ReadAllText(P(rel));

    // Stamps can tie within one tick on a fast disk; a real command is never that quick, but a test is.
    private static void Tick() => Thread.Sleep(20);

    private void Command(string label, Action act)
    {
        using (_journal.Begin(label)) { Tick(); act(); }
    }

    [TestMethod]
    public void Undo_reverses_an_add_a_change_and_a_delete_and_redo_puts_them_back()
    {
        Command("edit things", () =>
        {
            Write("Shop/Book.cs", "class Book { int Pages; }");
            Write("Shop/New.cs", "class New { }");
            File.Delete(P("Shop/Old.cs"));
        });

        var undone = _journal.Undo();
        Assert.IsTrue(undone.Done);
        Assert.AreEqual("class Book { }", Read("Shop/Book.cs"));
        Assert.IsFalse(File.Exists(P("Shop/New.cs")));
        Assert.AreEqual("class Old { }", Read("Shop/Old.cs"));
        Assert.AreEqual("Undid: edit things — restored 2 files, removed 1", undone.Message);

        var redone = _journal.Redo();
        Assert.IsTrue(redone.Done);
        Assert.AreEqual("class Book { int Pages; }", Read("Shop/Book.cs"));
        Assert.AreEqual("class New { }", Read("Shop/New.cs"));
        Assert.IsFalse(File.Exists(P("Shop/Old.cs")));
    }

    [TestMethod]
    public void One_changed_file_is_named()
    {
        Command("add property Pages to Book", () => Write("Shop/Book.cs", "class Book { int Pages; }"));

        Assert.AreEqual(@"Undid: add property Pages to Book — restored Shop\Book.cs", _journal.Undo().Message);
    }

    [TestMethod]
    public void A_command_that_changes_nothing_makes_no_entry()
    {
        Command("show classes", () => { });

        Assert.IsFalse(_journal.CanUndo);
        Assert.AreEqual("Nothing to undo.", _journal.Undo().Message);
    }

    [TestMethod]
    public void A_new_command_after_an_undo_clears_redo()
    {
        Command("first", () => Write("Shop/Book.cs", "1"));
        _journal.Undo();
        Command("second", () => Write("Shop/Book.cs", "2"));

        Assert.IsFalse(_journal.CanRedo);
        Assert.AreEqual("Nothing to redo.", _journal.Redo().Message);
    }

    [TestMethod]
    public void The_history_keeps_the_latest_fifty()
    {
        for (var i = 0; i < UndoJournal.Capacity + 5; i++)
            Command("c" + i, () => Write("Shop/Book.cs", i.ToString()));

        var undone = 0;
        while (_journal.Undo().Done) undone++;

        Assert.AreEqual(UndoJournal.Capacity, undone);
        Assert.AreEqual("4", Read("Shop/Book.cs"));   // the state before c5, the oldest kept
    }

    [TestMethod]
    public void An_outside_edit_between_commands_is_neither_undone_nor_credited()   // Review Focus 1
    {
        Command("first", () => Write("Shop/Book.cs", "by the tool"));
        Tick();
        Write("Shop/Notes.txt", "mine");                     // edited outside the tool
        Command("second", () => Write("Shop/Old.cs", "changed"));

        _journal.Undo();                                     // undoes "second" only

        Assert.AreEqual("mine", Read("Shop/Notes.txt"));
        Assert.AreEqual("class Old { }", Read("Shop/Old.cs"));
        Assert.AreEqual("by the tool", Read("Shop/Book.cs"));
    }

    [TestMethod]
    public void Undo_refuses_when_you_edited_a_file_it_would_restore()   // Review Focus 2
    {
        Command("add property Pages to Book", () => Write("Shop/Book.cs", "tool"));
        Tick();
        Write("Shop/Book.cs", "my own edit");

        var result = _journal.Undo();

        Assert.IsFalse(result.Done);
        Assert.AreEqual(@"Can't undo: Shop\Book.cs has changed since add property Pages to Book.", result.Message);
        Assert.AreEqual("my own edit", Read("Shop/Book.cs"));
        Assert.IsTrue(_journal.CanUndo, "a refused undo keeps the entry");
    }

    [TestMethod]
    public void Redo_refuses_when_you_edited_a_file_since_the_undo()
    {
        Command("x", () => Write("Shop/Book.cs", "tool"));
        _journal.Undo();
        Tick();
        Write("Shop/Book.cs", "mine");

        var result = _journal.Redo();

        Assert.IsFalse(result.Done);
        Assert.AreEqual(@"Can't redo: Shop\Book.cs has changed since you undid x.", result.Message);
    }

    [TestMethod]
    public void Undo_of_a_command_that_created_folders_removes_them()   // Review Focus 3
    {
        Command("build app: a library", () =>
        {
            Write("Library/Models/Book.cs", "class Book { }");
            Write("Library/Library.csproj", "<Project />");
            Directory.CreateDirectory(P("Library/bin/Debug"));   // excluded, but inside a created folder
            File.WriteAllText(P("Library/bin/Debug/x.dll"), "binary");
        });

        _journal.Undo();

        Assert.IsFalse(Directory.Exists(P("Library")), "the whole created tree goes, bin included");
    }

    [TestMethod]
    public void A_nested_begin_is_part_of_the_outer_entry()   // Review Focus 4
    {
        using (_journal.Begin("batch"))
        {
            Tick();
            Write("Shop/Book.cs", "a");
            using (_journal.Begin("inner command"))
                Write("Shop/Old.cs", "b");
        }

        Assert.AreEqual("batch", _journal.NextUndoLabel);
        _journal.Undo();
        Assert.IsFalse(_journal.CanUndo);
        Assert.AreEqual("class Old { }", Read("Shop/Old.cs"));
    }

    [TestMethod]
    public void Excluded_folders_are_never_journalled()
    {
        Command("build", () =>
        {
            Directory.CreateDirectory(P("Shop/bin"));
            File.WriteAllText(P("Shop/bin/a.dll"), "x");
            Directory.CreateDirectory(P(".codegen"));
            File.WriteAllText(P(".codegen/phrase-candidates.jsonl"), "{}");
        });

        Assert.IsFalse(_journal.CanUndo);
    }

    [TestMethod]
    public void Over_the_size_limit_it_switches_off_and_says_so_once()
    {
        var notices = new List<string>();
        using var journal = new UndoJournal(_root, notices.Add, shadowParent: _shadowParent, maxFiles: 1);

        using (journal.Begin("a")) Write("Shop/Book.cs", "1");
        using (journal.Begin("b")) Write("Shop/Book.cs", "2");

        Assert.IsFalse(journal.Enabled);
        Assert.HasCount(1, notices);
        StringAssert.Contains(notices[0], "Undo is off for this session");
        StringAssert.Contains(journal.Undo().Message, "Undo is off for this session");
    }

    [TestMethod]
    public void Dispose_deletes_the_shadow()
    {
        Command("x", () => Write("Shop/Book.cs", "1"));
        _journal.Dispose();

        Assert.IsEmpty(Directory.GetFileSystemEntries(_shadowParent));
    }
}
