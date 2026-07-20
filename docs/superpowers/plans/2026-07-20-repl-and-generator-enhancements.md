# REPL & Generator Enhancements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship seven independent REPL/generator/recognizer enhancements as one TDD commit each on branch `feat/repl-and-generator-enhancements`.

**Architecture:** New testable seams (`CommandSuggester`, `UnrecognizedCommand`, `HistoryCommand`, `HistoryView`) live in `CodeGenerator/App` beside `ClearCommand`, exercised by `CodeGenerator.Tests`. Generator and recognizer changes stay inside their existing single files.

**Tech Stack:** C# / .NET 10. Test frameworks differ by project: **`CodeGenerator.Tests` = MSTest** (`[TestClass]`/`[TestMethod]`/`[DataRow]`, `Assert`, `StringAssert`); **`DomainDiscovery.Tests` = xUnit** (`[Fact]`, `Assert.Equal/True/Contains`). Match the host project's framework.

## Global Constraints

- All commands run from the repository root **`D:\SmartCodeGenerator-Repository`** (the git root and the parent of the tool's working dir; the `.slnx` lives one level down).
- `CodeGenerator/App` types are `internal`; `CodeGenerator.Tests` sees them via the existing `<InternalsVisibleTo Include="CodeGenerator.Tests" />`.
- `MethodBodyGenerator` is tested by `CodeGenerator.Tests` (the only test project referencing `CodeGenerationEngine`).
- No generated method body may emit a statement that references an undeclared symbol (e.g. `_items`) — generated projects must still compile.
- Full suite must be green before every commit: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --nologo` and, for Task 7, `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --nologo`.
- Plan task order is dependency-ordered and does **not** match spec item numbers; the mapping is noted per task.

---

### Task 1: `CommandSuggester` — Levenshtein "did you mean" (spec #2)

**Files:**
- Create: `CodeGenerator/App/CommandSuggester.cs`
- Test: `CodeGenerator.Tests/App/CommandSuggesterTests.cs`

**Interfaces:**
- Consumes: `CodeGenerationEngine.Core.CommandDictionary.Commands` (each `CommandInfo` has a `.Command` string).
- Produces: `internal static int CommandSuggester.Distance(string a, string b)`; `internal static IReadOnlyList<string> CommandSuggester.Closest(string input, int max = 3, int maxDistance = 3)`.

- [ ] **Step 1: Write the failing test**

`CodeGenerator.Tests/App/CommandSuggesterTests.cs`:
```csharp
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class CommandSuggesterTests
{
    [TestMethod]
    [DataRow("kitten", "sitting", 3)]
    [DataRow("create", "create", 0)]
    [DataRow("craete", "create", 2)]
    [DataRow("", "abc", 3)]
    public void Distance_MatchesKnownPairs(string a, string b, int expected)
        => Assert.AreEqual(expected, CommandSuggester.Distance(a, b));

    [TestMethod]
    public void Closest_SuggestsCreateProject_ForTransposedTypo()
        => CollectionAssert.Contains(
            CommandSuggester.Closest("craete project").ToList(), "create project");

    [TestMethod]
    public void Closest_SuggestsListProjects_ForMissingLetter()
        => CollectionAssert.Contains(
            CommandSuggester.Closest("lst projects").ToList(), "list projects");

    [TestMethod]
    public void Closest_ReturnsEmpty_ForGibberish()
        => Assert.AreEqual(0, CommandSuggester.Closest("xyzzy plughfoo").Count);

    [TestMethod]
    public void Closest_ReturnsEmpty_ForBlankInput()
        => Assert.AreEqual(0, CommandSuggester.Closest("   ").Count);

    [TestMethod]
    public void Closest_ExcludesTemplatedForms_WithAngleBrackets()
        => Assert.IsFalse(
            CommandSuggester.Closest("create class").Any(s => s.Contains('<')));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --nologo`
Expected: FAIL — `CommandSuggester` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

`CodeGenerator/App/CommandSuggester.cs`:
```csharp
using CodeGenerationEngine.Core;

namespace CodeGenerator.App;

/// <summary>
/// Ranks the known command names by edit distance to what the user typed, so an
/// unrecognized command can offer a "did you mean" hint. Case-insensitive.
/// </summary>
internal static class CommandSuggester
{
    /// <summary>Levenshtein edit distance (case-insensitive), two-row DP.</summary>
    internal static int Distance(string a, string b)
    {
        a ??= string.Empty;
        b ??= string.Empty;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }

    /// <summary>
    /// The closest command names to <paramref name="input"/> within
    /// <paramref name="maxDistance"/> edits, best first, at most <paramref name="max"/>.
    /// Templated forms (those with a "&lt;placeholder&gt;") are skipped — they are not
    /// something a user types verbatim.
    /// </summary>
    internal static IReadOnlyList<string> Closest(string input, int max = 3, int maxDistance = 3)
    {
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return [];

        return CommandDictionary.Commands
            .Select(c => c.Command)
            .Where(c => !c.Contains('<'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(command => (Command: command, Distance: Distance(trimmed, command)))
            .Where(x => x.Distance <= maxDistance)
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Command, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => x.Command)
            .ToList();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --nologo`
Expected: PASS (all tests, including the new `CommandSuggesterTests`).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/CommandSuggester.cs CodeGenerator.Tests/App/CommandSuggesterTests.cs
git commit -m "Add CommandSuggester for did-you-mean command hints"
```

---

### Task 2: Unrecognized-command feedback (spec #1)

**Files:**
- Create: `CodeGenerator/App/UnrecognizedCommand.cs`
- Modify: `CodeGenerator/App/CodeGeneratorStarter.cs` (`Run()` pipeline call ~line 171-172; `RunOnce()` ~line 95-97)
- Test: `CodeGenerator.Tests/App/UnrecognizedCommandTests.cs`

**Interfaces:**
- Consumes: `CommandSuggester.Closest` (Task 1); `PipelineOutcome.Handled` (from `_pipeline.Handle`, already used by `RunOnce`).
- Produces: `internal static string UnrecognizedCommand.Describe(string input, IReadOnlyList<string> suggestions)`.

- [ ] **Step 1: Write the failing test**

`CodeGenerator.Tests/App/UnrecognizedCommandTests.cs`:
```csharp
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class UnrecognizedCommandTests
{
    [TestMethod]
    public void Describe_NoSuggestions_IsSingleLine()
    {
        var text = UnrecognizedCommand.Describe("zzz", []);
        StringAssert.Contains(text, "Command not recognized: 'zzz'");
        StringAssert.Contains(text, "Type 'help'");
        Assert.IsFalse(text.Contains("Did you mean"));
    }

    [TestMethod]
    public void Describe_WithSuggestions_AppendsDidYouMean()
    {
        var text = UnrecognizedCommand.Describe("craete class", ["create class", "create project"]);
        StringAssert.Contains(text, "Command not recognized: 'craete class'");
        StringAssert.Contains(text, "Did you mean: create class, create project?");
    }

    [TestMethod]
    public void Describe_TrimsTheEchoedInput()
        => StringAssert.Contains(
            UnrecognizedCommand.Describe("   foo   ", []), "'foo'");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --nologo`
Expected: FAIL — `UnrecognizedCommand` does not exist.

- [ ] **Step 3: Write minimal implementation**

`CodeGenerator/App/UnrecognizedCommand.cs`:
```csharp
namespace CodeGenerator.App;

/// <summary>
/// Builds the message shown when the pipeline does not recognize a command, with
/// an optional "did you mean" line from <see cref="CommandSuggester"/>.
/// </summary>
internal static class UnrecognizedCommand
{
    internal static string Describe(string input, IReadOnlyList<string> suggestions)
    {
        var head = $"Command not recognized: '{(input ?? string.Empty).Trim()}'. " +
                   "Type 'help' for the command list.";

        if (suggestions is null || suggestions.Count == 0)
            return head;

        return head + Environment.NewLine +
               $"Did you mean: {string.Join(", ", suggestions)}?";
    }
}
```

- [ ] **Step 4: Wire it into `Run()` and `RunOnce()`**

In `CodeGeneratorStarter.cs`, `Run()` — replace the discard-return pipeline call:
```csharp
                // NLE analyzes first, then CodeGenerator generates from the result.
                // The NLE analysis is used internally only; we surface just the
                // command's own output (e.g. the project list), not the raw
                // SemanticResult, so no onAnalyzed callback is passed.
                var outcome = _pipeline.Handle(input, ref currentProject,
                     _configuredRules, _workspaceRoot, r => ProjectManager.CurrentProject = r);

                if (!outcome.Handled && !string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine(
                        UnrecognizedCommand.Describe(input, CommandSuggester.Closest(input)));
                }
```

In `RunOnce()` — print before returning when unhandled:
```csharp
            var outcome = _pipeline.Handle(command, ref currentProject,
                _configuredRules, _workspaceRoot, r => ProjectManager.CurrentProject = r);

            if (!outcome.Handled && !string.IsNullOrWhiteSpace(command))
            {
                Console.WriteLine(
                    UnrecognizedCommand.Describe(command, CommandSuggester.Closest(command)));
            }

            return outcome.Handled;
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --nologo`
Expected: PASS (all tests). If any pre-existing test drove an unknown command through `RunOnce`/`Run` and asserted silence, update it to expect the new message.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/App/UnrecognizedCommand.cs CodeGenerator/App/CodeGeneratorStarter.cs CodeGenerator.Tests/App/UnrecognizedCommandTests.cs
git commit -m "Report unrecognized commands with a did-you-mean hint"
```

---

### Task 3: Delete the dead suggestion stack (spec #3)

**Files:**
- Delete: `CodeGenerationEngine/Helper/InteractiveConsole.cs`, `CodeGenerationEngine/Helper/CommandSuggestionService.cs`, `CodeGenerationEngine/Helper/CommandCatalog.cs`
- Possibly delete: any test referencing only those types.

**Interfaces:** none produced/consumed — pure removal.

- [ ] **Step 1: Prove they are unreferenced**

Run:
```bash
grep -rn -E "InteractiveConsole|CommandSuggestionService|CommandCatalog" --include="*.cs" . \
  | grep -viE "/bin/|/obj/" \
  | grep -vE "Helper/(InteractiveConsole|CommandSuggestionService|CommandCatalog)\.cs:"
```
Expected: no output (no live references). If a **test file** appears, note it for Step 2. If any **non-test, non-self** reference appears, STOP — the type is not dead; do not delete it.

- [ ] **Step 2: Delete the files (and any test that only covers them)**

```bash
git rm CodeGenerationEngine/Helper/InteractiveConsole.cs \
       CodeGenerationEngine/Helper/CommandSuggestionService.cs \
       CodeGenerationEngine/Helper/CommandCatalog.cs
```
If Step 1 surfaced a test that exercises only these types, `git rm` it too.

- [ ] **Step 3: Build and run the full suite**

Run: `dotnet build CodeGenerationEngine/CodeGenerationEngine.csproj --nologo` then `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --nologo`
Expected: build succeeds and all tests pass — proving the removed code was dead.

- [ ] **Step 4: Commit**

```bash
git commit -m "Remove dead InteractiveConsole suggestion stack"
```

---

### Task 4: `history` command (spec #4)

**Files:**
- Create: `CodeGenerator/App/HistoryCommand.cs`, `CodeGenerator/App/HistoryView.cs`
- Modify: `CodeGenerator/App/CodeGeneratorStarter.cs` (`Run()` — dispatch after the `clear` branch, ~line 149); `CodeGenerator/App/CommandLineEditor.cs` (`ShouldRecordInHistory` — exclude history); `CodeGenerationEngine/Core/CommandDictionary.cs` (Utility entry)
- Test: `CodeGenerator.Tests/App/HistoryCommandTests.cs`, `CodeGenerator.Tests/App/HistoryViewTests.cs`

**Interfaces:**
- Consumes: `_commandHistory` (`List<string>` on `CodeGeneratorStarter`); `CommandLineEditor.ShouldRecordInHistory`.
- Produces: `internal static bool HistoryCommand.IsHistory(string input)`; `internal static string HistoryView.Format(IReadOnlyList<string> entries, int max)`.

- [ ] **Step 1: Write the failing tests**

`CodeGenerator.Tests/App/HistoryCommandTests.cs`:
```csharp
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class HistoryCommandTests
{
    [TestMethod]
    [DataRow("history")]
    [DataRow("HISTORY")]
    [DataRow("  history  ")]
    public void IsHistory_RecognizesHistory(string input)
        => Assert.IsTrue(HistoryCommand.IsHistory(input));

    [TestMethod]
    [DataRow("")]
    [DataRow("histories")]
    [DataRow("history all")]
    [DataRow("show history")]
    public void IsHistory_RejectsEverythingElse(string input)
        => Assert.IsFalse(HistoryCommand.IsHistory(input));
}
```

`CodeGenerator.Tests/App/HistoryViewTests.cs`:
```csharp
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class HistoryViewTests
{
    [TestMethod]
    public void Format_Empty_SaysSo()
        => StringAssert.Contains(HistoryView.Format([], 20), "No commands in history");

    [TestMethod]
    public void Format_NumbersEntriesOldestFirst()
    {
        var text = HistoryView.Format(["create project Shop", "use project Shop"], 20);
        StringAssert.Contains(text, "1");
        StringAssert.Contains(text, "create project Shop");
        StringAssert.Contains(text, "2");
        StringAssert.Contains(text, "use project Shop");
        Assert.IsTrue(
            text.IndexOf("create project Shop") < text.IndexOf("use project Shop"),
            "oldest entry should be listed first");
    }

    [TestMethod]
    public void Format_ShowsOnlyTheLastMaxEntries()
    {
        var entries = Enumerable.Range(1, 30).Select(i => $"cmd{i}").ToList();
        var text = HistoryView.Format(entries, 20);
        Assert.IsFalse(text.Contains("cmd10"), "older-than-window entries are omitted");
        StringAssert.Contains(text, "cmd11");
        StringAssert.Contains(text, "cmd30");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --nologo`
Expected: FAIL — `HistoryCommand`/`HistoryView` do not exist.

- [ ] **Step 3: Write the implementations**

`CodeGenerator/App/HistoryCommand.cs`:
```csharp
namespace CodeGenerator.App;

/// <summary>
/// Recognizes the interactive <c>history</c> command, which lists recent commands.
/// Like <see cref="ClearCommand"/> it is a transient display command and is not
/// itself recorded in history.
/// </summary>
internal static class HistoryCommand
{
    public static bool IsHistory(string input)
        => input.Trim().Equals("history", StringComparison.OrdinalIgnoreCase);
}
```

`CodeGenerator/App/HistoryView.cs`:
```csharp
namespace CodeGenerator.App;

/// <summary>Formats the command history as a numbered, oldest-first list.</summary>
internal static class HistoryView
{
    internal static string Format(IReadOnlyList<string> entries, int max)
    {
        if (entries is null || entries.Count == 0)
            return "No commands in history yet.";

        var start = Math.Max(0, entries.Count - max);
        var lines = new List<string>();

        for (var i = start; i < entries.Count; i++)
            lines.Add($"{i - start + 1,4}  {entries[i]}");

        return string.Join(Environment.NewLine, lines);
    }
}
```

- [ ] **Step 4: Exclude `history` from persistence**

In `CommandLineEditor.cs`, `ShouldRecordInHistory`:
```csharp
    internal static bool ShouldRecordInHistory(
        IReadOnlyList<string> history, string command)
        => !string.IsNullOrWhiteSpace(command)
           && !ClearCommand.IsClear(command)
           && !HistoryCommand.IsHistory(command)
           && (history.Count == 0 || !history[^1].Equals(command));
```

- [ ] **Step 5: Dispatch `history` in `Run()`**

In `CodeGeneratorStarter.cs`, immediately after the `ClearCommand.IsClear` branch's closing `}` (before the `try`):
```csharp
            if (HistoryCommand.IsHistory(input))
            {
                Console.WriteLine(HistoryView.Format(_commandHistory, 20));
                continue;
            }
```

- [ ] **Step 6: Register in `CommandDictionary`**

In `CommandDictionary.cs`, in the `Utility` region (after the `clear` entry):
```csharp
        new()
        {
            Category = "Utility",
            Command = "history",
            Description = "Lists the last 20 commands (not itself recorded).",
            Example = "history"
        },
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --nologo`
Expected: PASS (all tests).

- [ ] **Step 8: Commit**

```bash
git add CodeGenerator/App/HistoryCommand.cs CodeGenerator/App/HistoryView.cs \
        CodeGenerator/App/CodeGeneratorStarter.cs CodeGenerator/App/CommandLineEditor.cs \
        CodeGenerationEngine/Core/CommandDictionary.cs \
        CodeGenerator.Tests/App/HistoryCommandTests.cs CodeGenerator.Tests/App/HistoryViewTests.cs
git commit -m "Add a history command to list recent commands"
```

---

### Task 5: `cls` TAB-completable (spec #5)

**Files:**
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs` (Utility region)
- Test: `CodeGenerator.Tests/Core/CommandDictionaryClearTests.cs`

**Interfaces:**
- Consumes: `CommandDictionary.Commands`.
- Produces: nothing new — a data-only addition.

- [ ] **Step 1: Write the failing test**

`CodeGenerator.Tests/Core/CommandDictionaryClearTests.cs`:
```csharp
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

[TestClass]
public class CommandDictionaryClearTests
{
    [TestMethod]
    public void Commands_IncludeBothClearAndClsAliases()
    {
        var names = CommandDictionary.Commands.Select(c => c.Command).ToList();
        CollectionAssert.Contains(names, "clear");
        CollectionAssert.Contains(names, "cls");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --nologo`
Expected: FAIL — `cls` is not in the dictionary.

- [ ] **Step 3: Add the `cls` entry**

In `CommandDictionary.cs`, Utility region, after the `clear` entry:
```csharp
        new()
        {
            Category = "Utility",
            Command = "cls",
            Description = "Alias for clear (clears the screen; history is kept).",
            Example = "cls"
        },
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/CommandDictionary.cs CodeGenerator.Tests/Core/CommandDictionaryClearTests.cs
git commit -m "List cls alias so it is TAB-completable"
```

---

### Task 6: Richer, compile-safe generated method bodies (spec #6)

**Files:**
- Modify: `CodeGenerationEngine/Core/MethodBodyGenerator.cs`
- Test: `CodeGenerator.Tests/Core/MethodBodyGeneratorTests.cs`

**Interfaces:**
- Consumes: `MethodDefinition` (has `.Name`, `.ReturnType`), `PropertyDefinition`.
- Produces: unchanged public signature `MethodBodyGenerator.Generate(string, MethodDefinition, List<PropertyDefinition>)`.

**Fix included:** today `GenerateUpdate`/`GenerateDelete` emit only comments and `GenerateExists`/`GenerateCount` hard-code `false`/`0`. A non-`void` `update`/`delete` method therefore emits no `return` (won't compile), and `exists`/`count` ignore the declared return type. All four now append `GenerateDefault(method.ReturnType)` so every path returns a valid value.

- [ ] **Step 1: Write the failing tests**

`CodeGenerator.Tests/Core/MethodBodyGeneratorTests.cs`:
```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public class MethodBodyGeneratorTests
{
    private static List<string> Body(string method, string returnType)
        => MethodBodyGenerator.Generate(
               "Employee",
               new MethodDefinition { Name = method, ReturnType = returnType },
               []).ToList();

    [TestMethod]
    public void Get_HasGuidanceAndAValidDefaultReturn()
    {
        var body = Body("GetActive", "Employee");
        Assert.IsTrue(body.Any(l => l.Contains("//")), "expected a guidance comment");
        Assert.IsTrue(body.Any(l => l.Contains("return default!;")));
    }

    [TestMethod]
    public void Update_NonVoid_EmitsAReturn_SoItCompiles()
    {
        var body = Body("UpdateEmployee", "bool");
        Assert.IsTrue(body.Any(l => l.Contains("return false;")),
            "a bool update must return a value");
    }

    [TestMethod]
    public void Update_Void_HasNoReturn()
    {
        var body = Body("UpdateEmployee", "void");
        Assert.IsFalse(body.Any(l => l.TrimStart().StartsWith("return")));
    }

    [TestMethod]
    public void Delete_NonVoid_EmitsAReturn()
        => Assert.IsTrue(Body("DeleteEmployee", "int").Any(l => l.Contains("return 0;")));

    [TestMethod]
    public void Count_ReturnsZeroForInt()
        => Assert.IsTrue(Body("CountEmployees", "int").Any(l => l.Contains("return 0;")));

    [TestMethod]
    public void Exists_ReturnsFalseForBool()
        => Assert.IsTrue(Body("HasEmployee", "bool").Any(l => l.Contains("return false;")));

    [TestMethod]
    public void NoBody_ReferencesAnUndeclaredItemsField()
    {
        foreach (var verb in new[] { "GetById", "FindByName", "GetAll", "UpdateX", "DeleteX" })
            Assert.IsFalse(
                Body(verb, "Employee").Any(l => !l.TrimStart().StartsWith("//") && l.Contains("_items")),
                $"{verb}: no executable statement may reference _items");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --nologo`
Expected: FAIL — `Update_NonVoid_EmitsAReturn_SoItCompiles` and `Delete_NonVoid_EmitsAReturn` fail (no return emitted today).

- [ ] **Step 3: Update the generator**

In `MethodBodyGenerator.cs`, change the dispatch calls to pass `method`:
```csharp
        if (StartsWithAny(lower, "exists", "has"))
            return GenerateExists(method);

        if (StartsWithAny(lower, "count"))
            return GenerateCount(method);
```
```csharp
        if (StartsWithAny(lower, "update", "edit", "modify"))
            return GenerateUpdate(method);

        if (StartsWithAny(lower, "delete", "remove"))
            return GenerateDelete(method);
```

Replace `GenerateGet` guidance and the four stub methods:
```csharp
    private static IEnumerable<string> GenerateGet(MethodDefinition method)
    {
        var lines = new List<string>
        {
            $"// Look up and return the {method.ReturnType} this method retrieves.",
            "// Generated stub -- replace with the real query."
        };

        lines.AddRange(GenerateDefault(method.ReturnType));

        return lines;
    }
```
```csharp
    private static IEnumerable<string> GenerateUpdate(MethodDefinition method)
    {
        var lines = new List<string>
        {
            "// Apply the requested changes to the target entity here.",
            "// Generated stub -- replace with the real update."
        };

        lines.AddRange(GenerateDefault(method.ReturnType));

        return lines;
    }

    private static IEnumerable<string> GenerateDelete(MethodDefinition method)
    {
        var lines = new List<string>
        {
            "// Remove the target entity here.",
            "// Generated stub -- replace with the real delete."
        };

        lines.AddRange(GenerateDefault(method.ReturnType));

        return lines;
    }
```
```csharp
    private static IEnumerable<string> GenerateExists(MethodDefinition method)
    {
        var lines = new List<string>
        {
            "// Return whether a matching entity exists.",
            "// Generated stub -- replace with the real check."
        };

        lines.AddRange(GenerateDefault(method.ReturnType));

        return lines;
    }

    private static IEnumerable<string> GenerateCount(MethodDefinition method)
    {
        var lines = new List<string>
        {
            "// Return the number of matching records.",
            "// Generated stub -- replace with the real count."
        };

        lines.AddRange(GenerateDefault(method.ReturnType));

        return lines;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --nologo`
Expected: PASS (all tests, including any existing `GeneratedProjectBuildTests`).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/MethodBodyGenerator.cs CodeGenerator.Tests/Core/MethodBodyGeneratorTests.cs
git commit -m "Emit richer, always-compiling generated method bodies"
```

---

### Task 7: Broader disambiguation detection in the recognizer (spec #7)

**Files:**
- Modify: `DomainDiscovery/Recognition/WikipediaTopicRecognizer.cs` (the `DisambiguationLead` regex, ~line 71-72)
- Test: `DomainDiscovery.Tests/Recognition/WikipediaTopicRecognizerTests.cs` (append) — **xUnit**

**Interfaces:**
- Consumes: `FakeWikipediaClient` (`HitsByTerm`, `Leads`), `SearchHit`, `WikipediaTopicRecognizer.Recognize`.
- Produces: no API change — only the private `DisambiguationLead` pattern widens.

- [ ] **Step 1: Write the failing tests**

Append to `WikipediaTopicRecognizerTests.cs`:
```csharp
    [Fact]
    public void Recognize_RecoversRealArticle_WhenTopHitIsAnyOfTheFollowingDisambiguation()
    {
        var client = new FakeWikipediaClient();
        client.HitsByTerm["Point"] = new()
        {
            new SearchHit("Point", "https://en.wikipedia.org/wiki/Point", 0),
            new SearchHit("Point of sale", "https://en.wikipedia.org/wiki/Point_of_sale", 1),
        };
        // A disambiguation lead the OLD regex missed: it ends on "following:", not "to:".
        client.Leads["Point"] = "Point may refer to any of the following:";
        client.Leads["Point of sale"] =
            "A point of sale is the time and place at which a retail transaction is completed.";

        var topic = new WikipediaTopicRecognizer(client).Recognize(new[] { "Point" });

        Assert.Equal("Point of sale", topic.Title);
    }

    [Fact]
    public void Recognize_RefusesAMayMeanDisambiguation()
    {
        var client = new FakeWikipediaClient();
        client.HitsByTerm["Mercury"] = new()
        {
            new SearchHit("Mercury", "https://en.wikipedia.org/wiki/Mercury", 0),
        };
        client.Leads["Mercury"] = "Mercury may mean:";

        Assert.False(new WikipediaTopicRecognizer(client).Recognize(new[] { "Mercury" }).IsRecognized);
    }

    [Fact]
    public void Recognize_KeepsARealDefinition_ThatMerelyMentionsReferTo()
    {
        var client = new FakeWikipediaClient();
        client.HitsByTerm["Invoice"] = new()
        {
            new SearchHit("Invoice", "https://en.wikipedia.org/wiki/Invoice", 0),
        };
        // Prose that says "refers to" mid-sentence and ends like a real sentence (period, no colon).
        client.Leads["Invoice"] =
            "An invoice refers to a commercial document issued by a seller to a buyer.";

        Assert.Equal("Invoice", new WikipediaTopicRecognizer(client).Recognize(new[] { "Invoice" }).Title);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --nologo`
Expected: FAIL — the "any of the following:" and "may mean:" leads are not detected as disambiguation, so those topics are wrongly recognized. (`KeepsARealDefinition...` should already pass and must keep passing.)

- [ ] **Step 3: Broaden the regex**

In `WikipediaTopicRecognizer.cs`, replace the `DisambiguationLead` definition:
```csharp
    /// <summary>
    /// A disambiguation page's lead does not describe a thing; it announces a list and closes on the
    /// colon that introduces it. Real disambiguation leads take several forms — "Point may refer
    /// to:", "Point may refer to any of the following:", "Mercury may mean:", "PIN stands for:" —
    /// so this matches one of those disambiguation verbs followed, with no sentence break, by the
    /// trailing colon. Requiring a colon at the very end and forbidding an intervening period keeps
    /// it off ordinary prose that merely says what a term "refers to" ("An invoice refers to a
    /// commercial document ... .").
    /// </summary>
    private static readonly Regex DisambiguationLead = new(
        @"\b(?:refers?\s+to|means?|stands?\s+for)\b[^.]*:\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --nologo`
Expected: PASS — including the pre-existing `Recognize_RefusesADisambiguationPage_EvenWhenItIsTheOnlyTopRankedHit` ("...may refer to:") and the new `KeepsARealDefinition...` negative case.

- [ ] **Step 5: Commit**

```bash
git add DomainDiscovery/Recognition/WikipediaTopicRecognizer.cs DomainDiscovery.Tests/Recognition/WikipediaTopicRecognizerTests.cs
git commit -m "Detect more disambiguation-lead forms so recognition recovers"
```

---

## Final verification

- [ ] **Full suite across affected projects**

Run:
```bash
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --nologo
dotnet test DomainDiscovery.Tests/DomainDiscovery.Tests.csproj --nologo
```
Expected: both green.

- [ ] **Rebuild the CLI before any manual check** (test runs leave `CodeGenerator/bin` stale)

Run: `dotnet build CodeGenerator/CodeGenerator.csproj -c Debug --nologo`
Expected: build succeeds.
