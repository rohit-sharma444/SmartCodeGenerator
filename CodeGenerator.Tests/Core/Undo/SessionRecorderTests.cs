using CodeGenerationEngine.Core.Undo;

namespace CodeGenerator.Tests.Core.Undo;

[TestClass]
public sealed class SessionRecorderTests
{
    private string _root = string.Empty;
    private string _shadowParent = string.Empty;
    private UndoJournal _journal = null!;
    private SessionRecorder _recorder = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "cg_rec_ws_" + Guid.NewGuid().ToString("N"));
        _shadowParent = Path.Combine(Path.GetTempPath(), "cg_rec_sh_" + Guid.NewGuid().ToString("N"));
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

    // A command that writes a file, so the journal makes an entry; kept lines as given.
    private void Command(string label, bool succeeded = true, params string[] kept)
    {
        using var scope = _journal.Begin(label);
        Thread.Sleep(20);
        File.WriteAllText(Path.Combine(_root, Guid.NewGuid().ToString("N") + ".cs"), label);
        if (succeeded)
            scope.Keep(kept.Length > 0 ? kept : [label]);
    }

    [TestMethod]
    public void Commands_that_changed_something_are_kept_in_order()
    {
        Command("create class Book");
        Command("create class Member");

        CollectionAssert.AreEqual(new[] { "create class Book", "create class Member" }, _recorder.Lines.ToArray());
    }

    [TestMethod]
    public void A_command_that_changed_nothing_is_not_kept()
    {
        using (var scope = _journal.Begin("list classes"))
            scope.Keep(["list classes"]);

        Assert.IsEmpty(_recorder.Lines);
    }

    [TestMethod]
    public void A_failed_command_that_still_wrote_a_file_is_not_kept_but_undo_stays_in_step()
    {
        Command("create class Book");
        Command("add propery Pages to Book", succeeded: false);

        Assert.IsTrue(_journal.Undo().Done);   // undoes the failed one
        CollectionAssert.AreEqual(new[] { "create class Book" }, _recorder.Lines.ToArray());

        Assert.IsTrue(_journal.Undo().Done);   // and now the real one
        Assert.IsEmpty(_recorder.Lines);       // Review Focus 2
    }

    [TestMethod]
    public void Undo_drops_a_command_and_redo_puts_it_back_in_place()
    {
        Command("create class Book");
        Command("create class Member");

        _journal.Undo();
        CollectionAssert.AreEqual(new[] { "create class Book" }, _recorder.Lines.ToArray());

        _journal.Redo();
        CollectionAssert.AreEqual(new[] { "create class Book", "create class Member" }, _recorder.Lines.ToArray());
    }

    [TestMethod]
    public void A_new_command_after_an_undo_means_the_undone_one_can_never_come_back()
    {
        Command("create class Book");
        Command("create class Member");
        _journal.Undo();
        Command("create class Loan");

        Assert.IsFalse(_journal.Redo().Done);
        CollectionAssert.AreEqual(new[] { "create class Book", "create class Loan" }, _recorder.Lines.ToArray());
    }

    [TestMethod]
    public void One_entry_can_keep_several_lines()
    {
        Command("run library.cgs", kept: ["create class Book", "add property Pages int to Book"]);

        CollectionAssert.AreEqual(new[] { "create class Book", "add property Pages int to Book" },
            _recorder.Lines.ToArray());
    }

    [TestMethod]
    public void A_nested_scope_adds_to_the_outer_entry()
    {
        using (var outer = _journal.Begin("batch"))
        {
            using (var inner = _journal.Begin("create class Book"))
            {
                Thread.Sleep(20);
                File.WriteAllText(Path.Combine(_root, "Book.cs"), "x");
                inner.Keep(["create class Book"]);
            }
            outer.Keep(["create class Member"]);
        }

        CollectionAssert.AreEqual(new[] { "create class Book", "create class Member" }, _recorder.Lines.ToArray());
        Assert.IsTrue(_journal.Undo().Done);
        Assert.IsEmpty(_recorder.Lines);
    }

    [TestMethod]
    public void A_command_the_journal_could_not_record_is_still_kept_and_undo_never_drops_it()
    {
        // Sorts first, so the very first shadow copy fails and the command goes unjournalled.
        File.WriteAllText(Path.Combine(_root, "Aaa.cs"), "x");
        using (new FileStream(Path.Combine(_root, "Aaa.cs"), FileMode.Open, FileAccess.Read, FileShare.None))
        using (var scope = _journal.Begin("create class Book"))
        {
            File.WriteAllText(Path.Combine(_root, "Book.cs"), "b");
            scope.Keep(["create class Book"]);
        }

        Command("create class Member");
        CollectionAssert.AreEqual(new[] { "create class Book", "create class Member" }, _recorder.Lines.ToArray());

        Assert.IsTrue(_journal.Undo().Done);   // undoes Member, the only entry
        CollectionAssert.AreEqual(new[] { "create class Book" }, _recorder.Lines.ToArray());

        _journal.Redo();
        CollectionAssert.AreEqual(new[] { "create class Book", "create class Member" }, _recorder.Lines.ToArray());
    }

    [TestMethod]
    public void With_undo_off_every_kept_command_is_kept_and_it_says_so()
    {
        using var journal = new UndoJournal(_root, shadowParent: _shadowParent, maxFiles: 0);
        var recorder = new SessionRecorder(journal);
        File.WriteAllText(Path.Combine(_root, "Book.cs"), "x");   // over a zero-file limit

        using (var scope = journal.Begin("create class Member"))
            scope.Keep(["create class Member"]);
        using (var scope = journal.Begin("list classes")) { }

        Assert.IsTrue(recorder.UndoWasOff);
        CollectionAssert.AreEqual(new[] { "create class Member" }, recorder.Lines.ToArray());
    }
}
