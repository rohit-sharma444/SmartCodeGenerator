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
    public void The_command_history_file_is_never_journalled_and_never_blocks_an_undo()
    {
        Write(".codegen_history", "run b2\n");

        // A script's build step appends its piped lines while the command runs...
        Command("run b2", () =>
        {
            Write("Shop/Book.cs", "class Book { int Pages; }");
            File.AppendAllText(P(".codegen_history"), "build app from csv books.csv\nexit\n");
        });

        // ...and typing undo appends again before the undo runs.
        Tick();
        File.AppendAllText(P(".codegen_history"), "undo\n");

        var undone = _journal.Undo();

        Assert.IsTrue(undone.Done, undone.Message);
        Assert.AreEqual(@"Undid: run b2 — restored Shop\Book.cs", undone.Message);
        Assert.AreEqual("run b2\nbuild app from csv books.csv\nexit\nundo\n", Read(".codegen_history"));
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

    [TestMethod]
    public void A_state_only_command_is_undone_and_named_without_files()
    {
        var registry = Path.Combine(_shadowParent, "ProjectManager.dat");
        File.WriteAllText(registry, "one");
        var state = new FakeState();
        using var journal = new UndoJournal(_root, registryPath: registry, captureState: state.Capture,
            shadowParent: _shadowParent);

        using (journal.Begin("use project Shop")) { state.Value = "Shop"; File.WriteAllText(registry, "two"); }

        var result = journal.Undo();
        Assert.AreEqual("Undid: use project Shop", result.Message);
        Assert.IsNull(state.Value);
        Assert.AreEqual("one", File.ReadAllText(registry));

        journal.Redo();
        Assert.AreEqual("Shop", state.Value);
        Assert.AreEqual("two", File.ReadAllText(registry));
    }

    [TestMethod]
    public void A_shadow_copy_that_failed_part_way_is_taken_again_and_never_costs_a_file()
    {
        // Sorts first, so the very first copy fails and nothing else is mirrored.
        Write("Shop/Aaa.cs", "class Aaa { }");
        using (new FileStream(P("Shop/Aaa.cs"), FileMode.Open, FileAccess.Read, FileShare.None))
            Command("while locked", () => { });

        Command("edit Book", () => Write("Shop/Book.cs", "class Book { int Pages; }"));

        Assert.IsTrue(_journal.Undo().Done);
        Assert.AreEqual("class Book { }", Read("Shop/Book.cs"));
    }

    [TestMethod]
    public void An_undo_that_fails_part_way_puts_back_what_it_did_and_can_be_tried_again()
    {
        Command("edit two", () =>
        {
            Write("Shop/Book.cs", "class Book { int Pages; }");
            Write("Shop/New.cs", "class New { }");
        });

        // Book.cs is restored first; New.cs then cannot be deleted while it is open.
        using (new FileStream(P("Shop/New.cs"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var refused = _journal.Undo();
            Assert.IsFalse(refused.Done);
            StringAssert.StartsWith(refused.Message, "Can't undo edit two:");
            Assert.AreEqual("class Book { int Pages; }", Read("Shop/Book.cs"));
        }

        Assert.IsTrue(_journal.Undo().Done);
        Assert.AreEqual("class Book { }", Read("Shop/Book.cs"));
        Assert.IsFalse(File.Exists(P("Shop/New.cs")));
    }

    [TestMethod]
    public void Undo_refuses_to_delete_a_created_folder_that_now_holds_a_git_repository()
    {
        Command("build app", () => Write("MyApp/Program.cs", "class P { }"));
        Write("MyApp/.git/HEAD", "ref: refs/heads/main");

        var refused = _journal.Undo();

        Assert.IsFalse(refused.Done);
        StringAssert.Contains(refused.Message, ".git");
        Assert.IsTrue(File.Exists(P("MyApp/.git/HEAD")));
    }

    [TestMethod]
    public void A_command_still_running_when_the_journal_is_disposed_leaves_no_shadow_behind()
    {
        var scope = _journal.Begin("slow");
        Write("Shop/Book.cs", "class Book { int Pages; }");
        _journal.Dispose();
        scope.Dispose();

        Assert.IsEmpty(Directory.GetFileSystemEntries(_shadowParent));
    }

    [TestMethod]
    public void Undo_and_redo_reload_the_project_list_after_writing_its_file()
    {
        var registry = Path.Combine(_shadowParent, "ProjectManager.dat");
        File.WriteAllText(registry, "one");
        var reloads = new List<string>();
        using var journal = new UndoJournal(_root, registryPath: registry, shadowParent: _shadowParent,
            reloadRegistry: () => reloads.Add(File.ReadAllText(registry)));

        using (journal.Begin("create project Foo")) File.WriteAllText(registry, "two");

        journal.Undo();
        journal.Redo();
        CollectionAssert.AreEqual(new[] { "one", "two" }, reloads);
    }

    [TestMethod]
    public void Undo_refuses_when_the_project_list_changed_since()
    {
        var registry = Path.Combine(_shadowParent, "ProjectManager.dat");
        File.WriteAllText(registry, "one");
        using var journal = new UndoJournal(_root, registryPath: registry, shadowParent: _shadowParent);

        using (journal.Begin("create project Foo")) File.WriteAllText(registry, "two");
        File.WriteAllText(registry, "three");   // another session's command

        Assert.AreEqual("Can't undo: the project list has changed since create project Foo.", journal.Undo().Message);
        Assert.AreEqual("three", File.ReadAllText(registry));
    }

    [TestMethod]
    public void Undo_refuses_when_the_state_it_would_put_back_changed_since()
    {
        var state = new FakeState();
        using var journal = new UndoJournal(_root, captureState: state.Capture, shadowParent: _shadowParent);

        using (journal.Begin("use project Shop")) state.Value = "Shop";
        state.Value = "Other";   // another session's command

        Assert.AreEqual("Can't undo: the state has changed since use project Shop.", journal.Undo().Message);
        Assert.AreEqual("Other", state.Value);
    }

    private sealed class FakeState
    {
        public string? Value;
        public ISessionState Capture() => new Snap(this, Value);
        private sealed record Snap(FakeState Owner, string? Value) : ISessionState
        {
            public bool SameAs(ISessionState other) => other is Snap s && s.Value == Value;
            public string? Conflict(ISessionState replacing) =>
                replacing is Snap s && s.Value != Value && Owner.Value != s.Value ? "the state" : null;
            public void Restore(ISessionState replacing) => Owner.Value = Value;
        }
    }
}
