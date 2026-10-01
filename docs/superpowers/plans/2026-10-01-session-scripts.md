# Session scripts (`run` / `save session`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `save session <file>` writes the commands of this session that worked and changed something to a `.cgs` file; `run <file>` replays one, all or nothing, on the console and the chat page.

**Architecture:** A pure `ScriptFile` parser turns text into steps. `ScriptRunner` runs steps under one `WorkspaceSnapshot`, restoring it and the in-memory `SessionState` on the first failure; each surface supplies the "run one step" delegate. A `build app` step runs in a separate copy of the tool with its lines piped in (`ScriptBuildApp`). The undo journal learns, per entry, which lines `save session` should write (`UndoScope.Keep`), and raises events that `SessionRecorder` mirrors, so an undone command drops out of the saved file.

**Tech Stack:** C# / .NET 10, MSTest + bUnit (CodeGenerator.Tests), Blazor Server (chat page).

**Spec:** `docs/superpowers/specs/2026-10-01-session-scripts-design.md`

## Global Constraints

- Build gate every task: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror -m:1` from `D:\SmartCodeGenerator-Repository` — 0 warnings, 0 errors.
- Per task: focused `dotnet test CodeGenerator.Tests -c Release --no-build --filter ...` only. The full suite runs once, after Task 8 (`-m:1`, after `dotnet build-server shutdown`; memory is short on this machine).
- Every new test must be seen failing before its code is written.
- Script file extension: `.cgs`. Encoding written: UTF-8 without BOM.
- Messages, verbatim from the spec:
  - `Line {n}: '{text}' can't be used in a script. Nothing was run.`
  - `Line {n}: 'build app' needs an 'approve …' or 'cancel' line after it. Nothing was run.`
  - `Line {n} failed. Everything the script did was put back.` (with `: {reason}` before the full stop when an exception gave one)
  - `Ran {n} command(s) from {name}.`
  - `Nothing to save — no command in this session changed anything.`
  - `{name} already exists. Add 'replace' to overwrite it: save session {arg} replace`
  - `Saved {n} command(s) to {name}.`
  - Header: `# Saved {yyyy-MM-dd HH:mm} from {workspace root}`; off note: `# Undo was off in this session, so this keeps every command that succeeded.`
  - Interactive build note: `# {build app line, secrets masked} (built interactively; add its answers below to replay it)`
- Script lines run **without the AI resolver and without confirm prompts**, exactly as `batch` and `RunOnce` run them: a script must not stop to ask.
- **Ruling (deviation from the spec, small):** besides the kinds the spec lists, `help`, `status`, `history`, `clear`/`cls` and `ai …` are refused in a script too. They change nothing, and only `Pipeline` and `BuildApp` steps have an executor; sending them down the pipeline would misreport. Cost if wrong: a demo script cannot print help.
- Do not use bash heredocs or `printf` to write C# or paths with backslashes (they have corrupted files on this machine). Use the editor tools.

## Review Focus

1. A script saved on one machine and run where a named file or class does not exist → line fails, everything rolled back, the line number named.
2. `save session` after `undo` of the only command → `Nothing to save …`, no file written (pinned in Task 2's recorder tests and Task 3).
3. A script file with Windows line endings, a UTF-8 BOM, trailing spaces and `#` comments indented → parsed the same as a clean one (Task 1).
4. `run` of a file name with spaces in double quotes, and with no extension → resolves to `<root>\my lib.cgs` (Task 3's `ScriptPath` tests).
5. A failing line that had already added a validation rule in memory → the rule is gone after the rollback, not only the files (Task 5).

---

### Task 1: Parse a script, and route `run` / `save session`

**Files:**
- Create: `CodeGenerator/App/Scripts/ScriptFile.cs`
- Create: `CodeGenerator/App/Scripts/RunCommand.cs` (recognition only in this task)
- Create: `CodeGenerator/App/Scripts/SaveSessionCommand.cs` (recognition only in this task)
- Modify: `CodeGenerator/App/CommandKind.cs` (add `Run`, `SaveSession` before `Pipeline`)
- Modify: `CodeGenerator/App/CommandRoute.cs` (classify both, just before `Batch`)
- Test: `CodeGenerator.Tests/App/Scripts/ScriptFileTests.cs`, `CodeGenerator.Tests/App/CommandRouteTests.cs`

**Interfaces:**
- Produces:
  - `abstract record ScriptStep(int Line)`; `sealed record CommandStep(int Line, string Text) : ScriptStep(Line)`; `sealed record BuildAppStep(int Line, IReadOnlyList<string> Lines) : ScriptStep(Line)`
  - `sealed record ScriptParse(IReadOnlyList<ScriptStep> Steps, string? Refusal)`
  - `static class ScriptFile { const string Extension = ".cgs"; static ScriptParse Parse(string text); static bool EndsBuild(string line); }`
  - `RunCommand.IsRun(string?)`, `SaveSessionCommand.IsSaveSession(string?)`
  - `CommandKind.Run`, `CommandKind.SaveSession`
  - All `internal`, namespace `CodeGenerator.App.Scripts` (kinds stay in `CodeGenerator.App`).

- [ ] **Step 1: Write the failing tests**

`CodeGenerator.Tests/App/Scripts/ScriptFileTests.cs`:

```csharp
using CodeGenerator.App.Scripts;

namespace CodeGenerator.Tests.App.Scripts;

[TestClass]
public sealed class ScriptFileTests
{
    [TestMethod]
    public void Commands_are_read_in_order_with_their_line_numbers_skipping_blanks_and_comments()
    {
        var parse = ScriptFile.Parse("# a library\r\n\r\ncreate class Book\r\n   # indented note\r\nadd property Pages int to Book  \r\n");

        Assert.IsNull(parse.Refusal);
        Assert.HasCount(2, parse.Steps);
        Assert.AreEqual(new CommandStep(3, "create class Book"), parse.Steps[0]);
        Assert.AreEqual(new CommandStep(5, "add property Pages int to Book"), parse.Steps[1]);
    }

    [TestMethod]
    public void A_byte_order_mark_is_not_part_of_the_first_command()   // Review Focus 3
    {
        var parse = ScriptFile.Parse("\uFEFFcreate class Book");

        Assert.AreEqual(new CommandStep(1, "create class Book"), parse.Steps.Single());
    }

    [TestMethod]
    [DataRow("undo")]
    [DataRow("redo")]
    [DataRow("exit")]
    [DataRow("batch")]
    [DataRow("demo")]
    [DataRow("chat")]
    [DataRow("show relationships")]
    [DataRow("run other.cgs")]
    [DataRow("save session x.cgs")]
    [DataRow("help")]
    [DataRow("status")]
    [DataRow("history")]
    [DataRow("clear")]
    public void A_command_that_cannot_run_in_a_script_refuses_the_whole_file_naming_the_line(string command)
    {
        var parse = ScriptFile.Parse("create class Book\n\n" + command + "\ncreate class Member");

        Assert.AreEqual($"Line 3: '{command}' can't be used in a script. Nothing was run.", parse.Refusal);
        Assert.IsEmpty(parse.Steps);
    }

    [TestMethod]
    public void A_build_app_takes_its_answers_up_to_and_including_approve()
    {
        var parse = ScriptFile.Parse(
            "build app from csv books.csv\n# the review\nrename Books to Library\napprove console\ncreate class Extra");

        Assert.IsNull(parse.Refusal);
        Assert.HasCount(2, parse.Steps);
        var build = (BuildAppStep)parse.Steps[0];
        Assert.AreEqual(1, build.Line);
        CollectionAssert.AreEqual(
            new[] { "build app from csv books.csv", "rename Books to Library", "approve console" },
            build.Lines.ToArray());
        Assert.AreEqual(new CommandStep(5, "create class Extra"), parse.Steps[1]);
    }

    [TestMethod]
    public void Cancel_also_ends_a_build_app()
    {
        var parse = ScriptFile.Parse("build app: a library\ncancel");

        CollectionAssert.AreEqual(new[] { "build app: a library", "cancel" },
            ((BuildAppStep)parse.Steps.Single()).Lines.ToArray());
    }

    [TestMethod]
    public void A_build_app_with_no_approve_or_cancel_is_refused()
    {
        var parse = ScriptFile.Parse("create class Book\nbuild app: a library\nrename Book to Volume");

        Assert.AreEqual("Line 2: 'build app' needs an 'approve …' or 'cancel' line after it. Nothing was run.",
            parse.Refusal);
    }

    [TestMethod]
    public void An_empty_file_has_no_steps_and_no_refusal()
    {
        var parse = ScriptFile.Parse("# only a comment\n\n");

        Assert.IsNull(parse.Refusal);
        Assert.IsEmpty(parse.Steps);
    }

    [TestMethod]
    [DataRow("approve", true)]
    [DataRow("approve console", true)]
    [DataRow("APPROVE wpf sqlite", true)]
    [DataRow("cancel", true)]
    [DataRow("approved", false)]
    [DataRow("cancel it", false)]
    public void What_ends_a_build(string line, bool ends) => Assert.AreEqual(ends, ScriptFile.EndsBuild(line));
}
```

Append to `CodeGenerator.Tests/App/CommandRouteTests.cs` (inside the class):

```csharp
    [TestMethod]
    [DataRow("run library.cgs", CommandKind.Run)]
    [DataRow("RUN \"my lib\"", CommandKind.Run)]
    [DataRow("run", CommandKind.Run)]
    [DataRow("save session library.cgs", CommandKind.SaveSession)]
    [DataRow("save session library.cgs replace", CommandKind.SaveSession)]
    [DataRow("save session", CommandKind.SaveSession)]
    [DataRow("running total in Order", CommandKind.Pipeline)]
    [DataRow("save Book", CommandKind.Pipeline)]
    public void Scripts_are_routed(string input, CommandKind expected) =>
        Assert.AreEqual(expected, CommandRoute.Classify(input));
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test CodeGenerator.Tests -c Release -m:1 --filter "FullyQualifiedName~ScriptFileTests|FullyQualifiedName~CommandRouteTests"`
Expected: build errors — `ScriptFile`, `CommandKind.Run` do not exist.

- [ ] **Step 3: Add the kinds and the recognisers**

`CodeGenerator/App/CommandKind.cs` — insert before `Pipeline`:

```csharp
    Run,
    SaveSession,
```

`CodeGenerator/App/Scripts/RunCommand.cs`:

```csharp
using System.Text.RegularExpressions;

namespace CodeGenerator.App.Scripts;

/// <summary>
/// <c>run &lt;file&gt;</c>: replays a script, all or nothing.
/// </summary>
internal static partial class RunCommand
{
    // Anchored on the whole word: "running total in Order" is a sentence for the pipeline.
    [GeneratedRegex(@"^\s*run(?:\s+(?<file>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Shape();

    public static bool IsRun(string? input) => Shape().IsMatch(input ?? string.Empty);

    /// <summary>The file named after <c>run</c>, trimmed, or empty.</summary>
    public static string FileArgument(string input) => Shape().Match(input).Groups["file"].Value.Trim();
}
```

`CodeGenerator/App/Scripts/SaveSessionCommand.cs`:

```csharp
using System.Text.RegularExpressions;

namespace CodeGenerator.App.Scripts;

/// <summary>
/// <c>save session &lt;file&gt; [replace]</c>: writes this session's commands as a script.
/// </summary>
internal static partial class SaveSessionCommand
{
    [GeneratedRegex(@"^\s*save\s+session(?:\s+(?<rest>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Shape();

    public static bool IsSaveSession(string? input) => Shape().IsMatch(input ?? string.Empty);
}
```

`CodeGenerator/App/CommandRoute.cs` — add `using CodeGenerator.App.Scripts;` and, immediately before the `BatchCommand.IsBatch` check:

```csharp
        if (RunCommand.IsRun(text))
            return CommandKind.Run;

        if (SaveSessionCommand.IsSaveSession(text))
            return CommandKind.SaveSession;
```

- [ ] **Step 4: Write the parser**

`CodeGenerator/App/Scripts/ScriptFile.cs`:

```csharp
namespace CodeGenerator.App.Scripts;

/// <summary>One thing a script does, and the line it starts on.</summary>
internal abstract record ScriptStep(int Line);

/// <summary>A command, run as a typed one would be.</summary>
internal sealed record CommandStep(int Line, string Text) : ScriptStep(Line);

/// <summary>
/// A <c>build app</c> line and the answers after it, up to and including <c>approve …</c> or
/// <c>cancel</c>. Run in a separate copy of the tool with these lines piped in.
/// </summary>
internal sealed record BuildAppStep(int Line, IReadOnlyList<string> Lines) : ScriptStep(Line);

/// <summary>The steps of a script, or why none of it may run.</summary>
internal sealed record ScriptParse(IReadOnlyList<ScriptStep> Steps, string? Refusal);

/// <summary>
/// Reads a <c>.cgs</c> script: one command per line, blank lines and <c>#</c> lines ignored.
/// </summary>
/// <remarks>
/// The whole file is checked before anything runs, so a refusal on line 9 never leaves lines 1-8
/// done. Kinds are decided by <see cref="CommandRoute"/>, the classifier the console uses, so a
/// script and a typed line can never disagree about what a line is.
/// </remarks>
internal static class ScriptFile
{
    public const string Extension = ".cgs";

    // Only these have an executor. Everything else either needs someone at the keyboard (undo,
    // batch, demo, chat, show relationships), would make a run depend on more than its own lines
    // (run, save session), ends the session (exit), or changes nothing (help, status, history,
    // clear, ai).
    private static readonly CommandKind[] Runnable = [CommandKind.Pipeline, CommandKind.BuildApp];

    public static ScriptParse Parse(string text)
    {
        var lines = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n');
        var steps = new List<ScriptStep>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (IsSkipped(line))
                continue;

            var number = i + 1;
            var kind = CommandRoute.Classify(line);

            if (!Runnable.Contains(kind))
                return Refused($"Line {number}: '{line}' can't be used in a script. Nothing was run.");

            if (kind == CommandKind.Pipeline)
            {
                steps.Add(new CommandStep(number, line));
                continue;
            }

            var block = new List<string> { line };
            var closed = false;

            for (i++; i < lines.Length; i++)
            {
                var answer = lines[i].Trim();
                if (IsSkipped(answer))
                    continue;

                block.Add(answer);
                if (EndsBuild(answer))
                {
                    closed = true;
                    break;
                }
            }

            if (!closed)
                return Refused($"Line {number}: 'build app' needs an 'approve …' or 'cancel' line after it. Nothing was run.");

            steps.Add(new BuildAppStep(number, block));
        }

        return new ScriptParse(steps, null);
    }

    /// <summary>Whether a review-loop answer ends the build: <c>approve</c> with or without a
    /// target, or <c>cancel</c>.</summary>
    public static bool EndsBuild(string line) =>
        line.Equals("cancel", StringComparison.OrdinalIgnoreCase)
        || line.Equals("approve", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("approve ", StringComparison.OrdinalIgnoreCase);

    private static bool IsSkipped(string line) => line.Length == 0 || line.StartsWith('#');

    private static ScriptParse Refused(string message) => new([], message);
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: the Step 2 command. Expected: all pass. Then the build gate.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/App/Scripts CodeGenerator/App/CommandKind.cs CodeGenerator/App/CommandRoute.cs CodeGenerator.Tests/App/Scripts CodeGenerator.Tests/App/CommandRouteTests.cs
git commit -m "feat(scripts): read a .cgs script, and route run and save session"
```

---

### Task 2: The journal says what to save; `SessionRecorder` keeps it

**Files:**
- Modify: `CodeGenerationEngine/Core/Undo/UndoJournal.cs`
- Create: `CodeGenerationEngine/Core/Undo/UndoScope.cs`
- Create: `CodeGenerationEngine/Core/Undo/SessionRecorder.cs`
- Test: `CodeGenerator.Tests/Core/Undo/SessionRecorderTests.cs`

**Interfaces:**
- Consumes: the existing `UndoJournal` (`Begin`, `End`, `Undo`, `Redo`, `Enabled`; private `Open` and `Entry` records).
- Produces:
  - `UndoScope Begin(string label)` (was `IDisposable`); `UndoScope.Keep(IEnumerable<string> lines)`
  - `event Action<IReadOnlyList<string>>? Recorded; event Action? Undone; event Action? Redone;` on `UndoJournal`
  - `sealed class SessionRecorder(UndoJournal journal) { IReadOnlyList<string> Lines; bool UndoWasOff; }`
  - All public, namespace `CodeGenerationEngine.Core.Undo`.

Semantics: an entry's kept lines travel with it. `Recorded` fires once per entry, kept lines possibly empty (a failed command that changed files) so the recorder's stack stays aligned with the journal's; `Undone`/`Redone` fire after a successful undo/redo. With the journal off, a scope that kept lines fires `Recorded` with them on dispose (no entry exists, and no undo will ever fire).

- [ ] **Step 1: Write the failing tests**

`CodeGenerator.Tests/Core/Undo/SessionRecorderTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test CodeGenerator.Tests -c Release -m:1 --filter "FullyQualifiedName~SessionRecorderTests"`
Expected: build errors — `SessionRecorder`, `Keep` do not exist.

- [ ] **Step 3: Add `UndoScope`**

`CodeGenerationEngine/Core/Undo/UndoScope.cs`:

```csharp
namespace CodeGenerationEngine.Core.Undo;

/// <summary>
/// One command's part of the undo history; disposing it ends the command.
/// </summary>
/// <remarks>
/// <see cref="Keep"/> says what <c>save session</c> should write for this command if the journal
/// records it. The caller decides — only it knows whether the command succeeded, and what a
/// replayable form of it is (a typed line, the lines a script ran, a note for an interactive
/// build).
/// </remarks>
public sealed class UndoScope : IDisposable
{
    internal enum Mode { Journaled, Off, Ignored }

    private readonly UndoJournal _journal;
    private readonly Mode _mode;
    private readonly List<string> _kept = [];
    private bool _ended;

    internal UndoScope(UndoJournal journal, Mode mode)
    {
        _journal = journal;
        _mode = mode;
    }

    /// <summary>The lines a saved session should hold for this command.</summary>
    public void Keep(IEnumerable<string> lines) => _kept.AddRange(lines);

    public void Dispose()
    {
        if (_ended)
            return;
        _ended = true;

        switch (_mode)
        {
            case Mode.Journaled:
                _journal.End(_kept);
                break;
            case Mode.Off:
                _journal.RecordWhileOff(_kept);
                break;
        }
    }
}
```

- [ ] **Step 4: Change `UndoJournal`**

In `UndoJournal.cs`:

1. Add, after `public string? NextRedoLabel …`:

```csharp
    /// <summary>An entry was recorded, with the lines its command asked to keep (maybe none). With
    /// undo off, raised for each command that kept lines, with no entry behind it.</summary>
    public event Action<IReadOnlyList<string>>? Recorded;

    /// <summary>The latest entry was undone.</summary>
    public event Action? Undone;

    /// <summary>The latest undone entry was redone.</summary>
    public event Action? Redone;
```

2. Replace `Begin` with:

```csharp
    public UndoScope Begin(string label)
    {
        if (_disposed)
            return new UndoScope(this, UndoScope.Mode.Ignored);
        if (!Enabled)
            return new UndoScope(this, UndoScope.Mode.Off);

        if (_depth++ > 0)
            return new UndoScope(this, UndoScope.Mode.Journaled);

        try
        {
            if (!_started && !Start())
            {
                _depth = 0;
                return new UndoScope(this, UndoScope.Mode.Off);
            }

            Sync();

            _open = new Open(label, _captureState?.Invoke(), ReadRegistry(), Directories(), []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable workspace must not stop the command; it only goes unjournalled.
            _open = null;
        }

        return new UndoScope(this, UndoScope.Mode.Journaled);
    }
```

3. Change `private void End()` to `internal void End(IReadOnlyList<string> kept)`, and make its first lines:

```csharp
    internal void End(IReadOnlyList<string> kept)
    {
        _open?.Kept.AddRange(kept);

        if (--_depth > 0 || _open is null)
            return;
```

4. In `End`, the `_undo.Add(new Entry(...))` call gains the kept lines as its last argument, and the event follows the redo clearing:

```csharp
            _undo.Add(new Entry(open.Label, folder, before, after, changed, createdRoots,
                open.State, stateAfter, open.Registry, registryAfter, registryChanged, [.. open.Kept]));

            foreach (var undone in _redo)
                TryDeleteTree(undone.Folder);
            _redo.Clear();

            Recorded?.Invoke(_undo[^1].Kept);
```

(The capacity trim stays after this; the recorder is deliberately not trimmed.)

5. Add after `End`:

```csharp
    // Undo is off, so there is no entry and never will be an undo; the lines are kept as they are.
    internal void RecordWhileOff(IReadOnlyList<string> kept)
    {
        if (kept.Count > 0)
            Recorded?.Invoke([.. kept]);
    }
```

6. In `Undo()`, raise `Undone?.Invoke();` just before its successful `return new UndoResult(true, …)`; in `Redo()`, `Redone?.Invoke();` just before its successful return.

7. Records: `Open` gains a last member `List<string> Kept`; `Entry` gains a last member `IReadOnlyList<string> Kept`.

8. Delete the private `Scope` and `NoScope` classes; `UndoScope` replaces both.

- [ ] **Step 5: Add `SessionRecorder`**

`CodeGenerationEngine/Core/Undo/SessionRecorder.cs`:

```csharp
namespace CodeGenerationEngine.Core.Undo;

/// <summary>
/// The commands <c>save session</c> writes: those that worked and changed something, minus what
/// has been undone.
/// </summary>
/// <remarks>
/// Mirrors the journal's own undo and redo stacks entry for entry — an entry kept no lines when its
/// command failed, and is still pushed, so an undo always pops the entry it undid. Not capped: the
/// journal forgets entries past 50, a saved session should not.
/// </remarks>
public sealed class SessionRecorder
{
    private readonly UndoJournal _journal;
    private readonly List<IReadOnlyList<string>> _kept = [];
    private readonly Stack<IReadOnlyList<string>> _undone = new();

    public SessionRecorder(UndoJournal journal)
    {
        _journal = journal;

        journal.Recorded += lines =>
        {
            _kept.Add(lines);
            _undone.Clear();
        };

        journal.Undone += () =>
        {
            if (_kept.Count == 0)
                return;
            _undone.Push(_kept[^1]);
            _kept.RemoveAt(_kept.Count - 1);
        };

        journal.Redone += () =>
        {
            if (_undone.Count > 0)
                _kept.Add(_undone.Pop());
        };
    }

    /// <summary>Every kept line, oldest first.</summary>
    public IReadOnlyList<string> Lines => [.. _kept.SelectMany(lines => lines)];

    /// <summary>True when undo is off, so <see cref="Lines"/> holds every command that kept lines,
    /// whether or not it changed anything.</summary>
    public bool UndoWasOff => !_journal.Enabled;
}
```

- [ ] **Step 6: Run the new and the existing undo tests**

Run: `dotnet test CodeGenerator.Tests -c Release -m:1 --filter "FullyQualifiedName~SessionRecorderTests|FullyQualifiedName~Undo|FullyQualifiedName~ChatSession"`
Expected: all pass (the existing `using var entry = … ? journal.Begin(input) : null;` callers compile unchanged). Then the build gate.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerationEngine/Core/Undo CodeGenerator.Tests/Core/Undo/SessionRecorderTests.cs
git commit -m "feat(undo): each entry carries what save session should write; SessionRecorder keeps it"
```

---

### Task 3: `save session`

**Files:**
- Create: `CodeGenerator/App/Scripts/ScriptPath.cs`
- Create: `CodeGenerator/App/Scripts/ScriptNotes.cs`
- Modify: `CodeGenerator/App/Scripts/SaveSessionCommand.cs` (add `Handle`)
- Test: `CodeGenerator.Tests/App/Scripts/SaveSessionCommandTests.cs`, `CodeGenerator.Tests/App/Scripts/ScriptPathTests.cs`

**Interfaces:**
- Consumes: `SessionRecorder` (Task 2), `ScriptFile.Extension` (Task 1).
- Produces:
  - `static class ScriptPath { static string Resolve(string argument, string workspaceRoot); static string Shown(string path, string workspaceRoot); }`
  - `static class ScriptNotes { static string InteractiveBuildApp(string input); const string Demo; }`
  - `sealed record SaveResult(bool Saved, string Message)`; `SaveSessionCommand.Handle(string input, string workspaceRoot, SessionRecorder recorder, DateTime now) : SaveResult`

- [ ] **Step 1: Write the failing tests**

`CodeGenerator.Tests/App/Scripts/ScriptPathTests.cs`:

```csharp
using CodeGenerator.App.Scripts;

namespace CodeGenerator.Tests.App.Scripts;

[TestClass]
public sealed class ScriptPathTests
{
    private const string Root = @"C:\Work\Library";

    [TestMethod]
    [DataRow("library", @"C:\Work\Library\library.cgs")]
    [DataRow("library.cgs", @"C:\Work\Library\library.cgs")]
    [DataRow("library.txt", @"C:\Work\Library\library.txt")]
    [DataRow("\"my lib\"", @"C:\Work\Library\my lib.cgs")]          // Review Focus 4
    [DataRow(@"scripts\setup", @"C:\Work\Library\scripts\setup.cgs")]
    [DataRow(@"D:\Recipes\audit.cgs", @"D:\Recipes\audit.cgs")]
    public void Names_resolve_against_the_workspace(string argument, string expected) =>
        Assert.AreEqual(expected, ScriptPath.Resolve(argument, Root));

    [TestMethod]
    public void A_path_inside_the_workspace_is_shown_relative_and_one_outside_in_full()
    {
        Assert.AreEqual(@"scripts\setup.cgs", ScriptPath.Shown(@"C:\Work\Library\scripts\setup.cgs", Root));
        Assert.AreEqual(@"D:\Recipes\audit.cgs", ScriptPath.Shown(@"D:\Recipes\audit.cgs", Root));
    }
}
```

`CodeGenerator.Tests/App/Scripts/SaveSessionCommandTests.cs`:

```csharp
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

        Assert.AreEqual("Saved 0 commands and 1 note to shop.cgs.", Save("save session shop").Message);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test CodeGenerator.Tests -c Release -m:1 --filter "FullyQualifiedName~SaveSessionCommandTests|FullyQualifiedName~ScriptPathTests"`
Expected: build errors.

- [ ] **Step 3: Write `ScriptPath` and `ScriptNotes`**

`CodeGenerator/App/Scripts/ScriptPath.cs`:

```csharp
namespace CodeGenerator.App.Scripts;

/// <summary>Where a script named on the command line lives, and how to name it back.</summary>
internal static class ScriptPath
{
    /// <summary>Relative to the workspace unless absolute; quotes stripped; <c>.cgs</c> added when
    /// there is no extension.</summary>
    public static string Resolve(string argument, string workspaceRoot)
    {
        var name = argument.Trim();
        if (name.Length >= 2 && name[0] == '"' && name[^1] == '"')
            name = name[1..^1];

        if (Path.GetExtension(name).Length == 0)
            name += ScriptFile.Extension;

        return Path.GetFullPath(Path.Combine(workspaceRoot, name));
    }

    /// <summary>Relative when inside the workspace, in full when not.</summary>
    public static string Shown(string path, string workspaceRoot)
    {
        var relative = Path.GetRelativePath(workspaceRoot, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? path
            : relative;
    }
}
```

`CodeGenerator/App/Scripts/ScriptNotes.cs`:

```csharp
using DataIngestion;

namespace CodeGenerator.App.Scripts;

/// <summary>What a saved session holds for commands it cannot replay as typed.</summary>
internal static class ScriptNotes
{
    /// <summary>A build chosen on the build page: its typed line says too little to replay it.
    /// Masked the way the history file masks it — a saved script is shared more than history.</summary>
    public static string InteractiveBuildApp(string input) =>
        "# " + ConnectionSecret.RedactLine(input.Trim())
        + " (built interactively; add its answers below to replay it)";

    public const string Demo = "# demo (a guided walkthrough; type demo to run it again)";
}
```

- [ ] **Step 4: Write `Handle`**

Add to `SaveSessionCommand` (and `using System.Text; using CodeGenerationEngine.Core.Undo;`), plus the record at file scope:

```csharp
internal sealed record SaveResult(bool Saved, string Message);
```

```csharp
    private const string ReplaceWord = "replace";

    public static SaveResult Handle(string input, string workspaceRoot, SessionRecorder recorder, DateTime now)
    {
        var rest = Shape().Match(input).Groups["rest"].Value.Trim();

        var replace = rest.EndsWith(" " + ReplaceWord, StringComparison.OrdinalIgnoreCase);
        if (replace)
            rest = rest[..^(ReplaceWord.Length + 1)].TrimEnd();

        if (rest.Length == 0)
            return new SaveResult(false, "Name the file to save to, for example: save session library.cgs");

        var lines = recorder.Lines;
        if (lines.Count == 0)
            return new SaveResult(false, "Nothing to save — no command in this session changed anything.");

        var path = ScriptPath.Resolve(rest, workspaceRoot);
        var shown = ScriptPath.Shown(path, workspaceRoot);

        if (File.Exists(path) && !replace)
        {
            return new SaveResult(false,
                $"{shown} already exists. Add 'replace' to overwrite it: save session {rest} replace");
        }

        var text = new List<string> { $"# Saved {now:yyyy-MM-dd HH:mm} from {workspaceRoot}" };
        if (recorder.UndoWasOff)
            text.Add("# Undo was off in this session, so this keeps every command that succeeded.");
        text.AddRange(lines);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SaveResult(false, $"Could not save {shown}: {ex.Message}");
        }

        var notes = lines.Count(l => l.TrimStart().StartsWith('#'));
        var commands = lines.Count - notes;

        return new SaveResult(true, $"Saved {Count(commands, "command")}"
            + (notes > 0 ? $" and {Count(notes, "note")}" : string.Empty) + $" to {shown}.");
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
```

- [ ] **Step 5: Run the tests to see them pass**, then the build gate.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/App/Scripts CodeGenerator.Tests/App/Scripts
git commit -m "feat(scripts): save session writes the kept commands to a .cgs file"
```

---

### Task 4: Run a `build app` block in a separate copy of the tool

**Files:**
- Modify: `CodeGenerationEngine/Helper/ProcessRunner.cs` (`RunStreaming` gains three optional parameters)
- Create: `CodeGenerator/App/Scripts/ScriptBuildApp.cs`
- Test: `CodeGenerator.Tests/Helper/ProcessRunnerStreamingTests.cs`, `CodeGenerator.Tests/App/Scripts/ScriptBuildAppTests.cs`

**Interfaces:**
- Produces:
  - `ProcessRunner.RunStreaming(string fileName, string arguments, Action<string> onLine, string? workingDirectory = null, TimeSpan? timeout = null, string? standardInput = null, IReadOnlyDictionary<string, string>? environment = null, Encoding? outputEncoding = null)`
  - `ScriptBuildApp.Run(IReadOnlyList<string> lines, string workspaceRoot, Action<string> onLine, string? executable = null) : bool`; `ScriptBuildApp.SuccessMarker = "Generated project at "`

Facts established while planning (2026-10-01, by hand): piped `build app from csv <path>` + `approve console` + `exit` generated `Books.Console` and `Books.Console.Tests` in 12 s, printing `> Generated project at C:\…\Books.Console`. **The piped CLI's exit code is 0 whether or not the build worked** (`Program.cs` returns 0 after `Run()`), so success is read from that line. The test bin holds `CodeGenerator.exe`.

- [ ] **Step 1: Write the failing tests**

Append to `ProcessRunnerStreamingTests`:

```csharp
    [TestMethod]
    public void Writes_standard_input_and_closes_it()
    {
        var lines = new List<string>();

        var result = ProcessRunner.RunStreaming("findstr.exe", "\"^\"", lines.Add, standardInput: "alpha\r\nbeta\r\n");

        Assert.AreEqual(0, result.ExitCode);
        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, lines.Select(l => l.Trim()).ToArray());
    }

    [TestMethod]
    public void Sets_the_childs_environment()
    {
        var lines = new List<string>();

        ProcessRunner.RunStreaming("cmd.exe", "/c echo %SCG_SCRIPT_TEST%", lines.Add,
            environment: new Dictionary<string, string> { ["SCG_SCRIPT_TEST"] = "from the parent" });

        Assert.AreEqual("from the parent", lines.Single().Trim());
    }
```

`CodeGenerator.Tests/App/Scripts/ScriptBuildAppTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test CodeGenerator.Tests -c Release -m:1 --filter "FullyQualifiedName~ProcessRunnerStreamingTests|FullyQualifiedName~ScriptBuildAppTests"`
Expected: build errors (`standardInput`, `ScriptBuildApp`).

- [ ] **Step 3: Extend `RunStreaming`**

Signature (add `using System.Text;` if absent):

```csharp
    public static ProcessResult RunStreaming(
        string fileName,
        string arguments,
        Action<string> onLine,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        string? standardInput = null,
        IReadOnlyDictionary<string, string>? environment = null,
        Encoding? outputEncoding = null)
```

In the `ProcessStartInfo` initializer add:

```csharp
                RedirectStandardInput = standardInput is not null,
```

After the initializer (before the handlers):

```csharp
        if (standardInput is not null)
            process.StartInfo.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // Only when asked: the existing callers read tools that write the console code page.
        if (outputEncoding is not null)
        {
            process.StartInfo.StandardOutputEncoding = outputEncoding;
            process.StartInfo.StandardErrorEncoding = outputEncoding;
        }

        if (environment is not null)
            foreach (var (name, value) in environment)
                process.StartInfo.Environment[name] = value;
```

After `process.BeginErrorReadLine();`:

```csharp
        // Written whole and closed, so a child reading to end-of-input sees it end.
        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }
```

Add `<param>` docs for the three new parameters.

- [ ] **Step 4: Write `ScriptBuildApp`**

```csharp
using System.ComponentModel;
using System.Text;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Helper;

namespace CodeGenerator.App.Scripts;

/// <summary>
/// Runs a script's <c>build app</c> block in a separate copy of the tool, its lines piped in.
/// </summary>
/// <remarks>
/// The piped form is the one <c>build app</c> already has and its end-to-end runs use: no wizard,
/// no page, answers read line by line. Running it in-process would mean changing how the
/// command reads input in a dozen places. The copy's exit code is 0 whatever happens, so success is
/// read from the line it prints when it generates a project.
/// </remarks>
internal static class ScriptBuildApp
{
    public const string SuccessMarker = "Generated project at ";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private static string DefaultExecutable =>
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "CodeGenerator.exe" : "CodeGenerator");

    /// <param name="onLine">Called once per output line, never concurrently.</param>
    public static bool Run(
        IReadOnlyList<string> lines, string workspaceRoot, Action<string> onLine, string? executable = null)
    {
        var gate = new object();
        var generated = false;
        var input = string.Join("\n", lines.Append("exit")) + "\n";

        ProcessResult result;
        try
        {
            result = ProcessRunner.RunStreaming(
                executable ?? DefaultExecutable,
                string.Empty,
                line =>
                {
                    // stdout and stderr arrive on two threads; the chat page's capture is a plain
                    // StringWriter.
                    lock (gate)
                    {
                        generated |= line.Contains(SuccessMarker, StringComparison.Ordinal);
                        onLine(line);
                    }
                },
                workingDirectory: workspaceRoot,
                timeout: Timeout,
                standardInput: input,
                environment: new Dictionary<string, string> { ["PROJECTPATH"] = workspaceRoot },
                outputEncoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Win32Exception ex)
        {
            onLine($"Could not start a copy of the tool: {ex.Message}");
            return false;
        }
        finally
        {
            // The copy may have registered a project in the file this process also reads.
            ProjectManager.ReloadWithin(workspaceRoot);
        }

        if (result.TimedOut)
        {
            onLine("The build took longer than 10 minutes and was stopped.");
            return false;
        }

        lock (gate)
            return generated || lines[^1].Equals("cancel", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**, then the build gate.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/Helper/ProcessRunner.cs CodeGenerator/App/Scripts/ScriptBuildApp.cs CodeGenerator.Tests/Helper/ProcessRunnerStreamingTests.cs CodeGenerator.Tests/App/Scripts/ScriptBuildAppTests.cs
git commit -m "feat(scripts): a build app block runs in a separate copy of the tool"
```

---

### Task 5: `ScriptRunner` — all or nothing

**Files:**
- Create: `CodeGenerator/App/Scripts/ScriptRunner.cs`
- Modify: `CodeGenerator/App/Scripts/RunCommand.cs` (add `Load`)
- Modify: `CodeGenerator/App/BatchCommand.cs` (make `ConfirmSnapshotSize` `internal`)
- Test: `CodeGenerator.Tests/App/Scripts/ScriptRunnerTests.cs`

**Interfaces:**
- Consumes: `ScriptStep`/`CommandStep`/`BuildAppStep`, `ScriptFile.Parse`, `ScriptPath` (Tasks 1, 3); `WorkspaceSnapshot`; `SessionState` (`Capture()`, `Restore(ISessionState replacing)`).
- Produces:
  - `sealed record ScriptOutcome(bool Succeeded, IReadOnlyList<string> Ran, string Message)`
  - `ScriptRunner.Run(string name, IReadOnlyList<ScriptStep> steps, string workspaceRoot, Func<ScriptStep, bool> execute, Action<string> say, Func<bool> confirmSnapshot) : ScriptOutcome`
  - `sealed record ScriptLoad(IReadOnlyList<ScriptStep>? Steps, string Name, string? Problem)`; `RunCommand.Load(string input, string workspaceRoot) : ScriptLoad`
  - `ScriptRunner.Text(ScriptStep)` — the line shown for a step.

The caller resets its own current project from `ProjectManager.CurrentProject` after a failed run (the runner restores `ProjectManager`'s).

- [ ] **Step 1: Write the failing tests**

`CodeGenerator.Tests/App/Scripts/ScriptRunnerTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
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
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test CodeGenerator.Tests -c Release -m:1 --filter "FullyQualifiedName~ScriptRunnerTests"`
Expected: build errors.

- [ ] **Step 3: Write the runner**

`CodeGenerator/App/Scripts/ScriptRunner.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Undo;

namespace CodeGenerator.App.Scripts;

/// <summary>How a run ended, and the lines it ran when it worked.</summary>
internal sealed record ScriptOutcome(bool Succeeded, IReadOnlyList<string> Ran, string Message);

/// <summary>
/// Runs a script's steps under one snapshot: all of them, or — at the first failure — none.
/// </summary>
/// <remarks>
/// The snapshot is <c>batch</c>'s: files and the project registry. Validation rules live only in
/// memory and the snapshot does not hold them, so <see cref="SessionState"/> is captured beside it
/// and put back with it. How one step runs is the caller's: the console prints, the chat page
/// makes a turn of it.
/// </remarks>
internal static class ScriptRunner
{
    public static ScriptOutcome Run(
        string name,
        IReadOnlyList<ScriptStep> steps,
        string workspaceRoot,
        Func<ScriptStep, bool> execute,
        Action<string> say,
        Func<bool> confirmSnapshot)
    {
        if (!confirmSnapshot())
            return new ScriptOutcome(false, [], "Nothing was run.");

        WorkspaceSnapshot snapshot;
        try
        {
            snapshot = WorkspaceSnapshot.Capture(workspaceRoot);
        }
        catch (Exception ex)
        {
            return new ScriptOutcome(false, [], $"Could not snapshot the workspace: {ex.Message}. Nothing was run.");
        }

        var state = SessionState.Capture();
        var ran = new List<string>();

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            say($"[{i + 1}/{steps.Count}] {Text(step)}");

            var reason = string.Empty;
            bool ok;
            try
            {
                ok = execute(step);
            }
            catch (Exception ex)
            {
                ok = false;
                reason = $": {ex.GetType().Name}: {ex.Message}";
            }

            if (ok)
            {
                ran.AddRange(step is BuildAppStep build ? build.Lines : [Text(step)]);
                continue;
            }

            try
            {
                snapshot.Restore();
            }
            catch (Exception ex)
            {
                return new ScriptOutcome(false, [],
                    $"Line {step.Line} failed{reason}. Putting it back failed too: {ex.Message}. "
                    + $"The workspace is in an unknown state; the copy taken before the run is at {snapshot.SnapshotPath}.");
            }

            state.Restore(SessionState.Capture());
            snapshot.Dispose();

            return new ScriptOutcome(false, [], $"Line {step.Line} failed{reason}. Everything the script did was put back.");
        }

        snapshot.Dispose();

        return new ScriptOutcome(true, ran,
            $"Ran {steps.Count} command{(steps.Count == 1 ? "" : "s")} from {name}.");
    }

    /// <summary>The line a step is shown as: its command, or a build's first line.</summary>
    public static string Text(ScriptStep step) => step switch
    {
        CommandStep command => command.Text,
        BuildAppStep build => build.Lines[0],
        _ => throw new ArgumentOutOfRangeException(nameof(step))
    };
}
```

- [ ] **Step 4: Add `Load` to `RunCommand`**

At file scope in `RunCommand.cs`:

```csharp
/// <summary>A script ready to run, or why it is not.</summary>
internal sealed record ScriptLoad(IReadOnlyList<ScriptStep>? Steps, string Name, string? Problem);
```

In the class:

```csharp
    public static ScriptLoad Load(string input, string workspaceRoot)
    {
        var argument = FileArgument(input);
        if (argument.Length == 0)
            return new ScriptLoad(null, string.Empty, "Name the script to run, for example: run library.cgs");

        var path = ScriptPath.Resolve(argument, workspaceRoot);
        var name = ScriptPath.Shown(path, workspaceRoot);

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new ScriptLoad(null, name, $"No script at {name}. Nothing was run.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ScriptLoad(null, name, $"Could not read {name}: {ex.Message}. Nothing was run.");
        }

        var parse = ScriptFile.Parse(text);
        if (parse.Refusal is not null)
            return new ScriptLoad(null, name, parse.Refusal);
        if (parse.Steps.Count == 0)
            return new ScriptLoad(null, name, $"{name} has no commands. Nothing was run.");

        return new ScriptLoad(parse.Steps, name, null);
    }
```

In `BatchCommand.cs`: `private static bool ConfirmSnapshotSize` → `internal static bool ConfirmSnapshotSize`.

- [ ] **Step 5: Run the tests to see them pass**, then the build gate.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/App/Scripts CodeGenerator/App/BatchCommand.cs CodeGenerator.Tests/App/Scripts/ScriptRunnerTests.cs
git commit -m "feat(scripts): ScriptRunner runs a script all or nothing"
```

---

### Task 6: The console — routing, keeping, help

**Files:**
- Modify: `CodeGenerator/App/CodeGeneratorStarter.cs` (`Run()` loop)
- Modify: `CodeGenerator/App/UndoCommand.cs` (`Journaled` includes `Run`)
- Modify: `CodeGenerator/App/BatchCommand.cs` (`Handle`/`Execute` return the applied sentences)
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs` (two entries)
- Regenerate: `docs/commands.md`, appendix of `docs/user-manual-source.html`
- Test: `CodeGenerator.Tests/App/Scripts/SessionScriptEndToEndTests.cs`, `CodeGenerator.Tests/App/UndoCommandTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `CodeGeneratorStarter.RunScript(string input, UndoScope? entry, ref ProjectMetaData? currentProject)` (private); `BatchCommand.Handle(...) : IReadOnlyList<string>`.

- [ ] **Step 1: Write the failing tests**

Append to `UndoCommandTests`:

```csharp
    [TestMethod]
    public void A_run_is_one_undo_step_and_save_session_is_none()
    {
        Assert.IsTrue(UndoCommand.Journaled(CommandKind.Run));
        Assert.IsFalse(UndoCommand.Journaled(CommandKind.SaveSession));
    }
```

`CodeGenerator.Tests/App/Scripts/SessionScriptEndToEndTests.cs` — drives the real `CodeGenerator.exe` from the test bin with piped input, the route checked by hand while planning (no existing test constructs `CodeGeneratorStarter`, and its constructor prints and opens the store). Each session is a fresh process, which is exactly what "replay in a fresh session" means. Uses Task 4's `standardInput`/`environment` parameters.

```csharp
using System.Text;
using CodeGenerationEngine.Helper;

namespace CodeGenerator.Tests.App.Scripts;

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
```

Checked by hand while planning (piped CLI, 2026-10-01): `add property Pages int to Ghost` prints `Class 'Ghost' not found.`; `add propery Title to Book` is not recognised; `add validation Title is required in Book` adds a rule in memory with no `Title` property needed.

The test bin's `CodeGenerator.exe` is rebuilt by `dotnet build CodeGenerator.Tests`; build the test project (not `--no-build`) before running these.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test CodeGenerator.Tests -c Release -m:1 --filter "FullyQualifiedName~SessionScriptEndToEndTests|FullyQualifiedName~UndoCommandTests"`
Expected: `Journaled(Run)` false; end-to-end: `run library` goes to the pipeline and is not recognised.

- [ ] **Step 3: `Journaled` and `batch`**

`UndoCommand.Journaled`: add `or CommandKind.Run` to the pattern.

`BatchCommand`: `Handle` returns `IReadOnlyList<string>`; every early `return;` becomes `return [];`; the final call becomes `return Execute(...)`. `Execute` returns `IReadOnlyList<string>`: on success `return [.. approved.Select(a => a.Sentence)];`, on every failure path `return [];`. Update the XML summary of `Handle`: "Returns the sentences it applied, or none when it applied nothing."

- [ ] **Step 4: The loop**

In `CodeGeneratorStarter.Run()`:

1. Beside `var journal = UndoJournal.ForSession(...)`: `var recorder = new SessionRecorder(journal);` — and where the journal is replaced because the root moved, replace the recorder too: `recorder = new SessionRecorder(journal);`.

2. After the root-moved check (so `recorder` is the current journal's) and before the `Undo`/`Redo` block, add:

```csharp
            // Not journaled: the file is what was asked for, and undoing a save would delete it.
            if (kind == CommandKind.SaveSession)
            {
                var saved = SaveSessionCommand.Handle(input, _workspace.Root, recorder, DateTime.Now);
                if (saved.Saved)
                    Output.Success(saved.Message);
                else
                    Output.Warning(saved.Message);
                continue;
            }
```

3. Demo: after `RunDemo(ref currentProject);` add `entry?.Keep([ScriptNotes.Demo]);`.

4. In the `try`: after `_buildApp.Handle(input, _workspace.Root, _ai);` add `entry?.Keep([ScriptNotes.InteractiveBuildApp(input)]);`. Batch becomes:

```csharp
                if (kind == CommandKind.Batch)
                {
                    var applied = _batch.Handle(ref currentProject, _workspace.Rules, _workspace.Root,
                        r => ProjectManager.CurrentProject = r, _pipeline);
                    entry?.Keep(applied);
                    continue;
                }

                if (kind == CommandKind.Run)
                {
                    RunScript(input, entry, ref currentProject);
                    continue;
                }
```

5. The pipeline block: declare `var handled = false;` before `CommandColour.Run(...)`, set `handled = outcome.Handled;` inside the lambda, and after the `CommandColour.Run(...)` call: `if (handled) entry?.Keep([input]);`.

6. Add the method:

```csharp
    /// <summary>
    /// <c>run &lt;file&gt;</c>: every line, or — at the first failure — none of them.
    /// </summary>
    /// <remarks>
    /// Lines stream live like <c>batch</c>'s, not through <c>CommandColour</c>: a build block runs
    /// for seconds, and its output belongs on screen as it comes. Lines run without the AI resolver
    /// or a confirm prompt, as <c>batch</c> and <c>RunOnce</c> do — a script must not stop to ask.
    /// </remarks>
    private void RunScript(string input, UndoScope? entry, ref ProjectMetaData? currentProject)
    {
        var load = RunCommand.Load(input, _workspace.Root);
        if (load.Steps is null)
        {
            Output.Warning(load.Problem!);
            return;
        }

        var project = currentProject;

        var outcome = ScriptRunner.Run(
            load.Name,
            load.Steps,
            _workspace.Root,
            step =>
            {
                if (step is BuildAppStep build)
                    return ScriptBuildApp.Run(build.Lines, _workspace.Root, Console.WriteLine);

                var text = ((CommandStep)step).Text;
                var result = _pipeline.Handle(text, ref project, _workspace.Rules, _workspace.Root,
                    r => ProjectManager.CurrentProject = r);

                if (!result.Recognized)
                    Output.Warning(UnrecognizedCommand.Describe(text, CommandSuggester.Closest(text)));

                return result.Handled;
            },
            Console.WriteLine,
            () => BatchCommand.ConfirmSnapshotSize(_workspace.Root));

        currentProject = outcome.Succeeded ? project : ProjectManager.CurrentProject;

        if (outcome.Succeeded)
        {
            entry?.Keep(outcome.Ran);
            Output.Success(outcome.Message);
        }
        else
        {
            Output.Error(outcome.Message);
        }
    }
```

Add `using CodeGenerator.App.Scripts;` and `using CodeGenerationEngine.Core.Undo;` as needed.

7. `RunOnce` (one command from argv): add, before the pipeline fallthrough, a case that refuses both new kinds with `Output.Warning("`run` and `save session` need a session. Start the tool and type them.")` and `return false;` — a single-shot process has no session to save, and replaying through it is out of scope.

- [ ] **Step 5: Help entries and docs**

In `CommandDictionary.cs`, after the `batch` entry:

```csharp
        new()
        {
            Category = "Utility",
            Command = "run <file>",
            Description = "Runs a script of commands (.cgs) saved with save session or written by hand: every line, or none if one fails. "
                        + "One undo reverses the whole run.",
            Example = "run library.cgs"
        },
        new()
        {
            Category = "Utility",
            Command = "save session <file> [replace]",
            Description = "Saves this session's commands that worked and changed something, as a script for run. "
                        + "Won't overwrite a file unless you add replace.",
            Example = "save session library.cgs"
        },
```

Regenerate: `$env:UPDATE_COMMAND_DOCS=1; $env:UPDATE_MANUAL_APPENDIX=1; dotnet test CodeGenerator.Tests -c Release -m:1 --filter "FullyQualifiedName~CommandReferenceDocTests|FullyQualifiedName~ManualAppendixDocTests"`, then rerun the same filter without the variables: pass. Also run `--filter "FullyQualifiedName~CommandHelp|FullyQualifiedName~CommandDictionary"` (80-column wrap and overview-height limits).

- [ ] **Step 6: Run the Step 2 filter and `FullyQualifiedName~Batch|FullyQualifiedName~Undo|FullyQualifiedName~Script`**: all pass. Build gate.

- [ ] **Step 7: Live console check** — rebuild `CodeGenerator` (memory: `dotnet test` leaves the CLI's bin stale), pipe a session in a temp folder: create a class, add properties, a typo, `save session s`, then in a fresh folder copy `Book.cs` and `s.cgs`, `run s`, `undo`, `redo`. Check the output and the file by eye.

- [ ] **Step 8: Commit**

```bash
git add -A CodeGenerator CodeGenerationEngine CodeGenerator.Tests docs/commands.md docs/user-manual-source.html
git commit -m "feat(cli): run and save session"
```

(Check `git status` first: `-A` must not sweep in anything but this task's files.)

---

### Task 7: The chat page

**Files:**
- Modify: `CodeGenerator/Web/Chat/ChatSession.cs`
- Test: `CodeGenerator.Tests/Web/Chat/ChatSessionTests.cs`

**Interfaces:**
- Consumes: `RunCommand.Load`, `ScriptRunner`, `ScriptBuildApp`, `SaveSessionCommand.Handle`, `SessionRecorder`, `UndoScope.Keep`.

- [ ] **Step 1: Write the failing tests** (append to `ChatSessionTests`)

```csharp
    [TestMethod]
    public async Task Run_shows_one_turn_per_line_then_a_summary()
    {
        var session = NewSession(out var workspace);
        File.WriteAllText(Path.Combine(workspace.Root, "Book.cs"), "namespace S { public class Book { } }");
        File.WriteAllText(Path.Combine(workspace.Root, "two.cgs"),
            "add property Pages int to Book\nadd property Title string to Book\n");

        await session.Send("run two");

        Assert.HasCount(3, session.Turns);
        Assert.AreEqual("add property Pages int to Book", session.Turns[0].Input);
        Assert.AreEqual(ChatVerdict.Succeeded, session.Turns[1].Verdict);
        Assert.AreEqual("run two", session.Turns[2].Input);
        StringAssert.Contains(TextOf(session.Turns[2]), "Ran 2 commands from two.cgs.");
        Assert.AreEqual("run two", session.NextUndoLabel);
    }

    [TestMethod]
    public async Task A_failing_run_in_the_chat_puts_everything_back()
    {
        var session = NewSession(out var workspace);
        var book = Path.Combine(workspace.Root, "Book.cs");
        File.WriteAllText(book, "namespace S { public class Book { } }");
        File.WriteAllText(Path.Combine(workspace.Root, "bad.cgs"),
            "add property Pages int to Book\nadd property Pages int to Ghost\n");

        await session.Send("run bad");

        Assert.AreEqual(ChatVerdict.Failed, session.Turns[^1].Verdict);
        StringAssert.Contains(TextOf(session.Turns[^1]), "Line 2 failed. Everything the script did was put back.");
        Assert.AreEqual("namespace S { public class Book { } }", File.ReadAllText(book));
    }

    [TestMethod]
    public async Task Save_session_in_the_chat_writes_what_the_chat_did()
    {
        var session = NewSession(out var workspace);
        File.WriteAllText(Path.Combine(workspace.Root, "Book.cs"), "namespace S { public class Book { } }");

        await session.Send("add property Pages int to Book");
        await session.Send("save session chat");

        StringAssert.Contains(TextOf(session.Turns[^1]), "Saved 1 command to chat.cgs.");
        CollectionAssert.AreEqual(new[] { "add property Pages int to Book" },
            File.ReadAllLines(Path.Combine(workspace.Root, "chat.cgs")).Skip(1).ToArray());
    }

    [TestMethod]
    public async Task A_missing_script_is_one_failed_turn()
    {
        var session = NewSession(out _);

        await session.Send("run nope");

        Assert.HasCount(1, session.Turns);
        Assert.AreEqual(ChatVerdict.Failed, session.Turns[0].Verdict);
        StringAssert.Contains(TextOf(session.Turns[0]), "No script at nope.cgs. Nothing was run.");
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test CodeGenerator.Tests -c Release -m:1 --filter "FullyQualifiedName~ChatSessionTests"`

- [ ] **Step 3: Implement**

In `ChatSession`:

1. Field `private SessionRecorder _recorder;` set beside `_journal` in the constructor (`_recorder = new SessionRecorder(_journal);`) and wherever `_journal` is replaced (`Load`).

2. In `Send`, replace the single-turn lines:

```csharp
            var turns = await Task.Run(() => Turns(input, kind, entry, _cancellation.Token));

            _turns.AddRange(turns);
```

3. Add:

```csharp
    // A run makes a turn per line and one for the outcome; everything else makes one. Collected
    // and added together once the worker is done, never from it: the page reads _turns while
    // rendering.
    private IReadOnlyList<ChatTurn> Turns(string input, CommandKind kind, UndoScope? entry, CancellationToken ct)
    {
        if (kind == CommandKind.Run)
            return RunScript(input, entry);

        var turn = Run(input, kind, ct);

        if (kind == CommandKind.Pipeline && turn.Verdict == ChatVerdict.Succeeded)
            entry?.Keep([input]);

        return [turn];
    }

    private IReadOnlyList<ChatTurn> RunScript(string input, UndoScope? entry)
    {
        var load = RunCommand.Load(input, _workspace.Root);
        if (load.Steps is null)
            return [Said(input, ChatVerdict.Failed, load.Problem!)];

        var turns = new List<ChatTurn>();
        var tooLarge = string.Empty;

        var outcome = ScriptRunner.Run(
            load.Name,
            load.Steps,
            _workspace.Root,
            step =>
            {
                var turn = StepTurn(step);
                turns.Add(turn);
                return turn.Verdict == ChatVerdict.Succeeded;
            },
            say: _ => { },
            confirmSnapshot: () =>
            {
                // No prompt here: a page cannot ask the console's [p]roceed question mid-run.
                var sizing = WorkspaceSnapshot.Measure(_workspace.Root);
                if (sizing.FileCount <= WorkspaceSnapshot.MaxFiles && sizing.Bytes <= WorkspaceSnapshot.MaxBytes)
                    return true;
                tooLarge = $"The workspace is {sizing.FileCount:N0} files ({sizing.Bytes / (1024.0 * 1024.0):N0} MB), "
                         + "too large to put back if a line fails. Run it from the console, which asks first. ";
                return false;
            });

        _currentProject = ProjectManager.CurrentProject;

        if (outcome.Succeeded)
            entry?.Keep(outcome.Ran);

        turns.Add(Said(input, outcome.Succeeded ? ChatVerdict.Succeeded : ChatVerdict.Failed, tooLarge + outcome.Message));
        return turns;
    }

    // One script line as a turn. No AI and no questions, as on the console: a script must not stop.
    private ChatTurn StepTurn(ScriptStep step)
    {
        var text = ScriptRunner.Text(step);
        var kind = CommandRoute.Classify(text);
        var verdict = ChatVerdict.Failed;
        string captured;

        using var capture = OutputCapture.Start();
        try
        {
            if (step is BuildAppStep build)
            {
                verdict = ScriptBuildApp.Run(build.Lines, _workspace.Root, Console.WriteLine)
                    ? ChatVerdict.Succeeded
                    : ChatVerdict.Failed;
            }
            else
            {
                var project = _currentProject;
                var outcome = _pipeline.Handle(text, ref project, _workspace.Rules, _workspace.Root,
                    r => ProjectManager.CurrentProject = r);
                _currentProject = project;

                if (!outcome.Recognized)
                    Console.WriteLine(UnrecognizedCommand.Describe(text, CommandSuggester.Closest(text)));

                verdict = CommandColour.Verdict(outcome.Recognized, outcome.Handled) switch
                {
                    CommandVerdict.Succeeded => ChatVerdict.Succeeded,
                    CommandVerdict.Failed => ChatVerdict.Failed,
                    _ => ChatVerdict.Unrecognized
                };
            }
        }
        catch (Exception ex)
        {
            foreach (var line in ErrorText.Lines(ex, ErrorText.Verbose))
                Console.WriteLine(line);
            verdict = ChatVerdict.Failed;
        }
        finally
        {
            captured = capture.Text;
        }

        return new ChatTurn(text, kind, verdict, OutputBlocks.Parse(text, captured), DateTimeOffset.Now);
    }

    private static ChatTurn Said(string input, ChatVerdict verdict, string text) =>
        new(input, CommandRoute.Classify(input), verdict, [new TextBlock(text)], DateTimeOffset.Now);
```

4. In `Execute`'s switch, add:

```csharp
            case CommandKind.SaveSession:
                var saved = SaveSessionCommand.Handle(input, _workspace.Root, _recorder, DateTime.Now);
                Console.WriteLine(saved.Message);
                return saved.Saved ? ChatVerdict.Succeeded : ChatVerdict.Failed;
```

`Undo`/`Redo` already work: the recorder follows the journal's events.

- [ ] **Step 4: Run the chat tests and the Step 6 filter of Task 6**: all pass. Build gate.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/Web/Chat/ChatSession.cs CodeGenerator.Tests/Web/Chat/ChatSessionTests.cs
git commit -m "feat(chat): run and save session on the chat page"
```

---

### Task 8: Manual, browser check, full suite

**Files:**
- Modify: `docs/user-manual-source.html` (a section after "Undo and redo")
- Regenerate: `docs/SmartCodeGenerator-User-Manual.docx` (`tools/build-user-manual.ps1`, then `tools/verify-user-manual.ps1`)

- [ ] **Step 1: Manual section** — "Scripts: run and save session", in the style of the undo section: what saving keeps (with the example session from the spec), the file format, `run` all-or-nothing with the `Line 3 failed …` message, the refused commands, `build app` blocks with `approve`, the `replace` word, chat page support. Rebuild and verify the .docx; confirm the new heading is in `word/document.xml` (read it with python `zipfile`, not `unzip | sed` — that hangs on the one-line XML).

- [ ] **Step 2: Browser check** — serve the chat page with the throwaway probe (`C:\ac\ZzChatUndoProbe.cs.txt` copied into `CodeGenerator.Tests`, workspace `C:\ac\chatundo` with `Book.cs`), write `C:\ac\chatundo\two.cgs`, drive `run two` with patchright (wait for `.composer textarea:not([disabled])` first), check three turns and the file, screenshot, then `undo`. Delete the probe.

- [ ] **Step 3: Full suite** — `dotnet build-server shutdown`, then `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build -m:1` after a Release build. Fix anything red.

- [ ] **Step 4: Commit**

```bash
git add docs/user-manual-source.html docs/SmartCodeGenerator-User-Manual.docx
git commit -m "docs: scripts in the manual"
```
