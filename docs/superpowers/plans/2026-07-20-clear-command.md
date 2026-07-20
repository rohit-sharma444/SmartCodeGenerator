# Clear Command Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `clear` command (with a `cls` alias) to the interactive CodeGenerator REPL that wipes the visible console screen while leaving all command history intact.

**Architecture:** A tiny testable predicate `ClearCommand.IsClear(input)` (mirroring the existing `BuildAppCommand.IsBuildApp` / `BatchCommand.IsBatch` pattern) recognizes the command. `CodeGeneratorStarter.Run()` dispatches it inline before the pipeline, calling `Console.Clear()` only when a real console is attached. A `CommandDictionary` entry makes it discoverable via `help` and TAB.

**Tech Stack:** C# / .NET, MSTest.

## Global Constraints

- Command recognition is a whole-line exact match after trimming, case-insensitive: only `clear` and `cls`, no arguments.
- History must never be touched: the in-memory `_commandHistory` list and the on-disk `.codegen_history` file are both untouched by construction (the dispatch branch `continue`s before any history write; `Console.Clear()` only affects the display buffer).
- `clear`/`cls` handling is interactive-only. `RunOnce` (one-shot scripting) is left unchanged.
- Follow the existing internal-class predicate pattern already used by `BuildAppCommand`/`BatchCommand` in `CodeGenerator/App/`.

---

### Task 1: `ClearCommand.IsClear` predicate (with test)

**Files:**
- Create: `CodeGenerator/App/ClearCommand.cs`
- Test: `CodeGenerator.Tests/App/ClearCommandTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal static bool CodeGenerator.App.ClearCommand.IsClear(string input)` — returns `true` iff `input`, after `Trim()`, equals `"clear"` or `"cls"` case-insensitively.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/ClearCommandTests.cs`:

```csharp
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ClearCommandTests
{
    [TestMethod]
    [DataRow("clear")]
    [DataRow("cls")]
    [DataRow("CLEAR")]
    [DataRow("Cls")]
    [DataRow("  clear  ")]
    [DataRow("\tcls\t")]
    public void IsClear_RecognizesClearAndCls(string input)
        => Assert.IsTrue(ClearCommand.IsClear(input));

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("clearall")]
    [DataRow("clear foo")]
    [DataRow("cls now")]
    [DataRow("create class")]
    public void IsClear_RejectsEverythingElse(string input)
        => Assert.IsFalse(ClearCommand.IsClear(input));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests --filter ClearCommandTests`
Expected: FAIL — build error, `ClearCommand` does not exist (`CS0103`/type not found).

- [ ] **Step 3: Write minimal implementation**

Create `CodeGenerator/App/ClearCommand.cs`:

```csharp
namespace CodeGenerator.App;

/// <summary>
/// Recognizes the interactive screen-clear command (`clear`, alias `cls`).
/// Clearing affects only the display; command history is never touched.
/// </summary>
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

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests --filter ClearCommandTests`
Expected: PASS — 12 test cases pass.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/ClearCommand.cs CodeGenerator.Tests/App/ClearCommandTests.cs
git commit -m "Add ClearCommand.IsClear predicate for clear/cls"
```

---

### Task 2: Dispatch `clear`/`cls` in the REPL loop

**Files:**
- Modify: `CodeGenerator/App/CodeGeneratorStarter.cs` (in `Run()`, immediately after the `exit` handling block, before the `try` at line ~136)

**Interfaces:**
- Consumes: `ClearCommand.IsClear(string)` from Task 1.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Add the dispatch branch**

In `CodeGeneratorStarter.Run()`, the current code reads:

```csharp
            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                // A half-done background update is safe to abandon: the store's date moved only for
                // days that fully landed, so the next start resumes at the one in progress.
                _storeUpdateCts?.Cancel();
                break;
            }

            // FIX #9: Improved exception handling with specific catch blocks
            try
```

Insert the `clear` branch between the `exit` block and the `// FIX #9` comment:

```csharp
            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                // A half-done background update is safe to abandon: the store's date moved only for
                // days that fully landed, so the next start resumes at the one in progress.
                _storeUpdateCts?.Cancel();
                break;
            }

            if (ClearCommand.IsClear(input))
            {
                // Screen-only wipe. Skipped when output is redirected because
                // Console.Clear() throws IOException with no console attached,
                // and clearing a piped transcript is meaningless anyway.
                // History (in-memory list and .codegen_history) is untouched:
                // we continue before any history write.
                if (!Console.IsOutputRedirected)
                    Console.Clear();
                continue;
            }

            // FIX #9: Improved exception handling with specific catch blocks
            try
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build CodeGenerator`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run the full test suite to confirm no regressions**

Run: `dotnet test CodeGenerator.Tests`
Expected: PASS — all tests, including `ClearCommandTests`, green.

- [ ] **Step 4: Manual smoke check (interactive)**

Rebuild the CLI first (test runs leave `bin` stale — a known trap in this repo):

Run: `dotnet build CodeGenerator -c Debug`

Then run the CLI interactively, type a command (e.g. `list projects`), then `clear`, confirm the screen wipes to a fresh `CodeGenerator >> ` prompt, press ↑ and confirm the previous command is still recalled. Repeat with `cls`.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/CodeGeneratorStarter.cs
git commit -m "Dispatch clear/cls to a screen wipe in the REPL loop"
```

---

### Task 3: Register `clear` in `CommandDictionary`

**Files:**
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs` (the `Utility` region, alongside `help`/`demo`/`exit`, around lines 312-334)

**Interfaces:**
- Consumes: nothing.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Add the command entry**

In the `#region "Utility"` block, after the `exit` entry (which ends at the `},` on line ~333) and before `#endregion`, add:

```csharp
        new()
        {
            Category = "Utility",
            Command = "clear",
            Description = "Clears the screen (history is kept). Alias: cls.",
            Example = "clear"
        },
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build CodeGenerationEngine`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Verify discoverability**

Run: `dotnet build CodeGenerator -c Debug`

Launch the CLI, run `help`, and confirm a `clear` line appears under `UTILITY`. Type `cl` then TAB and confirm `clear` is offered as a suggestion.

- [ ] **Step 4: Commit**

```bash
git add CodeGenerationEngine/Core/CommandDictionary.cs
git commit -m "List clear in the command dictionary for help/TAB"
```

---

## Self-Review

**1. Spec coverage:**
- Recognize `clear` + `cls`, case-insensitive, whitespace-trimmed, no args → Task 1 (`IsClear` + tests).
- Clear the screen only when a real console is attached → Task 2 (`Console.IsOutputRedirected` guard).
- History untouched (in-memory list + `.codegen_history`) → Task 2 (`continue` before any history write; documented in the branch comment).
- `RunOnce` left alone → not modified by any task (explicit in Global Constraints).
- Discoverable via `help` + TAB → Task 3 (`CommandDictionary` entry).
- Unit test for recognition → Task 1.
- All spec sections covered.

**2. Placeholder scan:** No TBD/TODO/vague steps; every code step shows complete code and exact commands.

**3. Type consistency:** `ClearCommand.IsClear(string)` is defined in Task 1 and consumed with the identical signature in Task 2. `CommandInfo` object-initializer shape in Task 3 matches the existing entries in the same file.
