# Undo and redo (Feature 8)

## Why

Every command writes straight to disk. A wrong `remove method`, a `build app` into the wrong name,
or a `batch` that did more than intended can only be put right by hand. `undo` should reverse the
last thing typed and `redo` should put it back, for any command, on both the CLI and the chat page.

## Decisions (Rohit, 2026-09-30)

- **This session only.** Closing the tool clears the history.
- **CLI and chat page**, each with its own history. The chat page gets Undo and Redo buttons.
- **Approach A:** keep a hidden copy of the workspace and compare. Not a full copy before every
  command (B), and not a hand-written reverse for every command (C).

## Behaviour

- One entry per thing typed. `add property …` is one entry; a whole `batch`, `build app` or `demo`
  is one entry.
- `undo` reverses the latest entry. `redo` replays the latest undone one. A new command after an
  undo clears the redo list. The history holds at most **50** entries, and the oldest drop off.
- A command that changed nothing (show, list, help, a refused edit) makes no entry.
- Messages:
  - `Undid: remove method Save from Book — restored Shop\Book.cs`
  - `Undid: build app: a library — removed 48 files`
  - `Redid: …`
  - `Nothing to undo.` / `Nothing to redo.`
- An entry holds:
  - the before and after contents of every workspace file the command added, changed or deleted;
  - the project registry (`ProjectManager.dat`, beside the exe), which `WorkspaceSnapshot` already
    treats as part of the state;
  - the in-memory state before and after: `ParsingStateSnapshot` (current project, class, namespace),
    `ProjectManager.CurrentProject`, and the `ValidationRegistry` rules. Rules live only in memory,
    so without this, undoing an `add validation` would change nothing.
- **Not rolled back, deliberately:** `.codegen` (phrase-learning log, theme choice), `.dd-cache`,
  `bin`, `obj`, `.git`, `TestResults`, `node_modules`, and the learned-intent store beside the exe.
  Undoing a class edit must not erase what the tool has learned.

## How it works

- `UndoJournal` lives in `CodeGenerationEngine/Core`, so both surfaces share it.
- **The hidden copy ("shadow").** At the first command that runs, the workspace is copied once to
  `%TEMP%\codegen_undo_<random>`, using `WorkspaceSnapshot`'s exclusions plus the two above. It is
  deleted when the journal is disposed at exit.
- **Around each command** (`Record(label, run)`):
  1. Scan the workspace (path, size, last-write time). Anything that differs from the shadow was
     changed outside the tool: copy it into the shadow **without** making an entry, so your own
     edits in Visual Studio are never credited to a command.
  2. Capture the in-memory state and the registry bytes.
  3. Run the command.
  4. Scan again. For each file added, changed or deleted, keep its before version (from the
     shadow) and its after version, then update the shadow. Capture the state again. If nothing
     changed, discard the entry.
- **Undo:** first check that every file the entry touched still matches the entry's after version.
  If you edited one outside the tool since, refuse and name it:
  `Can't undo: Shop\Book.cs has changed since remove method Save from Book.` Otherwise write the
  before versions back, delete what the command added, restore the registry and the state, and
  update the shadow. **Redo** is the mirror image, with the same check against the before versions.
- **Size guard:** if the first copy would exceed batch mode's limits (5,000 files or 200 MB), undo is
  off for the session. One line says so; it never prompts. The limits move from `BatchCommand` (in
  the CLI project, which the engine cannot see) to `WorkspaceSnapshot`, so both read one pair of numbers.
- **Memory:** entries keep file contents on disk inside the shadow folder, not in memory, so a
  `build app` entry does not hold an app's worth of text in RAM.

## Where it plugs in

- `CommandKind.Undo` and `CommandKind.Redo`, recognised by `CommandRoute.Classify` for the exact
  words `undo` and `redo`, handled before the language pipeline the way `status` is. Both get
  `CommandDictionary` entries, and `docs/commands.md` and the manual appendix are regenerated.
- **CLI** (`CodeGeneratorStarter.Run`): every command except undo, redo, help, status and exit
  runs inside `Record`, including `build app`, `batch`, `demo` and the relationship flow. After an
  undo or redo, the loop's `currentProject` is taken back from `ProjectManager.CurrentProject`.
  Single-shot mode (`RunOnce`, one command per process) is not journalled.
- **Chat page** (`ChatSession`): the same wrapping, with its own journal.
  - `ChatComposer` gets **Undo** and **Redo** buttons beside Send. They are disabled when
    `CanUndo`/`CanRedo` is false, and their tooltip names the entry
    (`Undo: add property Pages to Book`).
  - Clicking one sends `undo` or `redo` through the normal send path, so the transcript shows the
    result like any other command.

## Testing

- **Journal, on a temporary workspace:**
  - A command that adds, changes and deletes files is undone and redone exactly.
  - A read-only command makes no entry.
  - A new command clears redo.
  - The cap is 50.
  - Validation rules and the current project and class come back.
  - An outside edit between commands is not credited to the next command.
  - Undo refuses, naming the file, when you edited one it would restore; redo likewise.
  - The size guard switches undo off with its one line.
  - The shadow folder is deleted on dispose.
- **CLI, piped, for real:**
  - `create class` then `add property`, then `undo`; check the file on disk.
  - A `batch` undone in one step.
  - `build app … from csv` undone, which removes the app folder. This path is deterministic and
    needs no Ollama.
- **Chat page:** a bUnit test that the buttons are disabled with an empty history, send
  `undo`/`redo` when clicked, and carry the tooltip. Then the running page in a browser, because
  several past chat-page defects were invisible to bUnit.

## Not in scope

History that survives a restart. Undoing a single command out of the middle of the history.
Undoing single-shot runs. Undo inside the `build app` review loop, which already has its own
edit commands.
