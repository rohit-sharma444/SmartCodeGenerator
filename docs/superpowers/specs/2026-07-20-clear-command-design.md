# `clear` command — design

**Date:** 2026-07-20
**Status:** Approved (design)

## Goal

Add a `clear` command (with a `cls` alias) to the interactive CodeGenerator
REPL that wipes the visible console output and returns to a fresh prompt. It
must **not** delete or alter the command history in any form:

- the in-memory `_commandHistory` list (↑/↓ navigation) stays intact, and
- the persistent `.codegen_history` file on disk is never touched.

In other words, this is a standard shell-style screen clear (`clear`/`cls`),
not a history purge.

## Scope

**In scope**

- Recognize `clear` and `cls` (case-insensitive, surrounding whitespace
  ignored) in the interactive `Run()` loop.
- Clear the console screen when there is a real console attached.
- Register the command in `CommandDictionary` so it appears in `help` output
  and TAB suggestions.
- A unit test covering command recognition.

**Out of scope**

- `RunOnce` (one-shot scripting mode). Clearing the screen for a single
  command that immediately exits is meaningless, so that path is left alone.
- Any change to history storage, loading, or navigation.

## Behavior

In the interactive REPL:

```
CodeGenerator >> create project Foo
(...output...)
CodeGenerator >> clear

CodeGenerator >>            <- screen wiped, fresh prompt
# ↑ arrow still recalls "create project Foo"
# .codegen_history unchanged on disk
```

`cls` behaves identically.

## Design

### Command recognition — `ClearCommand`

Introduce a small internal class in `CodeGenerator/App/ClearCommand.cs`,
mirroring the existing `BuildAppCommand.IsBuildApp` / `BatchCommand.IsBatch`
predicate pattern:

```csharp
namespace CodeGenerator.App;

internal static class ClearCommand
{
    public static bool IsClear(string input)
    {
        var trimmed = input.Trim();
        return trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("cls", StringComparison.OrdinalIgnoreCase);
    }
}
```

Recognition is an exact match (after trimming) on the whole line — `clear` and
`cls` take no arguments, so `create ...` and similar are unaffected.

### Dispatch — `CodeGeneratorStarter.Run()`

Handle it inline, right after the `exit` check and *before* the `try` block
that dispatches to `build app:` / `batch` / the NLE pipeline, so a `clear`
never reaches the analyzer:

```csharp
if (ClearCommand.IsClear(input))
{
    if (!Console.IsOutputRedirected)
        Console.Clear();
    continue;
}
```

Two edge cases the guard handles:

- **Redirected / piped output** (scripted runs where `ReadCommand` falls back
  to line reading): `Console.Clear()` throws `IOException` when no console is
  attached, so it is skipped. `clear` becomes a harmless no-op in a piped
  transcript instead of crashing the session.
- **Editor cursor state**: after `Console.Clear()` the cursor is at `(0,0)`.
  The loop reprints `CodeGenerator >> `, and the next `CommandLineEditor`
  re-reads `Console.CursorTop`, so there is no stale-position artifact.

History is untouched by construction: the branch `continue`s before any
history write, and `Console.Clear()` only affects the display buffer.

### Discoverability — `CommandDictionary`

Add one entry under the `Utility` category (next to `help`, `demo`, `exit`):

```csharp
new()
{
    Category = "Utility",
    Command = "clear",
    Description = "Clears the screen (history is kept). Alias: cls.",
    Example = "clear"
},
```

Only the canonical `clear` is listed to keep suggestions uncluttered; `cls` is
documented as an alias in the description.

## Testing

Add `CodeGenerator.Tests/App/ClearCommandTests.cs` covering `IsClear`:

- `"clear"`, `"cls"` → `true`
- case-insensitive: `"CLEAR"`, `"Cls"` → `true`
- surrounding whitespace: `"  clear  "` → `true`
- non-matches: `""`, `"clearall"`, `"clear foo"`, `"create class"` → `false`

`Console.Clear()` itself is not unit-testable (no console under the test
runner); the extracted `IsClear` predicate is the testable seam, matching how
`IsBuildApp` / batch detection are tested elsewhere.

## Files touched

- `CodeGenerator/App/ClearCommand.cs` — new
- `CodeGenerator/App/CodeGeneratorStarter.cs` — dispatch branch in `Run()`
- `CodeGenerationEngine/Core/CommandDictionary.cs` — one command entry
- `CodeGenerator.Tests/App/ClearCommandTests.cs` — new
