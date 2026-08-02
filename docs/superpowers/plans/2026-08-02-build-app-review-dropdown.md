# Build-App Review Loop Action Picker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `build app` review loop's dense two-line command help and typed line with an arrow-key action picker, keeping the typed path intact for redirected input.

**Architecture:** A new pure `ReviewActions` catalog names each action and the arguments it needs, and composes the same `string[] parts` the typed line produces — so the existing `switch` and every tested helper below it are untouched. The `approve` case's scaffolding body is lifted into `Generate(...)` that both input paths call. Only the console-facing shell in `RunReviewLoop` is new, and it is guarded so redirected input keeps today's behaviour exactly.

**Tech Stack:** C# / .NET 10, MSTest, existing `SelectionPrompt` + `SelectionState` arrow-key components.

Spec: `docs/superpowers/specs/2026-08-02-build-app-review-dropdown-design.md`

## Global Constraints

- Repository root is `D:\SmartCodeGenerator-Repository`. The solution lives at `D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx`.
- CI runs `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-restore -warnaserror` then `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build`. The build must produce **0 warnings** — analyzer warnings are errors there even though a local Debug build hides them.
- **Verify with `--no-incremental`.** A plain `dotnet build` that finds nothing changed prints "Build succeeded" *without recompiling*, so the analyzers never run and an analyzer error stays invisible. Always: `dotnet build … -c Release -warnaserror --no-incremental`.
- MSTest analyzer MSTEST0037 forbids `Assert.IsTrue` for comparisons and `Assert.AreEqual` for collection counts. Use `Assert.IsLessThan(upperBound, value, message)` and `Assert.IsGreaterThanOrEqualTo(lowerBound, value, message)` — bound first, value second — and `Assert.HasCount(expectedCount, collection)` for counts.
- Do **not** change the signatures of `BuildAppCommand.ResolveTarget`, `ResolvePersistence`, `IsPersistenceAllowed`, `AddLine`, `WhyLines`, or `FieldSpec.Parse`. Their existing tests must keep passing untouched.
- Do **not** change the typed input path's parsing or its help text.
- `CodeGenerator/App/BuildAppCommand.cs` has **uncommitted working-tree edits** (a reformatted one-line help string near line 255, and a commented-out `FilesWritten` loop near line 317). Preserve both exactly. Commit them separately first if you prefer a clean base.
- Console-facing code is untested by convention (see the remarks on `SelectionPrompt` and `CommandLineEditor`); every decision lives in a pure type that is tested.

---

### Task 1: `ReviewActions` catalog and `Compose`

**Files:**
- Create: `D:\SmartCodeGenerator-Repository\CodeGenerator\App\ReviewActions.cs`
- Test: `D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\App\ReviewActionsTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `internal sealed record ReviewAction(string Verb, string Label, IReadOnlyList<string> ArgumentPrompts)`
  - `internal static class ReviewActions` with `const string Title`, `IReadOnlyList<ReviewAction> All`, `IReadOnlyList<string> Labels`, and `static string[] Compose(ReviewAction action, IReadOnlyList<string> answers)`.
  - Task 3 calls `ReviewActions.Title`, `.Labels`, `.All[index]` and `.Compose(...)`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests\App\ReviewActionsTests.cs`:

```csharp
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Covers the review loop's action catalog and the bridge back to the `string[] parts` its
/// switch already understands. The loop itself reads the console and cannot be driven from a
/// test, so everything decidable about an action lives here.
/// </summary>
[TestClass]
public class ReviewActionsTests
{
    private static ReviewAction Action(string verb)
        => ReviewActions.All.Single(a => a.Verb == verb);

    [TestMethod]
    public void All_CoversExactlyTheVerbsTheSwitchHandles()
    {
        string[] expected =
            ["approve", "add", "remove", "rename", "field", "unfield", "why", "cancel"];

        CollectionAssert.AreEquivalent(
            expected, ReviewActions.All.Select(a => a.Verb).ToArray());
    }

    [TestMethod]
    public void Labels_AreTheCatalogsLabelsInOrder()
        => CollectionAssert.AreEqual(
            ReviewActions.All.Select(a => a.Label).ToArray(),
            ReviewActions.Labels.ToArray());

    [TestMethod]
    [DataRow("approve", 0)]
    [DataRow("cancel", 0)]
    [DataRow("add", 1)]
    [DataRow("remove", 1)]
    [DataRow("why", 1)]
    [DataRow("rename", 2)]
    [DataRow("field", 2)]
    [DataRow("unfield", 2)]
    public void ArgumentPrompts_MatchWhatTheSwitchCaseNeeds(string verb, int expected)
        => Assert.HasCount(expected, Action(verb).ArgumentPrompts);

    [TestMethod]
    [DataRow("rename")]
    [DataRow("field")]
    [DataRow("unfield")]
    public void Compose_TwoArgumentAction_SatisfiesItsLengthGuard(string verb)
    {
        // The switch guards these cases with `when parts.Length >= 3`.
        var parts = ReviewActions.Compose(Action(verb), ["Book", "Novel"]);

        Assert.IsGreaterThanOrEqualTo(3, parts.Length, verb);
        Assert.AreEqual(verb, parts[0]);
    }

    [TestMethod]
    [DataRow("add")]
    [DataRow("remove")]
    public void Compose_OneArgumentAction_SatisfiesItsLengthGuard(string verb)
    {
        // The switch guards these cases with `when parts.Length >= 2`.
        var parts = ReviewActions.Compose(Action(verb), ["Book"]);

        CollectionAssert.AreEqual(new[] { verb, "Book" }, parts);
    }

    [TestMethod]
    public void Compose_NoArgumentAction_IsJustTheVerb()
        => CollectionAssert.AreEqual(
            new[] { "approve" }, ReviewActions.Compose(Action("approve"), []));

    [TestMethod]
    public void Compose_WhyWithNoName_YieldsTheWholeReportForm()
    {
        // WhyLines treats an absent name as "the whole report", and the switch's `why` case has
        // no length guard — so a blank answer has to disappear rather than arrive as "".
        var parts = ReviewActions.Compose(Action("why"), [""]);

        CollectionAssert.AreEqual(new[] { "why" }, parts);
    }

    [TestMethod]
    public void Compose_WhyWithName_KeepsIt()
        => CollectionAssert.AreEqual(
            new[] { "why", "Book" }, ReviewActions.Compose(Action("why"), ["Book"]));

    [TestMethod]
    public void Compose_MultiWordAnswer_StaysOneElement()
    {
        // The typed path's Split(' ', 3) would break this into pieces; composing must not.
        // ToUserTypedEntityName turns the single element into CashRegister downstream.
        var parts = ReviewActions.Compose(Action("rename"), ["Book", "cash register"]);

        CollectionAssert.AreEqual(new[] { "rename", "Book", "cash register" }, parts);
    }

    [TestMethod]
    public void Compose_TrimsEachAnswer()
        => CollectionAssert.AreEqual(
            new[] { "add", "Book" }, ReviewActions.Compose(Action("add"), ["  Book  "]));
}
```

- [ ] **Step 2: Run the test to verify it fails**

```
dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ReviewActionsTests" --nologo
```

Expected: build failure, `CS0246: The type or namespace name 'ReviewAction' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `CodeGenerator\App\ReviewActions.cs`:

```csharp
namespace CodeGenerator.App;

/// <summary>
/// One entry in the review loop's action list: the verb its switch case matches, the row the
/// picker shows, and the arguments the loop collects before that verb can run.
/// </summary>
internal sealed record ReviewAction(
    string Verb,
    string Label,
    IReadOnlyList<string> ArgumentPrompts);

/// <summary>
/// The review loop's action catalog, and the bridge from a picked action back to the
/// <c>string[] parts</c> its switch already understands.
/// </summary>
/// <remarks>
/// Pure and tested for the same reason <see cref="BuildAppCommand.ResolveTarget"/> is: the loop
/// reads the console and cannot be driven from a test, so everything decidable is lifted out of
/// it. Composing <c>parts</c> — rather than formatting a line and re-splitting it — is what lets
/// the picker reuse the existing switch, and every helper below it, untouched.
/// </remarks>
internal static class ReviewActions
{
    public const string Title = "What next?";

    public static IReadOnlyList<ReviewAction> All { get; } =
    [
        new("approve", "approve   — generate the app", []),
        new("add",     "add       — add an entity", ["New entity name"]),
        new("remove",  "remove    — remove an entity", ["Entity to remove"]),
        new("rename",  "rename    — rename an entity", ["Entity to rename", "New name"]),
        new("field",   "field     — add a field to an entity",
            ["Entity", "Field, name or name:type"]),
        new("unfield", "unfield   — remove a field from an entity",
            ["Entity", "Field to remove"]),
        new("why",     "why       — explain how the model was decided",
            ["Entity (blank for the whole report)"]),
        new("cancel",  "cancel    — nothing is generated", []),
    ];

    public static IReadOnlyList<string> Labels { get; } =
        All.Select(a => a.Label).ToList();

    /// <summary>
    /// The parts array the review loop's switch expects: the verb followed by the answers,
    /// each trimmed.
    /// </summary>
    /// <remarks>
    /// A trailing blank answer is dropped, which is how <c>why</c> with no name becomes the
    /// whole-report form <see cref="BuildAppCommand.WhyLines"/> already handles. No other action
    /// can reach here with a blank: the prompting layer abandons those instead of composing.
    /// </remarks>
    public static string[] Compose(ReviewAction action, IReadOnlyList<string> answers)
    {
        var parts = new List<string> { action.Verb };

        parts.AddRange(answers.Select(a => a.Trim()));

        while (parts.Count > 1 && parts[^1].Length == 0)
            parts.RemoveAt(parts.Count - 1);

        return [.. parts];
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```
dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ReviewActionsTests" --nologo
```

Expected: PASS, 20 tests (10 methods; `ArgumentPrompts_MatchWhatTheSwitchCaseNeeds` expands to 8 rows, `Compose_TwoArgumentAction_SatisfiesItsLengthGuard` to 3, `Compose_OneArgumentAction_SatisfiesItsLengthGuard` to 2).

- [ ] **Step 5: Commit**

```bash
cd /d/SmartCodeGenerator-Repository
git add CodeGenerator/App/ReviewActions.cs CodeGenerator.Tests/App/ReviewActionsTests.cs
git commit -m "feat(build-app): add the review loop action catalog

ReviewActions names each review action, the row the picker shows for it, and the
arguments the loop must collect. Compose turns a picked action plus its answers
into the same string[] parts the typed line produces, so the existing switch and
every helper below it stay untouched.

Nothing calls it yet.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Extract `Generate` from the `approve` case

Behaviour-preserving refactor. The picker path holds a target and a persistence kind as values, so the scaffolding body must be reachable without going back through `ResolveTarget`/`ResolvePersistence`, which read them out of a typed line.

**Files:**
- Modify: `D:\SmartCodeGenerator-Repository\CodeGenerator\App\BuildAppCommand.cs` (the `approve` case, roughly `:276`-`:324`)

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `private static void Generate(CandidateDomainModel model, string appName, string workspaceRoot, ImportedData importedData, ITargetScaffolder target, PersistenceKind persistence)` — Task 3 calls it for the picker path.

- [ ] **Step 1: Replace the tail of the `approve` case**

In the `case "a": case "approve":` block, replace everything from `var scaffold = new AppScaffolder()` down to (but not including) the `return;` that ends the case with a single call. **Keep the commented-out `FilesWritten` loop** — move it into `Generate` with the comment markers intact.

The case's tail becomes:

```csharp
                    Generate(
                        model, appName, workspaceRoot, importedData,
                        chosenTarget, persistence.Value);
                    return;
```

Everything above it in the case — the empty-model guard, `ResolveTarget`, the unknown-target message, `ResolvePersistence`, the unknown-persistence message, and the `IsPersistenceAllowed` message — is unchanged.

- [ ] **Step 2: Add the extracted method**

Place it immediately after `RunReviewLoop` ends (before the `ResolveTarget` summary comment):

```csharp
    /// <summary>
    /// Scaffolds the approved model and reports what was written.
    /// </summary>
    /// <remarks>
    /// Extracted from the <c>approve</c> case so the picker path can generate directly: it
    /// already holds a target and a persistence kind from its own prompts, while
    /// <see cref="ResolveTarget"/> and <see cref="ResolvePersistence"/> exist to read those out
    /// of a typed line.
    /// </remarks>
    private static void Generate(
        CandidateDomainModel model, string appName, string workspaceRoot,
        ImportedData importedData, ITargetScaffolder target, PersistenceKind persistence)
    {
        var scaffold = new AppScaffolder()
            .Scaffold(model, appName, workspaceRoot, target, persistence, importedData);

        if (scaffold.Success)
        {
            Console.WriteLine($"Generated project at {scaffold.ProjectPath}");
            if (scaffold.TestProjectPath is not null)
                Console.WriteLine($"Generated test project at {scaffold.TestProjectPath} — run with `dotnet test`.");
            Console.WriteLine("");
            //foreach (var file in scaffold.FilesWritten)
            //    Console.WriteLine($"  {Path.GetRelativePath(scaffold.ProjectPath, file)}");
        }
        else
        {
            Console.WriteLine($"Generation failed: {scaffold.Message}");
        }
    }
```

- [ ] **Step 3: Verify nothing changed behaviourally**

```
dotnet build D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx -c Release -warnaserror --nologo
dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx -c Release --no-build --nologo
```

Expected: build 0 warnings / 0 errors; all four test projects pass with the same counts as before the task (`CodeGenerator.Tests` 701 + the 20 from Task 1 = 721).

- [ ] **Step 4: Commit**

```bash
cd /d/SmartCodeGenerator-Repository
git add CodeGenerator/App/BuildAppCommand.cs
git commit -m "refactor(build-app): lift the approve case's scaffolding into Generate

The approve case derives its target and persistence from the typed parts array.
The action picker will hold both as values already, so the scaffolding body has
to be reachable without going back through ResolveTarget and ResolvePersistence.

Pure extraction — no behaviour change.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Drive the review loop from the picker

**Files:**
- Modify: `D:\SmartCodeGenerator-Repository\CodeGenerator\App\BuildAppCommand.cs` (`RunReviewLoop`, roughly `:242`-`:275`, plus four new private helpers)

**Interfaces:**
- Consumes: `ReviewActions.Title`, `.Labels`, `.All`, `.Compose(...)` and the `ReviewAction` record from Task 1; `Generate(...)` from Task 2; the existing `SelectionPrompt.Choose(string title, IReadOnlyList<string> labels, int defaultIndex) -> int?` and `ApplicationTypeMenu.Choose() -> int?` / `.Resolve(int) -> ITargetScaffolder`.
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Replace the input half of the loop body**

In `RunReviewLoop`, keep `PrintModel(model);` and the `Target:` line exactly as they are. Replace the block that starts at the two help `Console.WriteLine` calls and ends at `var verb = parts[0].ToLowerInvariant();` with:

```csharp
            string[] parts;

            if (UseTypedInput)
            {
                // The typed path is unchanged: SelectionPrompt cannot run without a console, and
                // piped scripts drive the loop through exactly these lines.
                Console.WriteLine("\n       [a]pprove [target] [sqlite] add <name>  remove <name>  rename <old> <new>  field <entity> <name>[:<type>]  unfield <entity> <name>  why [name]");
                Console.WriteLine("         [c]ancel");
                Console.Write("> ");

                var input = Console.ReadLine();
                if (input is null)   // stdin closed / EOF — treat as cancel, never busy-loop
                {
                    Console.WriteLine("Input closed. Cancelled — nothing was generated.");
                    return;
                }

                var line = input.Trim();

                if (line.Length == 0)
                    continue;

                parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            }
            else
            {
                var picked = PickAction();

                if (picked is null)   // Esc on the list reads as cancel
                {
                    Console.WriteLine("Cancelled. Nothing was generated.");
                    return;
                }

                // approve is the one action whose arguments are pickers rather than typed lines,
                // and it already holds the values Generate needs — so it never reaches the switch.
                if (picked.Verb == "approve")
                {
                    if (model.Entities.Count == 0)
                    {
                        Console.WriteLine("Nothing to generate — the model is empty.");
                        return;
                    }

                    var approved = ApproveInteractively(target);

                    if (approved is null)   // Esc at a picker: back to the action list
                        continue;

                    Generate(
                        model, appName, workspaceRoot, importedData,
                        approved.Value.Target, approved.Value.Persistence);
                    return;
                }

                var answers = AskArguments(picked);

                if (answers is null)   // blank or EOF: abandon the action
                    continue;

                parts = ReviewActions.Compose(picked, answers);
            }

            var verb = parts[0].ToLowerInvariant();
```

**Note:** the help string on the first `Console.WriteLine` must be copied verbatim from the current working tree, including the leading `\n` and the spacing — it is an uncommitted edit that this plan preserves.

The `switch (verb)` below it, and every case in it, is unchanged.

- [ ] **Step 2: Add the four helpers**

Place them immediately after `RunReviewLoop` and before `Generate`:

```csharp
    /// <summary>
    /// Whether the review loop must fall back to typed lines. <see cref="SelectionPrompt"/> needs
    /// <c>Console.ReadKey</c> and <c>Console.SetCursorPosition</c>, both of which throw with no
    /// console attached. Matches the guard in <see cref="ApplicationTypeMenu.Choose"/>.
    /// </summary>
    private static bool UseTypedInput
        => Console.IsInputRedirected || Console.IsOutputRedirected;

    /// <summary>The action the user picked, or null if they pressed Esc.</summary>
    private static ReviewAction? PickAction()
    {
        var index = SelectionPrompt.Choose(ReviewActions.Title, ReviewActions.Labels, 0);

        return index is null ? null : ReviewActions.All[index.Value];
    }

    /// <summary>
    /// Collects <paramref name="action"/>'s arguments, one typed line each. Null means the user
    /// abandoned the action — a blank answer, or stdin closing — and the loop returns to the list.
    /// </summary>
    private static IReadOnlyList<string>? AskArguments(ReviewAction action)
    {
        var answers = new List<string>();

        foreach (var prompt in action.ArgumentPrompts)
        {
            Console.Write($"{prompt}> ");

            var answer = Console.ReadLine();

            if (answer is null)
                return null;

            answer = answer.Trim();

            // `why` is the one action where a blank answer means something — the whole report —
            // so it is the one action a blank does not abandon.
            if (answer.Length == 0 && action.Verb != "why")
                return null;

            answers.Add(answer);
        }

        return answers;
    }

    /// <summary>
    /// Asks for what <c>approve</c> needs through pickers: the application type when the wizard
    /// has not already settled one, and the persistence backend when the chosen target can
    /// actually honour SQLite. Null means the user pressed Esc and nothing should be generated.
    /// </summary>
    private static (ITargetScaffolder Target, PersistenceKind Persistence)? ApproveInteractively(
        ITargetScaffolder? preselected)
    {
        var target = preselected;

        if (target is null)
        {
            var index = ApplicationTypeMenu.Choose();

            if (index is null)
                return null;

            target = ApplicationTypeMenu.Resolve(index.Value);
        }

        // WPF and WinForms read JsonStore directly, so offering a choice here would only lead to
        // the rejection the typed path prints. Skip the question instead.
        if (!IsPersistenceAllowed(target, PersistenceKind.Sqlite))
            return (target, PersistenceKind.Json);

        var persistence = SelectionPrompt.Choose("Persistence", ["JSON", "SQLite"], 0);

        return persistence is null
            ? null
            : (target, persistence.Value == 0 ? PersistenceKind.Json : PersistenceKind.Sqlite);
    }
```

- [ ] **Step 3: Run the full CI-equivalent verification**

```
dotnet build D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx -c Release -warnaserror --nologo
dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx -c Release --no-build --nologo
```

Expected: 0 warnings, 0 errors; all four test projects pass. Counts unchanged from Task 2 — no test targets the console shell.

- [ ] **Step 4: Verify the redirected path is untouched**

Rebuild the CLI first: `dotnet test` leaves `CodeGenerator\bin` stale, so a piped run can otherwise exercise an old binary.

```
dotnet build D:\SmartCodeGenerator-Repository\CodeGenerator\CodeGenerator.csproj -c Debug --nologo
```

Then drive it with piped stdin (which forces `UseTypedInput`) and confirm the help lines and `> ` prompt still appear and a typed command still works:

```
printf 'build app: a library with members and loans\nadd Shelf\ncancel\nexit\n' | dotnet run --project D:\SmartCodeGenerator-Repository\CodeGenerator\CodeGenerator.csproj --no-build
```

Expected: the two help lines print, `add Shelf` reports `Added 'Shelf'.`, `cancel` reports `Cancelled. Nothing was generated.`

- [ ] **Step 5: Verify the picker by hand**

The picker cannot be driven from a test or a pipe — it needs a real console. Run the CLI interactively in a terminal (not through a tool that captures stdout) and check:

1. `build app: a library with members and loans` reaches the review loop and shows the 8-row list instead of the help lines.
2. ↑/↓ moves the highlight; Esc reports `Cancelled. Nothing was generated.`
3. `add` prompts `New entity name> `; a name is added; a blank answer returns to the list without an error.
4. `why` with a blank answer prints the whole report rather than reporting an unknown name.
5. `approve` on a wizard-selected target skips the application-type picker; on a WPF target it also skips the persistence picker; on a Console target it offers JSON/SQLite.
6. The model reprints above the list on every iteration and nothing from the previous iteration is left on screen.

- [ ] **Step 6: Commit**

```bash
cd /d/SmartCodeGenerator-Repository
git add CodeGenerator/App/BuildAppCommand.cs
git commit -m "feat(build-app): pick the review action from a list

The review loop advertised nine commands as two lines of dense syntax and read a
typed line. It now shows an arrow-key list of the actions and prompts for the
picked action's arguments; approve asks for its target and persistence through
pickers, skipping either when the wizard already settled it or when the target
cannot honour SQLite.

The picker composes the same parts array the typed line produces, so the switch
and every helper below it are untouched. Redirected input keeps the typed path
verbatim: SelectionPrompt needs ReadKey and SetCursorPosition, which throw with no
console attached, and piped scripts drive the loop through those lines.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage:** interactive list → Task 3 Step 1; argument prompts → Task 1 catalog + Task 3 `AskArguments`; `approve` pickers → Task 3 `ApproveInteractively`; redirected path unchanged → Task 3 Step 1 `UseTypedInput` branch + Step 4; `ReviewActions` → Task 1; `Generate` extraction → Task 2; error handling table → Task 3 Steps 1-2 (Esc, blank, EOF, empty model); testing → Task 1 Step 1 plus Task 3 Steps 3-5.
- **Type consistency:** `ReviewAction`, `ReviewActions.All/Labels/Title/Compose`, `Generate`, `PickAction`, `AskArguments`, `ApproveInteractively`, `UseTypedInput` are spelled identically everywhere they appear.
- **Not covered, by decision:** entity pickers, and prose-tolerant entity lookup — both recorded under "Considered and rejected" in the spec.
