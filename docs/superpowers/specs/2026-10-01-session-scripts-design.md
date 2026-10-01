# Session scripts: `run` and `save session` (Feature 9)

## Why

Everything typed into the tool is lost as a recipe the moment it is done. There is no way to build
the same app again in an empty folder, to keep a set of common steps and apply them to another app,
or to replay a fixed sequence for a demo or a check after a change. `batch` runs several commands,
but they are typed in on the spot and approved one by one, and nothing can be saved.

## Decisions (Rohit, 2026-10-01)

- **Purpose: all three** — rebuilding the same app, reusable hand-written recipes, and demos/testing.
- **Saving keeps only what changed things:** commands that worked and changed the workspace or the
  session. Typos, failed commands, `help`, `status`, `list`/`show` are dropped.
- **On failure, stop and put everything back**, like `batch`.
- **No confirmation:** `run` starts straight away; one `undo` reverses the whole run.
- **Console and chat page** both get the commands.
- **An interactive `build app` is saved as a `#` note**, not replayed with default choices and not
  recorded choice by choice.
- **A `build app` inside a script runs in a separate copy of the tool** with its lines piped in,
  not by changing how `build app` reads its input.

## Behaviour

### Commands

- `save session <file>` writes the session's script. Refuses if the file exists:
  `library.cgs already exists. Add 'replace' to overwrite it: save session library.cgs replace`.
- `save session <file> replace` overwrites.
- `run <file>` runs a script.
- `<file>` is relative to the workspace root unless absolute. With no extension, `.cgs` is added.
  A name with spaces is written in double quotes.

### The file

Plain text, UTF-8, one command per line. Blank lines and lines whose first non-space character is
`#` are ignored.

```
# Library app — saved 2026-10-01 10:42 from C:\Work\Library
create class Book
add property Pages int to Book
add validation Pages must be positive in Book
```

### What `save session` writes

- A header line: `# Saved <date time> from <workspace root>`.
- Every command from this session that **succeeded and changed something**, in the order typed.
  "Changed something" is the undo journal's own test: a command that makes an undo entry.
- A command later undone is left out; redone, it is back in its place.
- A `run` is written as the lines it ran, not as `run x.cgs`, so the saved file stands alone.
- An interactive `build app` is written as a note, with any connection secret masked the way the
  history file masks it:
  `# build app: a library that lends books (built interactively; add its answers below to replay it)`
- When undo is off for the session (over 5,000 files or 200 MB), the journal cannot say what
  changed, so every command that succeeded is kept, and the header says so:
  `# Undo was off in this session, so this keeps every command that succeeded.`
- With nothing to save: `Nothing to save — no command in this session changed anything.` and no
  file is written.
- Success: `Saved 3 commands to library.cgs.`
- The console and the chat page each save their own session's commands.

### What `run` does

1. Reads the file. A missing or unreadable file is reported and nothing runs.
2. Checks every line **before running any**. The whole script is refused, naming the line, if a
   line is one of: `undo`, `redo`, `exit`, `batch`, `demo`, `chat`, `show relationships`, `run`,
   `save session`; or a `build app` with no `approve …` or `cancel` line after it.
   `Line 4: 'undo' can't be used in a script. Nothing was run.`
3. Takes the same whole-workspace snapshot `batch` takes (with the same size confirmation on the
   console when the workspace is over the snapshot limits).
4. Runs the lines in order, printing each one and its result:
   `[2/4] add property Pages int to Book` then the command's own output.
5. On the first failure, restores the snapshot and the current project, and says:
   `Line 3 failed: <reason>. Everything the script did was put back.`
6. On success: `Ran 4 commands from library.cgs.`
7. The whole run is one undo entry, labelled `run library.cgs`.

A `build app` line and its answer lines (up to and including the first line starting with
`approve` or `cancel`) run together in a separate copy of the tool, started with the workspace as
its project path and those lines plus `exit` on its input — exactly the existing piped form. Its
output is shown as it arrives. It counts as failed when the copy reports a failure; the run then
rolls back like any other failure. Whether a piped run's exit code reports a failed or cancelled
build today is not yet checked — the plan establishes it first, and if it does not, the step fails
when the copy's output does not report a generated app. After the copy exits, the project list is
reloaded from its file, since the copy may have registered a project.

### Chat page

`run` and `save session` are typed into the chat box like any command. A `run` shows one turn per
script line, then a summary turn. Undo on the chat page reverses the whole run, as on the console.

## How it is built

### `ScriptFile` (new, `CodeGenerator/App/Scripts/`)

Parses text into steps: `Command(lineNumber, text)` and `BuildApp(lineNumber, lines)`. Skips blanks
and comments. Returns either the steps or the first refusal (line number and message). Pure: no
console, no file system beyond the caller handing it text, so it is tested directly.

Refused kinds are found with `CommandRoute.Classify`, the same classifier the loop uses, plus the
two new commands; nothing is matched by hand-written strings that could drift from routing.

### `ScriptRunner` (new)

Given steps and the session's services, runs them under one `WorkspaceSnapshot`, through the same
`NaturalLanguagePipeline.Handle` call `BatchCommand.Execute` makes, threading the current project the
same way. `Handled == false` or an exception is a failure. A `BuildApp` step goes to
`ScriptBuildApp`, which starts the separate copy through the existing `ProcessRunner`. Output goes
to a writer, so the console streams it and the chat page turns it into turns.

The rollback, the snapshot-size confirmation and the current-project reset are the pieces
`BatchCommand` already has; they are shared, not copied.

### `SessionRecorder` (new)

Holds the list `save session` writes. The undo journal tells it when an entry is recorded (with
the command that made it), undone or redone, and the recorder mirrors that: a stack of saved
entries, where an entry is one or more script lines. A plain command is one line; a run is the
lines it ran; an interactive `build app` is its note. When the journal is off, the loop records
every succeeded command instead.

It is not capped at the journal's 50 entries: undo forgets old entries, but a saved session should
not.

### `SaveSessionCommand` (new)

Resolves the path, applies the refuse-unless-`replace` rule, and writes header plus lines.

### Wiring

- `CommandKind.Run` and `CommandKind.SaveSession`, classified in `CommandRoute` before `Pipeline`.
- The CLI loop and `ChatSession` route both; both are journaled as one entry (`run`) or not at all
  (`save session` writes outside the undo story's scope — the `.cgs` file is the user's output, and
  undoing a save is deleting a file the user asked for).
- `CommandDictionary` entries for both, so help, TAB completion, the chat suggestions,
  `docs/commands.md` and the manual appendix pick them up; the manual gets a section.

## Error handling

- Missing or unreadable file, or an empty one: reported, nothing runs.
- A refused line: reported with its number before anything runs.
- A failing line: rolled back, reported with its number and reason.
- A rollback that fails: reported the way `batch` reports it, with the snapshot's path kept.
- The separate copy cannot start: that line fails and the run rolls back.
- Saving to a folder that does not exist: the folder is created. Saving over an existing file
  without `replace`: refused.

## Testing

- `ScriptFile`: comments and blanks, every refused kind, a `build app` block with and without its
  `approve`, line numbers in messages.
- `SessionRecorder`: recorded, undone, redone, a new command after an undo, a run recorded as its
  lines, the journal-off fallback.
- `ScriptRunner` on a temp workspace: success, a failure on line 3 restoring lines 1–2, one undo
  reversing a whole run, refusal before any line runs.
- End to end: a session's commands saved, the output deleted, the script run in a fresh session,
  the same files produced.
- `build app` in a script: a piped `build app` block runs in the separate copy and generates the
  app; one without `approve` is refused.
- Chat page: `run` checked in a real browser, one turn per line.

## Out of scope

- Recording `build app` page choices so an interactive build replays exactly.
- Variables, parameters or conditions in scripts.
- Running a script from the command line when starting the tool (`CodeGenerator run x.cgs`).
- Saving across sessions (the history file already does that for every line typed).
