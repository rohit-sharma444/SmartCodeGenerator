# Validation Phase 3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Read the rows the tool already holds, offer the user the rules that hold in every one of them, and enforce in the generated app the three checks that need a record other than the one in hand.

**Architecture:** A `ColumnScan` runs once per column over the complete `TabularTable`s at `DataModelBuilder.Build` — the one seam the console and the browser share while the uncapped rows are still in memory. It produces `RuleProposal`s that ride on `CandidateField` beside `Facts`, so a review-loop rename cannot orphan them. Nothing is accepted until the user picks it; an accepted proposal becomes a `PropertyRule` in `DomainModelMapper.InferValidation`, the one place facts already become rules, and reaches the generated app by the road every existing rule travels. In the generated app a new `Entity` stage joins `Security` and `Field`, carrying reference-exists, uniqueness and referential integrity on delete.

**Tech Stack:** C# / .NET 10, MSTest, Roslyn (`Compiler.CompileSources`) for compile tests, Blazor Server (bUnit) for the browser review page, WPF/WinForms/Blazor emitted front ends.

**Spec:** `docs/superpowers/specs/2026-09-11-validation-phase-3-design.md` (parent: `docs/superpowers/specs/2026-09-06-validation-architecture-design.md`)

**Branch:** `validation-phase-3`, already created, with the spec committed on it.

## Global Constraints

- **CI builds `SmartCodeGenerator/SmartCodeGenerator.slnx` in Release with `--no-incremental -warnaserror`, then `dotnet test --no-build`.** A local Debug build of one project hides analyzer errors. Before declaring a task done, run:
  `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror`
- **`[DataTestMethod]` is MSTEST0044 and an error under `-warnaserror`.** Use `[TestMethod]` with `[DataRow]`.
- **`Assert.AreEqual(n, x.Count)` is MSTEST0037.** Use `Assert.HasCount(n, x)`.
- **Emitted C# is tested by COMPILING it**, with `Compiler.CompileSources` over the real files in their real namespaces. `EmittedSource.Flatten` strips `using` and `namespace`, so a flattened fixture proves nothing about cross-namespace resolution. Both halves of a partial class are compiled together, never concatenated.
- **Red before green.** Every task writes the failing test first and runs it to see it fail for the stated reason.
- **Never infer a rule from a sample.** Any scan added by this plan reads `TabularTable`, never `ImportedData` (capped at `ImportedData.MaxSeedRows = 1000`).
- **A proposal is never pre-accepted.** `RuleProposal.Accepted` starts `false` and only a user action sets it.
- Repo root is `D:\SmartCodeGenerator-Repository`; the solution is one level down in `SmartCodeGenerator/`.
- The checkout is CRLF; the Write tool produces LF-only files. Existing files keep their endings — never rewrite a whole file to change them.
- Rule name strings used by this plan, all lowercase, compared `OrdinalIgnoreCase`: `required`, `maxlength`, `minvalue`, `maxvalue`, `scale`, `mindate`, `dateonly` (existing) and `allowedvalues`, `pattern`, `unique` (new).
- Commit after every task. Attribution line on every commit:
  `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`

---

## File Structure

**New files**

| File | Responsibility |
|---|---|
| `DataIngestion/Schema/ColumnScan.cs` | One pass over one column: distinct values to a cap, longest value, duplicate count, non-blank count. Knows nothing about rules. |
| `DataIngestion/RuleProposals.cs` | Turns a `ColumnScanResult` into zero or more `RuleProposal`s. Knows the three kinds and their thresholds. |
| `CodeGenerationEngine/Models/RuleProposal.cs` | The proposal itself, beside `TypeFacts` and `PropertyRule` so DataIngestion, DomainDiscovery and AppScaffolding can all see it. |
| `AppScaffolding/ActingUserTemplate.cs` | Emits `Validation/ActingUser.cs`, replacing five copies of `CurrentRole()`. |
| `CodeGenerator.Tests/Helper/EmittedLines.cs` | Shared test helpers: the mixed-line-ending loop and the occurrence counter, currently copied ten times. |

**Modified files**

| File | Change |
|---|---|
| `DomainDiscovery/Model/CandidateDomainModel.cs` | `CandidateField.Proposals`. |
| `DataIngestion/DataModelBuilder.cs` | Attach proposals after relationships are known. |
| `CodeGenerationEngine/Models/PropertyDefinition.cs` | `Proposals`, carried from `CandidateField`. |
| `AppScaffolding/DomainModelMapper.cs` | Carry proposals across; accepted ones become rules. |
| `AppScaffolding/ValidatorTemplate.cs` | `allowedvalues`/`pattern` in `Checks`; the new `Entity` stage; `IEntityLookup.AnyOtherWith`; `EntityStageRules`. |
| `AppScaffolding/ForeignKeys.cs` | `ReferencesTo` — who points at this class, and through which property. |
| `AppScaffolding/AppScaffolder.cs` | Pass the role and the incoming references to `ValidatorTemplate.Generated`. |
| `AppScaffolding/PermissionsTemplate.cs` | `AppHasLogin`. |
| `AppScaffolding/Screens/MasterScreenTemplate.cs`, `AppScaffolding/ViewModelTemplates.cs`, `AppScaffolding/Screens/BlazorScreenTemplates.cs` | Grid deletes run the pipeline. |
| `AppScaffolding/Layouts/Wpf/WpfBusinessLayout.cs` | A status line on the transaction template. |
| `CodeGenerator/App/BuildAppCommand.cs`, `CodeGenerator/App/ReviewActions.cs`, `CodeGenerator/App/ReviewHints.cs` | The `rules` and `accept` verbs. |
| `CodeGenerator/Web/Components/Stages/ReviewStage.razor` | A checkbox per proposal. |
| `CodeGenerator/App/Resume/BuildSnapshot.cs` | Persist `Facts` and the accept/reject decisions. |

---

## Task order and why

Tasks 1-5 are the spec's entry conditions. **Tasks 1-3 are hard prerequisites of task 8** — a delete-time refusal that the deferred-save grid bypasses, or that the WPF transaction screen cannot display, is worse than no refusal at all, because it reports success over a check that did not run.

Tasks 6-8 build the Entity stage. Tasks 9-14 build the scan and the two review surfaces. Task 15 runs a real app, which is the only step that has historically found the defects the other fourteen miss.

---

### Task 1: A status line on the WPF transaction screen

The WPF `MasterTemplate` and `EditorTemplate` each bind `{Binding Status}`; `TransactionTemplate` binds it nowhere, even though `TransactionScreenViewModel<T>` inherits `Status` and its delete writes a refusal into it. Task 8 writes refusals there. Without this, they are invisible.

**Files:**
- Modify: `AppScaffolding/Layouts/Wpf/WpfBusinessLayout.cs:194-215` (the `TransactionTemplate` `DataTemplate`)
- Test: `CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs` (add to the existing file; create it only if absent)

- [ ] **Step 1: Write the failing test**

```csharp
[TestMethod]
public void The_transaction_template_shows_the_status_line()
{
    var xaml = WpfBusinessLayout.AppXaml(AppCustomization.Default);

    var transaction = Between(xaml, "x:Key=\"TransactionTemplate\"", "</DataTemplate>");

    // The master and editor templates have always bound it. A refusal written into Status by
    // the delete handler is invisible on this screen until it does too.
    StringAssert.Contains(transaction, "{Binding Status}");
}

/// <summary>The text between the first occurrence of <paramref name="start"/> and the first
/// <paramref name="end"/> after it — so an assertion about one template cannot pass on
/// another's markup.</summary>
private static string Between(string source, string start, string end)
{
    var from = source.IndexOf(start, StringComparison.Ordinal);
    Assert.IsTrue(from >= 0, $"'{start}' is not in the emitted XAML.");

    var to = source.IndexOf(end, from, StringComparison.Ordinal);
    Assert.IsTrue(to > from, $"'{end}' does not follow '{start}'.");

    return source[from..to];
}
```

Check the real name and signature of the method that emits `App.xaml` before writing the first line — if it is not `AppXaml(AppCustomization)`, use what the file declares. Nothing else in the test changes.

- [ ] **Step 2: Run it and watch it fail**

```
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "The_transaction_template_shows_the_status_line"
```

Expected: FAIL — `Assert.Contains` did not find `{Binding Status}` in the transaction template's markup.

- [ ] **Step 3: Add the binding**

In the `TransactionTemplate`'s top `Grid` (the one holding `{Binding Title}` and the button row), add the status line beside the title, matching the master template's shape at `:147`:

```xml
                <Grid DockPanel.Dock="Top" Margin="0,0,0,10">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="{Binding Title}" FontSize="15" FontWeight="SemiBold" />
                        <TextBlock Text="{Binding Status}" Foreground="{{{muted}}}"
                                   VerticalAlignment="Center" Margin="12,0,0,0" />
                    </StackPanel>
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
```

Keep the rest of the `Grid` exactly as it is. `{{{muted}}}` is the interpolation the surrounding raw string already uses for a theme colour — copy it verbatim from `:147`.

- [ ] **Step 4: Run the test and the whole desktop suite**

```
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "WpfBusinessLayout"
```

Expected: PASS, and no other WPF layout test regresses. Several assert on the XAML's structure.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Layouts/Wpf/WpfBusinessLayout.cs CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs
git commit -m "Give the WPF transaction screen somewhere to say it refused"
```

---

### Task 2: Tell "this app has no login" from "nobody is signed in"

`Permissions.Allows` does `if (role is null) return true;`. That permissive answer is REQUIRED for the four targets with no auth core — a matrix that refused a null role would brick every console, Web API, worker and classlib app. It is wrong only for an app that HAS a login and is currently signed out.

**Files:**
- Modify: `AppScaffolding/PermissionsTemplate.cs` (the `Source` method and its `Allows`)
- Test: `CodeGenerator.Tests/App/PermissionsTemplateTests.cs`

**Interfaces:**
- Produces: `PermissionsTemplate.Source(string rootNamespace, bool login)` — if the method does not already take the login flag, add it as the last parameter and update every call site the compiler names.

- [ ] **Step 1: Write the two failing tests**

```csharp
[TestMethod]
public void An_app_with_a_login_refuses_a_signed_out_caller()
{
    var source = PermissionsTemplate.Source("Acme", login: true);

    StringAssert.Contains(source, "private const bool AppHasLogin = true;");
    StringAssert.Contains(source, "if (role is null)");
    StringAssert.Contains(source, "return !AppHasLogin;");
}

[TestMethod]
public void An_app_with_no_login_still_allows_everything()
{
    var source = PermissionsTemplate.Source("Acme", login: false);

    // The four targets with no auth core have nobody to attribute a write to. Refusing a null
    // role there would refuse every write in every console, Web API and worker app.
    StringAssert.Contains(source, "private const bool AppHasLogin = false;");
}
```

- [ ] **Step 2: Run them and watch them fail**

```
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "PermissionsTemplate"
```

Expected: FAIL — `Source` does not take a `login` argument, or `AppHasLogin` is not emitted.

- [ ] **Step 3: Emit the const and read it**

In `PermissionsTemplate.Source`, add the const inside the emitted `Permissions` class and change the null arm:

```csharp
    /// <summary>Whether this app was generated WITH a login. False for the four targets that have
    /// no auth core at all, where a null role means "this app has no notion of who is acting"
    /// rather than "nobody is signed in".</summary>
    private const bool AppHasLogin = {{(login ? "true" : "false")}};

    public static bool Allows(ValidationOperation op, string entity, string? role)
    {
        // Two different situations arrive here as the same null. An app with no login has nobody
        // to refuse and must allow everything; an app that HAS a login reports null before the
        // first sign-in and again after a sign-out, and that caller is not authorised.
        if (role is null)
            return !AppHasLogin;
```

Update the emitted remarks above `Allows` so they describe what the code now does — the current ones say a null role is always allowed.

- [ ] **Step 4: Fix the call sites the compiler names, then run the suite**

Every caller of `PermissionsTemplate.Source` must pass the login flag it already holds. Then:

```
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

Expected: PASS. Watch specifically for tests that construct a `ValidationContext` with a null `UserRole` against a login app — if any now fail, that is this change working, and the test's expectation is what moves.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/PermissionsTemplate.cs CodeGenerator.Tests/App/PermissionsTemplateTests.cs
git commit -m "Separate an app with no login from an app nobody has signed into"
```

---

### Task 3: The grids ask the pipeline, not the matrix

Four emitted delete handlers call `Permissions.Allows` directly. That is equivalent to a pipeline run only while Security is the only stage that can refuse a delete — which stops being true in task 8. The affordance (a disabled button) keeps asking the matrix, because a whole pipeline run per repaint is not something a button's enabled state can afford; the HANDLER moves to the pipeline.

**Files:**
- Modify: `AppScaffolding/Screens/MasterScreenTemplate.cs:379` (WPF/WinForms master grid)
- Modify: `AppScaffolding/ViewModelTemplates.cs:297` (the plain deferred-save grid)
- Modify: `AppScaffolding/Screens/BlazorScreenTemplates.cs:~915` (login variant) and `:960` (no-login variant)
- Test: `CodeGenerator.Tests/App/DeleteGoesThroughThePipelineTests.cs` (new)

**Interfaces:**
- Consumes: the emitted `ValidatorFactory.Create<T>()`, `ValidationContext<T>`, `StoreEntityLookup`, all from phase 2.
- Produces: every emitted delete handler runs `ValidatorFactory.Create<T>().Validate(...)` with `ValidationOperation.Delete` and reports the first message on refusal.

- [ ] **Step 1: Write the failing test**

```csharp
[TestMethod]
[DataRow("MasterScreen")]
[DataRow("ViewModel")]
[DataRow("BlazorLogin")]
[DataRow("BlazorNoLogin")]
public void Every_grid_delete_runs_the_pipeline(string handler)
{
    var source = DeleteHandlerSource(handler);

    // The matrix alone is equivalent to a pipeline run only while Security is the only stage
    // that can refuse a delete. Referential integrity ends that, and the deferred-save grid is
    // exactly the front end that would then remove a row the pipeline refused and say nothing.
    StringAssert.Contains(source, "ValidationOperation.Delete");
    StringAssert.Contains(source, "ValidatorFactory.Create<");
    StringAssert.Contains(source, ".Validate(");
}
```

`DeleteHandlerSource` calls the four templates with whatever arguments their existing tests already use, and returns the emitted text. Copy those argument lists from the current tests for each template rather than inventing them.

- [ ] **Step 2: Run it and watch it fail**

```
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "Every_grid_delete_runs_the_pipeline"
```

Expected: FAIL on all four rows — none of the handlers names `ValidatorFactory`.

- [ ] **Step 3: Move each handler onto the pipeline**

`MasterScreenTemplate.Delete` — the immediate-save grid, which has the stored record:

```csharp
    private void Delete()
    {
        if (Selected is not T record)
        {
            Status = "Select a record first.";
            return;
        }

        // The whole pipeline, not the matrix alone: Security is no longer the only stage that
        // can refuse a delete, and the button's enabled state cannot afford a pipeline run per
        // repaint — so the affordance asks the matrix and the handler asks this.
        var outcome = ValidatorFactory.Create<T>().Validate(
            new ValidationContext<T>(
                record, ValidationOperation.Delete, record, new StoreEntityLookup(),
                ActingUser.Role(), ActingUser.Id()));

        if (!outcome.IsValid)
        {
            Status = outcome.Messages[0];
            return;
        }

        var all = _store.GetAll();
```

The rest of the method is unchanged. `ActingUser.Role()`/`ActingUser.Id()` arrive in task 4 — until then, use the `CurrentRole()` this template already emits and pass `null` for the id, and task 4 will replace both.

`ViewModelTemplates.Delete` — the deferred-save grid, whose comment about checking here rather than at Save stays true and gains a second reason:

```csharp
    private void Delete()
    {
        if (_selected is not T item)
            return;

        // Checked HERE, not at Save: this screen defers every change to one SaveAll, and a
        // whole-list write cannot tell that a record was removed. There is nowhere later to ask
        // — which is also why this must be the whole pipeline and not the matrix alone. A
        // referential-integrity refusal that this screen skipped would remove the row from the
        // list and write the removal out, silently.
        var outcome = ValidatorFactory.Create<T>().Validate(
            new ValidationContext<T>(
                item, ValidationOperation.Delete, item, new StoreEntityLookup(),
                CurrentRole(), null));

        if (!outcome.IsValid)
        {
            Status = outcome.Messages[0];
            return;
        }

        Records.Remove(item);
```

Both Blazor variants take the same shape, writing to `_status`, with the login variant passing `state?.User.FindFirst(ClaimTypes.Role)?.Value` and the no-login variant passing `null`. `MayDelete` is NOT changed: it stays on the matrix, because it is the affordance.

- [ ] **Step 4: Compile the emitted output, not just the templates**

Add to the same test file a test that generates a small app with two related entities and compiles the whole `Validation/` and screen output with `Compiler.CompileSources`, in their real namespaces. Follow the existing pattern in `CodeGenerator.Tests/App/ValidatorCompilesTests.cs` — copy its source-collection helper rather than writing a new one.

```
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "DeleteGoesThroughThePipeline"
```

Expected: PASS, including the compile.

- [ ] **Step 5: Run everything**

```
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

Expected: PASS. Existing delete-refusal tests may now assert the matrix's sentence where the pipeline's first message is the same text — they should still pass, because `Security` produces `Permissions.Refusal(...)` verbatim.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/Screens/MasterScreenTemplate.cs AppScaffolding/ViewModelTemplates.cs AppScaffolding/Screens/BlazorScreenTemplates.cs CodeGenerator.Tests/App/DeleteGoesThroughThePipelineTests.cs
git commit -m "Run the pipeline at every grid delete, before anything can refuse one"
```

---

### Task 4: One `ActingUser`, not five `CurrentRole()` copies

Five emitted `CurrentRole()` copies in three shapes. Inert today — every product call site passes the flag explicitly — so this is cleanup, and task 3 just added a sixth caller.

**Files:**
- Create: `AppScaffolding/ActingUserTemplate.cs`
- Modify: `AppScaffolding/AppScaffolder.cs` (emit the new file into `Validation/`)
- Modify: the five templates that emit a `CurrentRole()` — find them with
  `grep -rn "CurrentRole" --include=*.cs AppScaffolding/`
- Test: `CodeGenerator.Tests/App/ActingUserTests.cs` (new)

**Interfaces:**
- Produces: emitted `Validation/ActingUser.cs` with `internal static class ActingUser { public static string? Role(); public static int? Id(); }` in `<root>.Validation`.

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public void A_login_app_reads_the_acting_user_from_the_session()
{
    var source = ActingUserTemplate.Source("Acme", login: true);

    StringAssert.Contains(source, "namespace Acme.Validation;");
    StringAssert.Contains(source, "public static string? Role()");
    StringAssert.Contains(source, "public static int? Id()");
    StringAssert.Contains(source, "Session.Current");
}

[TestMethod]
public void An_app_with_no_login_has_nobody_acting()
{
    // No Session type is emitted at all for a target with no auth core, so naming one here is
    // the CS0234 that issue #126 is about. This returns null without referring to it.
    var source = ActingUserTemplate.Source("Acme", login: false);

    Assert.IsFalse(source.Contains("Session", StringComparison.Ordinal));
    StringAssert.Contains(source, "public static string? Role() => null;");
}

[TestMethod]
public void No_template_emits_its_own_CurrentRole()
{
    foreach (var (name, source) in EveryEmittedScreenSource())
        Assert.IsFalse(
            source.Contains("CurrentRole", StringComparison.Ordinal),
            $"{name} still emits its own copy.");
}
```

`EveryEmittedScreenSource()` returns each screen/store template's emitted text with the arguments its own existing tests use.

- [ ] **Step 2: Run them and watch them fail**

Expected: FAIL — `ActingUserTemplate` does not exist.

- [ ] **Step 3: Write the template**

```csharp
namespace AppScaffolding;

/// <summary>Emits <c>Validation/ActingUser.cs</c>: who is acting, in one place.</summary>
/// <remarks>
/// Five templates used to emit their own <c>CurrentRole()</c> in three different shapes. They
/// agreed, but nothing held them together, and every new caller was a sixth chance to disagree.
/// </remarks>
public static class ActingUserTemplate
{
    /// <summary>Source of <c>Validation/ActingUser.cs</c>.</summary>
    public static string Source(string rootNamespace, bool login) => login
        ? $$"""
using {{rootNamespace}}.Auth;

namespace {{rootNamespace}}.Validation;

/// <summary>Who is acting, for the context every validation carries.</summary>
internal static class ActingUser
{
    /// <summary>The signed-in user's role, or null before a sign-in and after a sign-out.</summary>
    public static string? Role() => Session.Current?.Role.ToString();

    /// <summary>The signed-in user's id, or null when nobody is signed in.</summary>
    public static int? Id() => Session.Current?.Id;
}
"""
        : $$"""
namespace {{rootNamespace}}.Validation;

/// <summary>Who is acting. This app was generated with no login, so there is nobody — and no
/// Auth namespace to name, which is what makes this the no-login half rather than a null check.
/// </summary>
internal static class ActingUser
{
    public static string? Role() => null;

    public static int? Id() => null;
}
""";
}
```

Check `Session.Current`'s real members in `AuthCoreFiles` before writing the login half — use what it declares.

- [ ] **Step 4: Emit it, and delete the five copies**

In `AppScaffolder`, add the file beside the other `Validation/` files:

```csharp
        files.Add((validationFolder, "ActingUser.cs",
            ActingUserTemplate.Source(rootNamespace, login)));
```

It is emitted whether or not `validation` is on, for the same reason `Permissions` is: task 3's handlers call it, and they are emitted regardless.

Then remove each template's `{{currentRole}}` insertion and its local method, and replace every `CurrentRole()` call in emitted code with `ActingUser.Role()`.

- [ ] **Step 5: Run the whole suite and compile the output**

```
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

Expected: PASS. Then go back to task 3's `MasterScreenTemplate` and `ViewModelTemplates` handlers and replace the interim `CurrentRole(), null` with `ActingUser.Role(), ActingUser.Id()`.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/ActingUserTemplate.cs AppScaffolding/AppScaffolder.cs AppScaffolding/Screens AppScaffolding/ViewModelTemplates.cs AppScaffolding/EfStoreTemplate.cs AppScaffolding/JsonStoreTemplate.cs CodeGenerator.Tests/App/ActingUserTests.cs
git commit -m "Emit one ActingUser instead of five copies of the same question"
```

---

### Task 5: Shared test helpers, and the plain grid's missing guard test

Six copies of a mixed-line-ending loop and four `Occurrences`-shaped counters across the test projects, and `ViewModelTemplates.Delete`'s guard is compiled but never executed as a unit test — its twin (`MasterScreenTests.A_plain_user_cannot_delete_a_record`) is.

**Files:**
- Create: `CodeGenerator.Tests/Helper/EmittedLines.cs`
- Modify: the ten test files the two shapes are copied into — find them with
  `grep -rln "Replace(\"\\\\r\\\\n\"\|Occurrences" CodeGenerator.Tests/`
- Test: `CodeGenerator.Tests/App/ViewModelTemplatesTests.cs` (add the guard test)

**Interfaces:**
- Produces: `EmittedLines.Normalized(string source)` → the source with `\r\n` and lone `\r` reduced to `\n`; `EmittedLines.Occurrences(string source, string needle)` → count of non-overlapping ordinal occurrences.

- [ ] **Step 1: Write the failing guard test**

```csharp
[TestMethod]
public void The_plain_grid_refuses_a_delete_the_pipeline_refuses()
{
    // Emit the same screen the other tests in this file emit — copy their argument list rather
    // than inventing one; the template's signature is not what this test is about.
    var source = EmittedPlainGridScreen();
    var normalized = EmittedLines.Normalized(source);

    // Compiled but never executed as a unit test before this. Its twin on the business-layout
    // grid is MasterScreenTests.A_plain_user_cannot_delete_a_record.
    StringAssert.Contains(normalized, "if (!outcome.IsValid)");
    StringAssert.Contains(normalized, "Status = outcome.Messages[0];");
    Assert.AreEqual(1, EmittedLines.Occurrences(normalized, "Records.Remove(item);"));
}
```

- [ ] **Step 2: Run it and watch it fail**

Expected: FAIL — `EmittedLines` does not exist.

- [ ] **Step 3: Write the helper**

```csharp
namespace CodeGenerator.Tests.Helper;

/// <summary>
/// The two shapes every emitted-source test needs. Six copies of the first and four of the
/// second existed before this; they agreed, and nothing held them together.
/// </summary>
internal static class EmittedLines
{
    /// <summary>
    /// <paramref name="source"/> with every line ending reduced to \n.
    /// </summary>
    /// <remarks>
    /// Raw string literals carry their SOURCE FILE's line endings, so a template in a CRLF file
    /// emits CRLF and one in an LF file emits LF — in the same generated app. An assertion about
    /// a multi-line emitted fragment is otherwise a test of which file the template happens to
    /// live in.
    /// </remarks>
    public static string Normalized(string source)
        => source.Replace("\r\n", "\n").Replace("\r", "\n");

    /// <summary>How many times <paramref name="needle"/> occurs in <paramref name="source"/>,
    /// counting non-overlapping ordinal matches.</summary>
    public static int Occurrences(string source, string needle)
    {
        var count = 0;
        var at = source.IndexOf(needle, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = source.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
```

- [ ] **Step 4: Replace the ten copies, then run everything**

Replace each local copy with a call to the helper, deleting the local method. Do NOT change any assertion while doing it — a behaviour change hidden in a cleanup is the thing this task must not do.

```
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

Expected: PASS, with the same test count plus one.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator.Tests/
git commit -m "One copy of each test helper, and the guard test the plain grid never had"
```

---

### Task 6: The Entity stage, and a foreign key a zero no longer skips

Today the FK check is emitted inside `Field` and reads
`if (item.{target}Id != 0 && !context.Lookup.Exists<{target}>(item.{target}Id))` — so a sale line with no product at all is valid. This moves the check to a new `Entity` stage where the spec assigns it, and makes the key REQUIRED for a transaction or a document line while a master's stays optional.

**Files:**
- Modify: `AppScaffolding/ValidatorTemplate.cs` (`Generated`)
- Modify: `AppScaffolding/AppScaffolder.cs:511` (pass the role)
- Test: `CodeGenerator.Tests/App/ValidatorStagesTests.cs`, `CodeGenerator.Tests/App/ValidatorCompilesTests.cs`

**Interfaces:**
- Consumes: `EntityRoles.Classify(classes)` → `EntityRoleMap`, already computed in `AppScaffolder`.
- Produces: `ValidatorTemplate.Generated(string rootNamespace, ClassDefinition definition, IReadOnlyList<string> foreignKeyTargets, bool referencesRequired)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public void A_foreign_key_check_runs_in_the_entity_stage()
{
    var source = ValidatorTemplate.Generated(
        "Acme", LineWithProductId(), ["Product"], referencesRequired: true);

    // The stage list is the contract. A check emitted in Field would report a bad key beside a
    // bad field; the spec stops between stages so that a lookup never runs over a record whose
    // fields have not been judged yet.
    StringAssert.Contains(source, "Security,\n        Field,\n        Entity,");
    StringAssert.Contains(source, "private static void Entity(");
    StringAssert.Contains(source, "context.Lookup.Exists<Product>(item.ProductId)");
}

[TestMethod]
public void A_transaction_line_must_have_its_foreign_key_set()
{
    var source = ValidatorTemplate.Generated(
        "Acme", LineWithProductId(), ["Product"], referencesRequired: true);

    // A sale line with no product is not a valid sale line. The zero escape below is what made
    // it one.
    StringAssert.Contains(source, "if (item.ProductId == 0)");
    Assert.IsFalse(source.Contains("item.ProductId != 0 &&", StringComparison.Ordinal));
}

[TestMethod]
public void A_masters_optional_foreign_key_may_stay_unset()
{
    var source = ValidatorTemplate.Generated(
        "Acme", CustomerWithRegionId(), ["Region"], referencesRequired: false);

    // Optional means optional: a Customer with no Region is a Customer, and only a key that IS
    // set has to resolve.
    StringAssert.Contains(source, "if (item.RegionId != 0 && !context.Lookup.Exists<Region>(item.RegionId))");
    Assert.IsFalse(source.Contains("if (item.RegionId == 0)", StringComparison.Ordinal));
}

[TestMethod]
public void A_model_with_no_foreign_keys_has_no_entity_stage()
{
    var source = ValidatorTemplate.Generated("Acme", PlainCustomer(), [], referencesRequired: false);

    // An empty stage emitting nothing is what keeps a three-entity CRUD app's output no heavier
    // than before this phase.
    Assert.IsFalse(source.Contains("Entity,", StringComparison.Ordinal));
    Assert.IsFalse(source.Contains("private static void Entity(", StringComparison.Ordinal));
}
```

Normalize with `EmittedLines.Normalized` before the stage-list assertion — the `\n` in it is a line ending.

- [ ] **Step 2: Run them and watch them fail**

Expected: FAIL — `Generated` takes three arguments, and the FK block is emitted into `Field`.

- [ ] **Step 3: Emit the stage**

In `Generated`, stop appending the FK blocks to `blocks` (the `Field` body) and build them into a separate list:

```csharp
        var entityChecks = new List<string>();

        foreach (var target in foreignKeyTargets)
        {
            if (referencesRequired)
            {
                entityChecks.Add(
                    $"if (item.{target}Id == 0)");
                entityChecks.Add(
                    $"    errors.Add(new ValidationError(\"{target}Id\", \"{target} is required.\"));");
                entityChecks.Add(
                    $"else if (!context.Lookup.Exists<{target}>(item.{target}Id))");
                entityChecks.Add(
                    $"    errors.Add(new ValidationError(\"{target}Id\", \"{target}Id does not refer to a known {target}.\"));");
            }
            else
            {
                entityChecks.Add(
                    $"if (item.{target}Id != 0 && !context.Lookup.Exists<{target}>(item.{target}Id))");
                entityChecks.Add(
                    $"    errors.Add(new ValidationError(\"{target}Id\", \"{target}Id does not refer to a known {target}.\"));");
            }
        }
```

Then emit the stage only when `entityChecks.Count > 0`, and add `Entity,` to the `Stages` array on the same condition. Build both as interpolated fragments:

```csharp
        var entityStage = entityChecks.Count == 0
            ? string.Empty
            : $$"""


    /// <summary>What cannot be judged from the record in hand: whether the records it names
    /// exist. Runs only after Field has passed, so a lookup never runs over a record whose own
    /// fields were wrong.</summary>
    private static void Entity(ValidationContext<{{definition.Name}}> context, List<ValidationError> errors)
    {
        var item = context.Item;
        if (item is null)
            return;

{{Block(entityChecks)}}
    }
""";

        var stageList = entityChecks.Count == 0
            ? "        Security,\n        Field,"
            : "        Security,\n        Field,\n        Entity,";
```

Substitute `{{stageList}}` into the `Stages` array and `{{entityStage}}` after the `Field` method. A null `item` RETURNS rather than adding an error — `Field` has already reported it, and the pipeline stopped, so this is reachable only in a hand-written test.

- [ ] **Step 4: Pass the role from the scaffolder**

At `AppScaffolder.cs:511`, `roles` is already in scope:

```csharp
            foreach (var definition in classes)
                files.Add((validationFolder, $"{definition.Name}Validator.Generated.cs",
                    ValidatorTemplate.Generated(
                        rootNamespace, definition,
                        ForeignKeys.TargetsOf(definition, classNameSet),
                        referencesRequired: roles.RoleOf(definition.Name)
                            is EntityRole.Transaction or EntityRole.DocumentLine)));
```

Use `EntityRoleMap`'s real accessor — check the type before writing `RoleOf`.

- [ ] **Step 5: Re-reason the stage-order tests**

A record with a bad field AND a bad foreign key now reports only the field. Find every test asserting both at once (`grep -rn "does not refer to a known" CodeGenerator.Tests/`) and change what it expects — this is the behaviour the move is FOR, not a regression to work around. Then:

```
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

Expected: PASS, including `ValidatorCompilesTests`.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/ValidatorTemplate.cs AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/
git commit -m "Give the lookup checks their own stage, and stop a zero meaning no product"
```

---

### Task 7: Uniqueness, and the last widening of `IEntityLookup`

A uniqueness check needs to know whether any OTHER record holds this value — the third and last member of the interface.

**Files:**
- Modify: `AppScaffolding/ValidatorTemplate.cs` (`EntityLookupInterface`, `StoreEntityLookup`, `Generated`, and a new `EntityStageRules`)
- Test: `CodeGenerator.Tests/App/ValidatorStagesTests.cs`, and a runtime test that executes the emitted lookup

**Interfaces:**
- Produces: emitted `bool AnyOtherWith<TEntity>(string propertyName, object? value, int exceptId) where TEntity : class, new()`.
- Produces: `ValidatorTemplate.EntityStageRules` → `IReadOnlyCollection<(string Type, string Rule)>`, and `ValidatorTemplate.CanEmitEntityRule(string propertyType, string ruleName)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public void A_unique_rule_asks_whether_another_record_holds_the_value()
{
    var customer = PlainCustomer();
    customer.Properties.Single(p => p.Name == "Code").Rules.Add(
        new PropertyRule { Name = "unique" });

    var source = ValidatorTemplate.Generated("Acme", customer, [], referencesRequired: false);

    StringAssert.Contains(source, "private static void Entity(");
    StringAssert.Contains(
        source,
        "context.Lookup.AnyOtherWith<Customer>(\"Code\", item.Code, item.Id)");
}

[TestMethod]
public void An_update_does_not_collide_with_itself()
{
    // exceptId is item.Id, so saving a record over itself is not a duplicate of itself. Without
    // it, every second save of an unchanged record would be refused.
    var source = ValidatorTemplate.EntityLookupInterface("Acme");

    StringAssert.Contains(source, "int exceptId");
}

[TestMethod]
public void Unique_is_declared_emittable_for_the_types_it_emits_on()
{
    // The register that stops a rule kind being recorded with no emitter. EmittableRules is the
    // Field-stage register and says nothing about this stage, so a second one is the honest
    // shape rather than a lie in the first.
    Assert.IsTrue(ValidatorTemplate.CanEmitEntityRule("string", "unique"));
    Assert.IsTrue(ValidatorTemplate.CanEmitEntityRule("int", "unique"));
    Assert.IsFalse(ValidatorTemplate.CanEmitEntityRule("bool", "unique"));
}
```

- [ ] **Step 2: Run them and watch them fail**

Expected: FAIL — `AnyOtherWith` and `CanEmitEntityRule` do not exist.

- [ ] **Step 3: Widen the interface and implement it**

In `EntityLookupInterface`:

```csharp
    /// <summary>Whether any record of this type OTHER than <paramref name="exceptId"/> holds
    /// this value for this property.</summary>
    /// <remarks>
    /// The exception is what lets an Update save an unchanged record: without it every record
    /// would be a duplicate of itself the second time it was saved.
    /// </remarks>
    bool AnyOtherWith<TEntity>(string propertyName, object? value, int exceptId)
        where TEntity : class, new();
```

In `StoreEntityLookup`, beside the other two:

```csharp
    /// <inheritdoc />
    public bool AnyOtherWith<TEntity>(string propertyName, object? value, int exceptId)
        where TEntity : class, new()
    {
        var property = typeof(TEntity).GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        // A name that is not there finds nothing rather than throwing, for the reason
        // CountReferencing gives: the name came from the model at generation time, and a rename
        // that leaves the two disagreeing must leave the delete and the save behaving as they
        // did before this phase.
        if (property is null)
            return false;

        foreach (var item in StoreFactory.Create<TEntity>().GetAll())
        {
            if (IdOf(item) == exceptId)
                continue;

            if (Equals(property.GetValue(item), value))
                return true;
        }

        return false;
    }
```

`Equals` compares strings by ordinal value, which is the same comparison the scan counts duplicates with — the two must agree, or a rule proposed as holding would refuse the very rows it was proposed from.

- [ ] **Step 4: Emit the check and the register**

In `Generated`, before the foreign-key blocks, add per-property uniqueness to `entityChecks`:

```csharp
        foreach (var property in definition.Properties)
        {
            if (!property.Rules.Any(r =>
                    string.Equals(r.Name, UniqueRule, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (!CanEmitEntityRule(property.Type, UniqueRule))
                continue;

            var guard = property.Type == "string"
                ? $"if (!string.IsNullOrWhiteSpace(item.{property.Name}) && "
                : "if (";

            entityChecks.Add(
                $"{guard}context.Lookup.AnyOtherWith<{definition.Name}>(\"{property.Name}\", item.{property.Name}, item.Id))");
            entityChecks.Add(
                $"    errors.Add(new ValidationError(\"{property.Name}\", \"Another {definition.Name} already has this {property.Name}.\"));");
        }
```

And the register, beside `EmittableRules`:

```csharp
    private const string UniqueRule = "unique";

    /// <summary>
    /// Every (property type, rule name) pair the ENTITY stage knows how to emit.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="EmittableRules"/>, which is the Field stage's register and is
    /// derived from what <see cref="Checks"/> emits. Adding an entity-stage rule to that list
    /// would claim <c>Checks</c> emits something for it, which it does not — and the guard test
    /// that reads it would then pass over a rule with no emitter at all.
    /// <para>
    /// A bool is excluded deliberately: two records with the same true is not a collision, it is
    /// a boolean.
    /// </para>
    /// </remarks>
    public static IReadOnlyCollection<(string Type, string Rule)> EntityStageRules { get; } =
    [
        ("string", UniqueRule), ("int", UniqueRule),
        ("decimal", UniqueRule), ("DateTime", UniqueRule)
    ];

    /// <summary>Whether the Entity stage emits anything for a <paramref name="ruleName"/> rule on
    /// a property of type <paramref name="propertyType"/>.</summary>
    public static bool CanEmitEntityRule(string propertyType, string ruleName)
        => EntityStageRules.Any(pair =>
            string.Equals(pair.Type, propertyType, StringComparison.Ordinal)
            && string.Equals(pair.Rule, ruleName, StringComparison.OrdinalIgnoreCase));
```

- [ ] **Step 5: Prove the emitted lookup RUNS**

Add to `CodeGenerator.Tests/App/ValidatorCompilesTests.cs` (or a new runtime test file) a test that compiles the emitted `StoreEntityLookup` into an assembly, loads it, seeds two records with the same Code, and asserts `AnyOtherWith` returns true for one and false when the only match is the record itself. Follow the existing pattern for executing emitted code — search for `AssemblyBytes` in the test project to find it.

```
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "Validator"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/ValidatorTemplate.cs CodeGenerator.Tests/App/
git commit -m "Ask whether another record already holds this value"
```

---

### Task 8: Refuse a delete that would orphan records

`CountReferencing` has never been called by anything. Its early return — `if (property is null) return 0;` — is what stops a full scan of a large store, and nothing has ever executed it.

**Files:**
- Modify: `AppScaffolding/ForeignKeys.cs` (add `ReferencesTo`)
- Modify: `AppScaffolding/ValidatorTemplate.cs` (`Generated`)
- Modify: `AppScaffolding/AppScaffolder.cs` (pass the incoming references)
- Test: `CodeGenerator.Tests/App/ValidatorStagesTests.cs`, plus a runtime test for the early return

**Interfaces:**
- Produces: `ForeignKeys.ReferencesTo(string target, IReadOnlyList<ClassDefinition> classes)` → `IReadOnlyList<(string Entity, string Property)>`.
- Produces: `ValidatorTemplate.Generated(rootNamespace, definition, foreignKeyTargets, referencesRequired, referencedBy)` — `referencedBy` is the result of `ReferencesTo` for this definition.

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public void Who_points_at_this_class_is_read_from_the_same_definition_of_a_foreign_key()
{
    var classes = new List<ClassDefinition> { CustomerClass(), InvoiceWithCustomerId() };

    var referencing = ForeignKeys.ReferencesTo("Customer", classes);

    Assert.HasCount(1, referencing);
    Assert.AreEqual("Invoice", referencing[0].Entity);
    Assert.AreEqual("CustomerId", referencing[0].Property);
}

[TestMethod]
public void A_delete_is_refused_with_the_count_in_the_sentence()
{
    var source = ValidatorTemplate.Generated(
        "Acme", CustomerClass(), [], referencesRequired: false,
        referencedBy: [("Invoice", "CustomerId")]);

    StringAssert.Contains(source, "if (context.Op == ValidationOperation.Delete)");
    StringAssert.Contains(
        source, "context.Lookup.CountReferencing<Invoice>(\"CustomerId\", item.Id)");
    // The count is the point: "cannot be deleted" alone leaves the user hunting.
    StringAssert.Contains(source, "cannot be deleted");
    StringAssert.Contains(source, "{referencing} Invoice");
}

[TestMethod]
public void A_class_nothing_points_at_emits_no_delete_check()
{
    var source = ValidatorTemplate.Generated(
        "Acme", CustomerClass(), [], referencesRequired: false, referencedBy: []);

    Assert.IsFalse(source.Contains("CountReferencing", StringComparison.Ordinal));
}
```

- [ ] **Step 2: Run them and watch them fail**

Expected: FAIL — `ReferencesTo` does not exist and `Generated` takes four arguments.

- [ ] **Step 3: Write `ReferencesTo`**

```csharp
    /// <summary>
    /// Every (class, property) that points at <paramref name="target"/> through a foreign key.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="TargetsOf"/>, and deliberately built ON it rather than beside
    /// it: a delete refused for a reference the seeder does not consider a foreign key would
    /// refuse a delete no other part of the app can explain.
    /// </remarks>
    public static IReadOnlyList<(string Entity, string Property)> ReferencesTo(
        string target, IReadOnlyList<ClassDefinition> classes)
    {
        var names = ClassNameSet(classes);
        var referencing = new List<(string Entity, string Property)>();

        foreach (var c in classes)
            foreach (var t in TargetsOf(c, names))
                if (string.Equals(t, target, StringComparison.OrdinalIgnoreCase))
                    referencing.Add((c.Name, t + "Id"));

        return referencing;
    }
```

- [ ] **Step 4: Emit the delete check**

In `Generated`, after the uniqueness and foreign-key checks, append to `entityChecks`:

```csharp
        if (referencedBy.Count > 0)
        {
            entityChecks.Add("if (context.Op == ValidationOperation.Delete)");
            entityChecks.Add("{");

            foreach (var (entity, property) in referencedBy)
            {
                entityChecks.Add(
                    $"    var referencing{entity} = context.Lookup.CountReferencing<{entity}>(\"{property}\", item.Id);");
                entityChecks.Add($"    if (referencing{entity} > 0)");
                entityChecks.Add(
                    $"        errors.Add(new ValidationError(null, $\"This {definition.Name} cannot be deleted — {{referencing{entity}}} {entity} record(s) refer to it.\"));");
            }

            entityChecks.Add("}");
        }
```

The `$"..."` inside an already-interpolated raw string needs its braces doubled where they are literal — check the emitted text in the test, not only that it compiles.

Wire the argument at `AppScaffolder.cs:511`:

```csharp
                        referencedBy: ForeignKeys.ReferencesTo(definition.Name, classes)));
```

- [ ] **Step 5: Test the early return at RUNTIME**

The Entity stage is `CountReferencing`'s first caller, so this task owns the test nothing has ever run:

```csharp
[TestMethod]
public void Counting_a_property_that_is_not_there_scans_nothing()
{
    // The early return is what stops a full scan of a large store for a property a rename left
    // behind. Asserted by ORDER, not only by the zero: a zero is also what a full scan of an
    // empty store returns.
    var lookup = CompileAndConstructStoreEntityLookup(out var storeReads);

    var count = lookup.CountReferencing<Invoice>("NoSuchId", 1);

    Assert.AreEqual(0, count);
    Assert.AreEqual(0, storeReads, "the store was read for a property that is not there");
}
```

`storeReads` counts calls into the emitted `StoreFactory`. If the existing runtime-test harness cannot count them, assert instead that `GetAll` was never reached by pointing the store at a directory that would throw if read — either proves the order; a bare `Assert.AreEqual(0, count)` does not.

- [ ] **Step 6: Run everything**

```
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

- [ ] **Step 7: Commit**

```bash
git add AppScaffolding/ForeignKeys.cs AppScaffolding/ValidatorTemplate.cs AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/
git commit -m "Refuse a delete that would orphan records, and say how many"
```

---

### Task 9: `ColumnScan` — one pass, every count

**Files:**
- Create: `DataIngestion/Schema/ColumnScan.cs`
- Test: `DataIngestion.Tests/ColumnScanTests.cs` (new)

**Interfaces:**
- Produces:

```csharp
public sealed record ColumnScanResult(
    IReadOnlyList<string> DistinctValues,
    bool ExceededDistinctCap,
    int LongestValueLength,
    int DuplicateValueCount,
    int NonBlankRowCount,
    int RowCount);

public static class ColumnScan
{
    public const int DistinctCap = 51;
    public static ColumnScanResult Of(TabularTable table, string column);
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public void A_column_of_repeating_values_reports_them_all()
{
    var table = TableOf("Status", "Draft", "Posted", "Draft", "Cancelled", "Posted");

    var scan = ColumnScan.Of(table, "Status");

    CollectionAssert.AreEquivalent(
        new[] { "Draft", "Posted", "Cancelled" }, scan.DistinctValues.ToArray());
    Assert.IsFalse(scan.ExceededDistinctCap);
    Assert.AreEqual(9, scan.LongestValueLength);      // "Cancelled"
    Assert.AreEqual(2, scan.DuplicateValueCount);     // Draft and Posted each occur twice
    Assert.AreEqual(5, scan.NonBlankRowCount);
}

[TestMethod]
public void The_distinct_list_stops_at_the_cap()
{
    var values = Enumerable.Range(1, 200).Select(i => $"v{i}").ToArray();
    var table = TableOf("Reference", values);

    var scan = ColumnScan.Of(table, "Reference");

    // 51 is how "more than 50" is answered without holding a million strings for a column that
    // was never going to be a status.
    Assert.HasCount(ColumnScan.DistinctCap, scan.DistinctValues);
    Assert.IsTrue(scan.ExceededDistinctCap);
}

[TestMethod]
public void A_repeat_past_the_cap_is_still_counted()
{
    var values = Enumerable.Range(1, 200).Select(i => $"v{i}").ToList();
    values.Add("v60");

    var scan = ColumnScan.Of(TableOf("Reference", [.. values]), "Reference");

    // A column of a million references is exactly where uniqueness is most wanted, and it is
    // always past the cap. Counting repeats only among the first 51 values would propose
    // uniqueness for a column whose 60th value is duplicated — a rule the generated app then
    // refuses the source's own rows against. So the repeat count is tracked over EVERY value,
    // and only the distinct LIST is capped.
    Assert.IsTrue(scan.ExceededDistinctCap);
    Assert.AreEqual(1, scan.DuplicateValueCount);
}

[TestMethod]
public void A_fifth_value_at_row_fifty_thousand_is_still_found()
{
    var values = new List<string>();
    for (var i = 0; i < 50_000; i++)
        values.Add(i % 2 == 0 ? "Draft" : "Posted");
    values.Add("Reversed");

    var scan = ColumnScan.Of(TableOf("Status", [.. values]), "Status");

    // The whole reason the scan reads the complete table. A 200-value sample, or the 1000-row
    // ImportedData cap, would have produced a whitelist that rejects the source's own data.
    CollectionAssert.Contains(scan.DistinctValues.ToArray(), "Reversed");
    Assert.AreEqual(50_001, scan.NonBlankRowCount);
}

[TestMethod]
public void Blank_cells_count_as_rows_but_not_as_values()
{
    var table = TableOf("Status", "Draft", null, "   ", "Posted");

    var scan = ColumnScan.Of(table, "Status");

    // Whether a column may be blank is the Field stage's question, answered from the declared
    // nullability. A blank is not a status value.
    Assert.HasCount(2, scan.DistinctValues);
    Assert.AreEqual(2, scan.NonBlankRowCount);
    Assert.AreEqual(4, scan.RowCount);
}

[TestMethod]
public void A_column_that_is_not_there_scans_nothing()
{
    var scan = ColumnScan.Of(TableOf("Status", "Draft"), "NoSuchColumn");

    Assert.IsEmpty(scan.DistinctValues);
    Assert.AreEqual(0, scan.RowCount);
}

private static TabularTable TableOf(string column, params string?[] values)
{
    var table = new TabularTable { Name = "T" };
    table.Columns.Add(new TabularColumn { Name = column });

    foreach (var value in values)
        table.Rows.Add([value]);

    return table;
}
```

- [ ] **Step 2: Run them and watch them fail**

```
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "ColumnScan"
```

Expected: FAIL — `ColumnScan` does not exist.

- [ ] **Step 3: Write the scanner**

```csharp
namespace DataIngestion.Schema;

/// <summary>What one pass over one column found.</summary>
/// <param name="DistinctValues">Up to <see cref="ColumnScan.DistinctCap"/> distinct non-blank
/// values, in the order first seen.</param>
/// <param name="ExceededDistinctCap">True when the cap was reached, which is how "more than 50"
/// is known without keeping every value of a million-row column.</param>
/// <param name="DuplicateValueCount">How many distinct values occur more than once. Zero is what
/// a uniqueness proposal needs. Counted over EVERY value, not only the capped list: a column of a
/// million references is always past the cap, and that is exactly where uniqueness matters.
/// </param>
public sealed record ColumnScanResult(
    IReadOnlyList<string> DistinctValues,
    bool ExceededDistinctCap,
    int LongestValueLength,
    int DuplicateValueCount,
    int NonBlankRowCount,
    int RowCount);

/// <summary>
/// One pass per column over the COMPLETE table, producing every count the rule proposals need.
/// </summary>
/// <remarks>
/// Runs over <see cref="TabularTable"/> and never over <c>ImportedData</c>, which
/// <c>TabularImport</c> caps at <c>ImportedData.MaxSeedRows</c>. A rule inferred from the first
/// thousand rows is a rule inferred from a sample, and the app it generates would reject data its
/// own source contains — the single failure this whole layer exists to prevent.
/// <para>
/// One pass for every output rather than one pass per candidate: the readers have already
/// materialised every row, and walking them four times to answer four questions costs four times
/// as much for nothing.
/// </para>
/// </remarks>
public static class ColumnScan
{
    /// <summary>One more than the largest distinct count that can be a status, so reaching it is
    /// the answer "more than 50".</summary>
    public const int DistinctCap = 51;

    /// <summary>What <paramref name="column"/> of <paramref name="table"/> holds.</summary>
    public static ColumnScanResult Of(TabularTable table, string column)
    {
        var index = table.IndexOf(column);

        if (index < 0)
            return new ColumnScanResult([], false, 0, 0, 0, 0);

        // Ordinal, because the emitted uniqueness check compares with Equals — which is ordinal
        // for strings. A scan that folded case would propose a rule the generated app then
        // refuses the source's own rows against.
        //
        // Two structures, deliberately: `order` is the capped list a status proposal reads, and
        // `seen` is every value, uncapped, so a repeat past the cap still counts. A column of a
        // million references is always past the cap, and it is the one uniqueness matters most
        // for. `seen` costs the memory of the column's distinct values, which the reader has
        // already paid for the rows themselves.
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();

        var exceeded = false;
        var longest = 0;
        var nonBlank = 0;

        foreach (var row in table.Rows)
        {
            var value = index < row.Length ? row[index] : null;

            if (string.IsNullOrWhiteSpace(value))
                continue;

            nonBlank++;

            if (value.Length > longest)
                longest = value.Length;

            if (seen.TryGetValue(value, out var already))
            {
                seen[value] = already + 1;
                continue;
            }

            seen[value] = 1;

            // Past the cap only the LIST stops growing. Every other count carries on, which is
            // what makes a uniqueness proposal possible for a column that is not a status.
            if (order.Count >= DistinctCap)
                exceeded = true;
            else
                order.Add(value);
        }

        var duplicates = 0;
        foreach (var count in seen.Values)
            if (count > 1)
                duplicates++;

        return new ColumnScanResult(
            order, exceeded, longest, duplicates, nonBlank, table.Rows.Count);
    }
}
```

- [ ] **Step 4: Run the tests**

Expected: PASS, all six.

- [ ] **Step 5: Commit**

```bash
git add DataIngestion/Schema/ColumnScan.cs DataIngestion.Tests/ColumnScanTests.cs
git commit -m "Count what every row of a column holds, in one pass"
```

---

### Task 10: `RuleProposal`, and attaching proposals where a rename cannot orphan them

**Files:**
- Create: `CodeGenerationEngine/Models/RuleProposal.cs`
- Create: `DataIngestion/RuleProposals.cs`
- Modify: `DomainDiscovery/Model/CandidateDomainModel.cs` (`CandidateField.Proposals`)
- Modify: `DataIngestion/DataModelBuilder.cs` (attach after relationships are known)
- Test: `DataIngestion.Tests/RuleProposalsTests.cs` (new), `DataIngestion.Tests/DataModelBuilderTests.cs`

**Interfaces:**
- Produces:

```csharp
public enum RuleProposalKind { Status, Unique, Pattern }

public sealed class RuleProposal
{
    public required RuleProposalKind Kind { get; init; }
    public required PropertyRule Rule { get; init; }
    public required string Evidence { get; init; }
    public bool Accepted { get; set; }
}
```

- Produces: `RuleProposals.For(ColumnScanResult scan, string fieldName, string propertyType, bool mayBeUnique)` → `IReadOnlyList<RuleProposal>`.
- Produces: `CandidateField.Proposals` → `List<RuleProposal>`, get-only, empty by default.

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public void A_small_set_of_short_values_is_proposed_as_a_status()
{
    var scan = new ColumnScanResult(["Draft", "Posted", "Cancelled"], false, 9, 3, 12_431, 12_431);

    var proposals = RuleProposals.For(scan, "Status", "string", mayBeUnique: true);

    var status = proposals.Single(p => p.Kind == RuleProposalKind.Status);
    Assert.AreEqual("allowedvalues", status.Rule.Name);
    CollectionAssert.AreEquivalent(
        new[] { "Draft", "Posted", "Cancelled" }, status.Rule.AllowedValues.ToArray());
    StringAssert.Contains(status.Evidence, "12,431");
    Assert.IsFalse(status.Accepted, "a proposal arrives unaccepted");
}

[TestMethod]
public void A_long_valued_column_is_not_a_status()
{
    // The length bound is what separates a status from a low-cardinality description or a notes
    // column that happens to repeat.
    var scan = new ColumnScanResult(["a short one", new string('x', 31)], false, 31, 2, 40, 40);

    var proposals = RuleProposals.For(scan, "Notes", "string", mayBeUnique: true);

    Assert.IsFalse(proposals.Any(p => p.Kind == RuleProposalKind.Status));
}

[TestMethod]
public void More_than_fifty_values_is_not_a_status()
{
    var scan = new ColumnScanResult(
        [.. Enumerable.Range(1, 51).Select(i => $"v{i}")], true, 4, 0, 9_000, 9_000);

    var proposals = RuleProposals.For(scan, "Reference", "string", mayBeUnique: true);

    Assert.IsFalse(proposals.Any(p => p.Kind == RuleProposalKind.Status));
}

[TestMethod]
public void A_column_with_no_repeats_is_proposed_as_unique()
{
    var scan = new ColumnScanResult(["A1", "A2"], true, 2, 0, 12_431, 12_431);

    var unique = RuleProposals.For(scan, "Code", "string", mayBeUnique: true)
        .Single(p => p.Kind == RuleProposalKind.Unique);

    Assert.AreEqual("unique", unique.Rule.Name);
    StringAssert.Contains(unique.Evidence, "12,431");
    Assert.IsFalse(unique.Accepted);
}

[TestMethod]
public void A_column_with_a_repeat_is_not_proposed_as_unique()
{
    var scan = new ColumnScanResult(["A1", "A1"], false, 2, 1, 2, 2);

    Assert.IsFalse(
        RuleProposals.For(scan, "Code", "string", mayBeUnique: true)
            .Any(p => p.Kind == RuleProposalKind.Unique));
}

[TestMethod]
public void Uniqueness_is_not_proposed_where_it_may_not_be()
{
    var scan = new ColumnScanResult(["A1", "A2"], true, 2, 0, 4, 4);

    // A transaction line's product code repeats by design. Proposing uniqueness on a small young
    // table would propose a rule forbidding a second sale of the same product.
    Assert.IsFalse(
        RuleProposals.For(scan, "ProductCode", "string", mayBeUnique: false)
            .Any(p => p.Kind == RuleProposalKind.Unique));
}

[TestMethod]
public void A_column_whose_every_value_is_an_email_is_proposed_as_one()
{
    var scan = new ColumnScanResult(["a@b.com", "c@d.co.uk"], false, 9, 0, 2, 2);

    var pattern = RuleProposals.For(scan, "Contact", "string", mayBeUnique: true)
        .Single(p => p.Kind == RuleProposalKind.Pattern);

    Assert.AreEqual("pattern", pattern.Rule.Name);
    Assert.AreEqual("email address", pattern.Rule.CustomRule);
}

[TestMethod]
public void One_value_that_does_not_match_stops_the_pattern()
{
    var scan = new ColumnScanResult(["a@b.com", "not an email"], false, 12, 0, 2, 2);

    Assert.IsFalse(
        RuleProposals.For(scan, "Contact", "string", mayBeUnique: true)
            .Any(p => p.Kind == RuleProposalKind.Pattern));
}

[TestMethod]
public void A_column_past_the_distinct_cap_can_still_be_unique()
{
    var scan = new ColumnScanResult(["A1"], true, 2, 0, 1_000_000, 1_000_000);

    // A million distinct references is the case uniqueness matters most for, and it is always
    // past the cap. It is NOT offered a status or a pattern — neither can be judged from the 51
    // values that were kept — but zero repeats is a complete answer.
    var proposals = RuleProposals.For(scan, "Code", "string", mayBeUnique: true);

    Assert.IsTrue(proposals.Any(p => p.Kind == RuleProposalKind.Unique));
    Assert.IsFalse(proposals.Any(p => p.Kind == RuleProposalKind.Status));
    Assert.IsFalse(proposals.Any(p => p.Kind == RuleProposalKind.Pattern));
}

[TestMethod]
public void A_table_with_no_rows_is_offered_nothing()
{
    // The shape a discovered domain arrives in: columns but no rows. Wikipedia and Ollama
    // produce no tables at all and never reach this code, but a table read from an empty CSV
    // does — and a rule proposed from zero rows would hold vacuously in all of them.
    var table = new TabularTable { Name = "Book" };
    table.Columns.Add(new TabularColumn { Name = "Title" });

    var model = DataModelBuilder.Build([table]);

    Assert.IsTrue(model.Entities.SelectMany(e => e.Fields).All(f => f.Proposals.Count == 0));
}
```

Every test here constructs a `ColumnScanResult` directly, so they exercise the thresholds and nothing else. Task 9 already guarantees the counts they are given are the ones a real table produces.

- [ ] **Step 2: Run them and watch them fail**

Expected: FAIL — `RuleProposal` does not exist.

- [ ] **Step 3: Write `RuleProposal`**

```csharp
namespace CodeGenerationEngine.Models;

/// <summary>What a proposal would become.</summary>
public enum RuleProposalKind
{
    /// <summary>A small fixed set of values, which becomes an allowed-values rule.</summary>
    Status,

    /// <summary>No value repeats.</summary>
    Unique,

    /// <summary>Every value matches a format the rule catalog already knows.</summary>
    Pattern
}

/// <summary>
/// A rule the data supports, offered to the user and enforced only if they take it.
/// </summary>
/// <remarks>
/// Separate from <see cref="PropertyRule"/>, which is a rule the app WILL enforce. A rule the
/// source DECLARED — a NOT NULL column, an nvarchar(50) — lands as a PropertyRule silently,
/// because it is a fact the source stated. A rule inferred from the CONTENTS of rows is a guess
/// about the future however many rows support it, so it arrives here instead, unaccepted, and
/// reaches a generated app only once someone has picked it.
/// </remarks>
public sealed class RuleProposal
{
    public required RuleProposalKind Kind { get; init; }

    /// <summary>The rule this becomes when accepted, ready to be added to a property's Rules.
    /// </summary>
    public required PropertyRule Rule { get; init; }

    /// <summary>What makes this arguable rather than mysterious: "holds in all 12,431 rows".
    /// </summary>
    public required string Evidence { get; init; }

    /// <summary>False until a user picks it. Settable for the same reason
    /// <see cref="PropertyRule"/>'s members are: the review surfaces set it in place.</summary>
    public bool Accepted { get; set; }
}
```

- [ ] **Step 4: Write `RuleProposals`**

```csharp
using CodeGenerationEngine.Models;
using DataIngestion.Schema;

namespace DataIngestion;

/// <summary>
/// Turns one column's scan into the rules that hold in every row of it.
/// </summary>
/// <remarks>
/// Every threshold here is a counter-example test: a rule is proposed only when the number of
/// rows breaking it is ZERO. Nothing is proposed from a sample, and nothing proposed here is
/// enforced until a user takes it.
/// </remarks>
public static class RuleProposals
{
    /// <summary>The largest distinct count that can be a status.</summary>
    private const int MaxStatusValues = 50;

    /// <summary>The longest a status value may be, in characters.</summary>
    /// <remarks>What separates a status from a low-cardinality description, or a notes column
    /// whose few entries happen to repeat.</remarks>
    private const int MaxStatusValueLength = 30;

    /// <summary>The formats the rule catalog already has, and the name each is shown by.</summary>
    /// <remarks>
    /// ONLY these. A regex derived from 12,000 references is a regex that rejects the 12,001st,
    /// and it is exactly the kind of rule a user cannot judge from a card: "matches
    /// ^[A-Z]{3}-\d{4}$, holds in all 12,431 rows" reads like evidence and is a trap. These six
    /// are deliberately loose where the real-world format is, which is what makes them safe to
    /// offer.
    /// </remarks>
    private static readonly (string Name, string Pattern)[] KnownFormats =
    [
        ("email address", @"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$"),
        ("phone number", @"^\+?[0-9 ().\-]{7,20}$"),
        ("web address", @"^(?:https?://)?[^\s.]+(?:\.[^\s.]+)+$"),
        ("postcode", @"^[A-Za-z]{1,2}[0-9][0-9A-Za-z]?\s*[0-9][A-Za-z]{2}$"),
        ("GUID", @"^\{[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\}$|^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$|^[0-9a-fA-F]{32}$"),
        ("IP address", @"^(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$")
    ];

    /// <summary>
    /// Every rule <paramref name="scan"/> supports for a property called
    /// <paramref name="fieldName"/> of type <paramref name="propertyType"/>.
    /// </summary>
    /// <param name="mayBeUnique">
    /// Whether a uniqueness proposal is allowed at all. False for an entity that references
    /// another — a transaction line's product code repeats by design, and a young table where it
    /// happens not to have yet would otherwise propose a rule forbidding a second sale of the
    /// same product.
    /// </param>
    public static IReadOnlyList<RuleProposal> For(
        ColumnScanResult scan, string fieldName, string propertyType, bool mayBeUnique)
    {
        var proposals = new List<RuleProposal>();

        if (scan.NonBlankRowCount == 0)
            return proposals;

        var rows = scan.NonBlankRowCount.ToString("N0");

        if (propertyType == "string"
            && !scan.ExceededDistinctCap
            && scan.DistinctValues.Count is > 0 and <= MaxStatusValues
            && scan.LongestValueLength <= MaxStatusValueLength)
        {
            proposals.Add(new RuleProposal
            {
                Kind = RuleProposalKind.Status,
                Rule = new PropertyRule
                {
                    PropertyName = fieldName,
                    Name = "allowedvalues",
                    AllowedValues = [.. scan.DistinctValues]
                },
                Evidence =
                    $"{fieldName} holds only {string.Join(", ", scan.DistinctValues)} "
                    + $"in all {rows} rows"
            });
        }

        if (mayBeUnique && scan.DuplicateValueCount == 0 && scan.NonBlankRowCount > 1)
        {
            proposals.Add(new RuleProposal
            {
                Kind = RuleProposalKind.Unique,
                Rule = new PropertyRule
                {
                    PropertyName = fieldName,
                    Name = "unique",
                    IsUnique = true
                },
                Evidence = $"no two of the {rows} rows share a {fieldName}"
            });
        }

        if (propertyType == "string" && MatchedFormat(scan) is { } format)
        {
            proposals.Add(new RuleProposal
            {
                Kind = RuleProposalKind.Pattern,
                Rule = new PropertyRule
                {
                    PropertyName = fieldName,
                    Name = "pattern",
                    RegexPattern = format.Pattern,
                    CustomRule = format.Name
                },
                Evidence = $"every one of the {rows} values is a valid {format.Name}"
            });
        }

        return proposals;
    }

    /// <summary>The first known format every scanned value matches, or null.</summary>
    /// <remarks>
    /// Only the values the scan KEPT can be checked, so a column past the distinct cap is not
    /// offered a pattern: a format that holds in the first 51 of a million values is not a format
    /// that holds in the million.
    /// </remarks>
    private static (string Name, string Pattern)? MatchedFormat(ColumnScanResult scan)
    {
        if (scan.ExceededDistinctCap || scan.DistinctValues.Count == 0)
            return null;

        foreach (var format in KnownFormats)
        {
            var regex = new System.Text.RegularExpressions.Regex(format.Pattern);

            if (scan.DistinctValues.All(v => regex.IsMatch(v)))
                return format;
        }

        return null;
    }
}
```

Copy the five non-email patterns verbatim from `NamedFormatRuleParser.Patterns` rather than retyping them; the email one from `EmailRuleParser`. A divergence here means the app refuses values the catalog's own rule accepts.

- [ ] **Step 5: Attach proposals in the builder**

Add to `CandidateField`:

```csharp
    /// <summary>
    /// Rules the scan found hold in every row, offered to the user unaccepted. On this field
    /// rather than in a table keyed by name, for the reason <see cref="Facts"/> is: the review
    /// loop lets a property be renamed, and a side table would then describe a field that is no
    /// longer there.
    /// </summary>
    public List<CodeGenerationEngine.Models.RuleProposal> Proposals { get; } = new();
```

In `DataModelBuilder.Build`, keep the table each entity came from, and add proposals AFTER relationships are known:

```csharp
        var sources = new Dictionary<string, TabularTable>(StringComparer.OrdinalIgnoreCase);
        // ... inside the entity loop, after model.Entities.Add(entity):
        sources[entityName] = table;

        // ... after AddRelationships(model, entityNames):
        AddProposals(model, sources);
```

```csharp
    /// <summary>
    /// The rules each entity's own rows support, attached to the fields they are about.
    /// </summary>
    /// <remarks>
    /// After the relationship passes, because whether uniqueness may be proposed at all depends
    /// on them: an entity that references another is a transaction or a child, where a repeating
    /// value is the design rather than a defect. That is the first half of the same test
    /// <c>EntityRoles.IsTransaction</c> applies later over class definitions — stricter than it,
    /// deliberately, since the cost of not offering a rule is that the user types it themselves
    /// and the cost of offering a wrong one is an app that refuses good data.
    /// <para>
    /// Runs over the COMPLETE tables. Everything downstream of <c>TabularImport</c> is capped at
    /// <c>ImportedData.MaxSeedRows</c>, and a rule inferred from a sample is the one thing this
    /// layer must never produce.
    /// </para>
    /// </remarks>
    private static void AddProposals(
        CandidateDomainModel model, IReadOnlyDictionary<string, TabularTable> sources)
    {
        foreach (var entity in model.Entities)
        {
            if (!sources.TryGetValue(entity.Name, out var table))
                continue;

            var mayBeUnique = entity.Relationships.Count == 0;

            foreach (var field in entity.Fields)
            {
                var column = ColumnFor(table, field.Name);

                if (column is null)
                    continue;

                var scan = ColumnScan.Of(table, column);

                foreach (var proposal in RuleProposals.For(
                             scan, field.Name, field.Type, mayBeUnique))
                    field.Proposals.Add(proposal);
            }
        }
    }
```

`ColumnFor` maps a field name back to its source column — a column may have been renamed off its class name by `ResolveColumns`. Reuse whatever `ResolveColumns` records rather than guessing; if it does not keep the pair, have it return the source column alongside the field name and store it on the outcome.

- [ ] **Step 6: Prove the cap is not the source**

```csharp
[TestMethod]
public void Proposals_are_scanned_from_the_whole_table_not_the_seed_sample()
{
    var table = new TabularTable { Name = "Orders" };
    table.Columns.Add(new TabularColumn { Name = "Status" });

    for (var i = 0; i < ImportedData.MaxSeedRows + 500; i++)
        table.Rows.Add([i < ImportedData.MaxSeedRows ? "Draft" : "Reversed"]);

    var model = DataModelBuilder.Build([table]);

    var status = model.Entities.Single().Fields.Single(f => f.Name == "Status");
    var proposal = status.Proposals.Single(p => p.Kind == RuleProposalKind.Status);

    // Every value past MaxSeedRows is Reversed. A scan over ImportedData would have proposed a
    // whitelist of one, and the generated app would then reject 500 rows of its own source.
    CollectionAssert.Contains(proposal.Rule.AllowedValues.ToArray(), "Reversed");
}
```

- [ ] **Step 7: Run and commit**

```
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "RuleProposals|ColumnScan|DataModelBuilder"
```

```bash
git add CodeGenerationEngine/Models/RuleProposal.cs DataIngestion/ DomainDiscovery/Model/CandidateDomainModel.cs DataIngestion.Tests/
git commit -m "Propose only the rules that hold in every row, and leave them unaccepted"
```

---

### Task 11: An accepted proposal becomes a rule, and the rule is emitted

**Files:**
- Modify: `CodeGenerationEngine/Models/PropertyDefinition.cs` (`Proposals`)
- Modify: `AppScaffolding/DomainModelMapper.cs` (carry them, then apply the accepted ones)
- Modify: `AppScaffolding/ValidatorTemplate.cs` (`Checks` emits `allowedvalues` and `pattern`; `EmittableRules` gains their pairs)
- Test: `CodeGenerator.Tests/App/ValidatorEmitsEveryRuleKindTests.cs`, `CodeGenerator.Tests/App/DomainModelMapperTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public void An_accepted_proposal_becomes_a_rule()
{
    var model = ModelWithProposal(accepted: true);

    var classes = DomainModelMapper.ToClasses(model, out _);

    var status = classes.Single().Properties.Single(p => p.Name == "Status");
    var rule = status.Rules.Single(r => r.Name == "allowedvalues");
    CollectionAssert.AreEquivalent(new[] { "Draft", "Posted" }, rule.AllowedValues.ToArray());
}

[TestMethod]
public void An_unaccepted_proposal_becomes_nothing()
{
    var model = ModelWithProposal(accepted: false);

    var classes = DomainModelMapper.ToClasses(model, out _);

    // The decision of this phase: nothing inferred from the CONTENTS of data reaches a generated
    // app unless someone picked it. A scripted build gets none of it.
    Assert.IsFalse(
        classes.Single().Properties.Single(p => p.Name == "Status").Rules
            .Any(r => r.Name == "allowedvalues"));
}

[TestMethod]
public void Every_new_rule_kind_is_declared_emittable_for_its_type()
{
    // The register that stops a rule being recorded with no emitter. Its absence is silent: the
    // emitter guards every check on the property's TYPE, so a rule on the wrong type produces
    // nothing at all and the validator reports success over a check that is not there.
    Assert.IsTrue(ValidatorTemplate.CanEmit("string", "allowedvalues"));
    Assert.IsTrue(ValidatorTemplate.CanEmit("string", "pattern"));
}

[TestMethod]
public void An_allowed_values_rule_emits_a_check_that_names_the_values()
{
    var customer = PlainCustomer();
    customer.Properties.Single(p => p.Name == "Status").Rules.Add(new PropertyRule
    {
        Name = "allowedvalues",
        AllowedValues = ["Draft", "Posted"]
    });

    var source = ValidatorTemplate.Generated("Acme", customer, [], false, []);

    StringAssert.Contains(source, "\"Draft\"");
    StringAssert.Contains(source, "\"Posted\"");
    StringAssert.Contains(source, "must be one of");
}

[TestMethod]
public void A_value_with_a_quote_in_it_does_not_break_the_emitted_source()
{
    var customer = PlainCustomer();
    customer.Properties.Single(p => p.Name == "Status").Rules.Add(new PropertyRule
    {
        Name = "allowedvalues",
        AllowedValues = ["He said \"go\"", "back\\slash"]
    });

    var source = ValidatorTemplate.Generated("Acme", customer, [], false, []);

    // A status value comes from the user's own data and can hold anything. An unescaped quote is
    // a generated app that does not compile.
    var compiled = Compiler.CompileSources([("Validator.cs", source), .. SupportingSources()]);
    Assert.IsTrue(compiled.Success, string.Join("\n", compiled.Diagnostics));
}
```

- [ ] **Step 2: Run them and watch them fail**

Expected: FAIL — `PropertyDefinition` has no `Proposals`, `CanEmit` returns false for both names.

- [ ] **Step 3: Carry the proposals across**

On `PropertyDefinition`:

```csharp
    /// <summary>Rules the data supports, carried from the candidate field so the accepted ones
    /// can be applied where every other rule is recorded.</summary>
    public List<RuleProposal> Proposals { get; set; } = new();
```

In `DomainModelMapper`'s candidate loop:

```csharp
            foreach (var field in entity.Fields)
                definition.Properties.Add(new PropertyDefinition
                {
                    Name = field.Name,
                    Type = field.Type,
                    Facts = field.Facts,
                    Proposals = [.. field.Proposals]
                });
```

And in `InferValidation`, before the type switch:

```csharp
        // Accepted proposals first, so a rule the user picked is recorded whatever the declared
        // type then adds. They are separate registers: a maxlength from an nvarchar(50) and an
        // allowed-values from a scan are both true of the same column.
        foreach (var proposal in property.Proposals)
        {
            if (!proposal.Accepted)
                continue;

            property.Rules.Add(proposal.Rule);
        }
```

- [ ] **Step 4: Emit the two new rule kinds**

In `Checks`, inside the `property.Type == "string"` arm, after the max-length block:

```csharp
            if (Rule(property, AllowedValuesRule)?.AllowedValues is { Count: > 0 } allowed)
            {
                var literals = string.Join(", ", allowed.Select(Quoted));
                var listed = string.Join(", ", allowed);

                checks.Add($"if (!string.IsNullOrWhiteSpace(item.{name})");
                checks.Add($"    && System.Array.IndexOf(new[] {{ {literals} }}, item.{name}) < 0)");
                checks.Add($"    errors.Add(new ValidationError(\"{name}\", {Quoted($"{name} must be one of: {listed}.")}));");
            }

            if (Rule(property, PatternRule) is { RegexPattern.Length: > 0 } format)
            {
                checks.Add($"if (!string.IsNullOrWhiteSpace(item.{name})");
                checks.Add($"    && !System.Text.RegularExpressions.Regex.IsMatch(item.{name}!, {Quoted(format.RegexPattern!)}))");
                checks.Add($"    errors.Add(new ValidationError(\"{name}\", {Quoted($"{name} is not a valid {format.CustomRule ?? "value"}.")}));");
            }
```

with the escaper beside `Literal`:

```csharp
    /// <summary>
    /// <paramref name="value"/> as a C# string literal.
    /// </summary>
    /// <remarks>
    /// These values come from the user's own rows and can hold anything — a quote, a backslash, a
    /// newline. An unescaped one is a generated app that does not compile, and the tool would
    /// have reported success writing it.
    /// </remarks>
    private static string Quoted(string value)
        => "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
        + "\"";
```

Add the constants and the register entries:

```csharp
    private const string AllowedValuesRule = "allowedvalues";
    private const string PatternRule = "pattern";
```

```csharp
        ("string", RequiredRule), ("string", MaxLengthRule),
        ("string", AllowedValuesRule), ("string", PatternRule),
```

- [ ] **Step 5: Run the whole suite**

```
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

Expected: PASS, including `ValidatorEmitsEveryRuleKindTests`, which walks `EmittableRules` and would now fail if either new pair had no emitter.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/Models/PropertyDefinition.cs AppScaffolding/ CodeGenerator.Tests/App/
git commit -m "Record the rules the user picked, and emit the checks they became"
```

---

### Task 12: The console offers the proposals, and `approve` takes none of them

`PrintModel` is commented out in its entirety, so the console review loop prints nothing about the model — there is no existing per-field printout to hang proposals under. A block reprinted after every action is the noise `PrintModel` was silenced for, so this is a one-line summary plus two verbs.

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs` (the loop's summary line and the switch)
- Modify: `CodeGenerator/App/ReviewActions.cs`, `CodeGenerator/App/ReviewHints.cs`
- Create: `CodeGenerator/App/ProposalReport.cs`
- Test: `CodeGenerator.Tests/App/ProposalReportTests.cs` (new)

**Interfaces:**
- Produces: `ProposalReport.Summary(CandidateDomainModel)` → `string?` (null when there are none).
- Produces: `ProposalReport.Lines(CandidateDomainModel)` → `IReadOnlyList<string>`, numbered from 1.
- Produces: `ProposalReport.Accept(CandidateDomainModel, string argument)` → `string` (what to print).

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public void A_model_with_no_proposals_says_nothing()
{
    Assert.IsNull(ProposalReport.Summary(ModelWithNoProposals()));
}

[TestMethod]
public void The_summary_says_how_many_and_how_to_see_them()
{
    var line = ProposalReport.Summary(ModelWithTwoProposals());

    StringAssert.Contains(line!, "2");
    StringAssert.Contains(line!, "rules");
}

[TestMethod]
public void Each_proposal_is_numbered_with_its_evidence()
{
    var lines = ProposalReport.Lines(ModelWithTwoProposals());

    StringAssert.Contains(lines[0], "1.");
    StringAssert.Contains(lines[0], "Order.Status");
    StringAssert.Contains(lines[0], "holds");
    StringAssert.Contains(lines[0], "[ ]");        // unticked
}

[TestMethod]
public void Accepting_one_ticks_only_that_one()
{
    var model = ModelWithTwoProposals();

    ProposalReport.Accept(model, "1");

    var all = EveryProposal(model).ToList();
    Assert.IsTrue(all[0].Accepted);
    Assert.IsFalse(all[1].Accepted);
}

[TestMethod]
public void Accepting_all_ticks_all()
{
    var model = ModelWithTwoProposals();

    ProposalReport.Accept(model, "all");

    Assert.IsTrue(EveryProposal(model).All(p => p.Accepted));
}

[TestMethod]
public void A_number_that_is_not_there_is_reported_not_thrown()
{
    var model = ModelWithTwoProposals();

    var message = ProposalReport.Accept(model, "9");

    StringAssert.Contains(message, "9");
    Assert.IsFalse(EveryProposal(model).Any(p => p.Accepted));
}

[TestMethod]
public void Approving_without_accepting_takes_no_proposal()
{
    // The decision of this phase, pinned by a test so convenience cannot reverse it: a scripted
    // build, and anyone who just types approve, gets none of it.
    var model = ModelWithTwoProposals();

    var classes = DomainModelMapper.ToClasses(model, out _);

    Assert.IsFalse(
        classes.SelectMany(c => c.Properties).SelectMany(p => p.Rules)
            .Any(r => r.Name is "allowedvalues" or "unique" or "pattern"));
}
```

- [ ] **Step 2: Run them and watch them fail**

Expected: FAIL — `ProposalReport` does not exist.

- [ ] **Step 3: Write `ProposalReport`**

Numbering is by a stable walk — entities in order, fields in order, proposals in order — so `accept 2` means the same thing as long as the model has not been edited. State that in the remarks, and renumber on every call rather than caching.

```csharp
namespace CodeGenerator.App;

/// <summary>
/// The review loop's view of the rules the scan proposed.
/// </summary>
/// <remarks>
/// A summary line rather than a block under every field: PrintModel's whole per-entity printout
/// was commented out because it was reprinted after every action and scrolled the loop away, and
/// a proposals block would arrive the same way. One line says there is something to look at; the
/// `rules` verb shows it.
/// <para>
/// Pure and tested, for the reason ReviewActions is: the loop reads the console and cannot be
/// driven from a test, so everything decidable is lifted out of it.
/// </para>
/// </remarks>
internal static class ProposalReport
{
    /// <summary>The one line under the model, or null when the data proposed nothing.</summary>
    public static string? Summary(CandidateDomainModel model)
    {
        var count = All(model).Count;

        if (count == 0)
            return null;

        var accepted = All(model).Count(p => p.Proposal.Accepted);

        return $"  {count} rule(s) proposed from your data, {accepted} accepted — "
             + "type `rules` to see them.";
    }

    /// <summary>Every proposal, numbered from 1, with its evidence and whether it is taken.
    /// </summary>
    public static IReadOnlyList<string> Lines(CandidateDomainModel model)
    {
        var lines = new List<string>();
        var all = All(model);

        if (all.Count == 0)
            return ["  Nothing was proposed from your data."];

        lines.Add("  Rules your data supports. None is applied until you accept it.");

        for (var i = 0; i < all.Count; i++)
        {
            var (entity, field, proposal) = all[i];

            lines.Add(
                $"  {i + 1}. [{(proposal.Accepted ? "x" : " ")}] {entity}.{field} — "
                + $"{proposal.Evidence}");
        }

        lines.Add("  accept <n>   accept all");

        return lines;
    }

    /// <summary>Accepts one proposal by number, or every one, and reports what happened.
    /// </summary>
    public static string Accept(CandidateDomainModel model, string argument)
    {
        var all = All(model);

        if (string.Equals(argument, "all", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var entry in all)
                entry.Proposal.Accepted = true;

            return $"Accepted {all.Count} rule(s).";
        }

        if (!int.TryParse(argument, out var number) || number < 1 || number > all.Count)
            return $"There is no proposal {argument}. Type `rules` to see them.";

        var picked = all[number - 1];
        picked.Proposal.Accepted = true;

        return $"Accepted: {picked.Entity}.{picked.Field}.";
    }

    private static List<(string Entity, string Field, RuleProposal Proposal)> All(
        CandidateDomainModel model)
    {
        var all = new List<(string, string, RuleProposal)>();

        foreach (var entity in model.Entities)
            foreach (var field in entity.Fields)
                foreach (var proposal in field.Proposals)
                    all.Add((entity.Name, field.Name, proposal));

        return all;
    }
}
```

- [ ] **Step 4: Wire it into the loop**

In `RunReviewLoop`, after the customization lines:

```csharp
            if (ProposalReport.Summary(model) is { } proposals)
                Console.WriteLine(proposals);
```

Two cases in the switch:

```csharp
                case "rules":
                    foreach (var line in ProposalReport.Lines(model))
                        Console.WriteLine(line);
                    break;

                case "accept" when parts.Length >= 2:
                    Console.WriteLine(ProposalReport.Accept(model, parts[1]));
                    break;
```

Add to `ReviewHints.Full`:

```csharp
        "  rules                       the rules your data supports",
        "  accept <n>|all              apply a proposed rule",
```

And to `ReviewActions.All`, so the picker path reaches them too:

```csharp
        new("rules",  "rules     — the rules your data supports", []),
        new("accept", "accept    — apply a proposed rule", ["Number, or 'all'"]),
```

`ReviewHints.Short` is NOT changed — it is the line a user reads more than any other in the tool and it already wraps at its current length.

- [ ] **Step 5: Run and commit**

```
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "ProposalReport|ReviewActions|ReviewHints"
```

```bash
git add CodeGenerator/App/ CodeGenerator.Tests/App/
git commit -m "Offer the proposed rules in the review loop, and take none without being asked"
```

---

### Task 13: A checkbox per proposal on the review page

**Files:**
- Modify: `CodeGenerator/Web/Components/Stages/ReviewStage.razor`
- Test: `CodeGenerator.Tests/Web/ReviewStageProposalTests.cs` (new; follow the bUnit setup in the existing `ReviewStage` tests)

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public void A_proposal_is_offered_unticked_with_its_evidence()
{
    var page = Render(SessionWithProposal());

    var box = page.Find("input.proposal");

    Assert.IsFalse(box.HasAttribute("checked"));
    StringAssert.Contains(page.Markup, "holds in all 12,431 rows");
}

[TestMethod]
public void Ticking_a_proposal_accepts_it()
{
    var session = SessionWithProposal();
    var page = Render(session);

    page.Find("input.proposal").Change(true);

    Assert.IsTrue(ProposalOf(session).Accepted);
}

[TestMethod]
public void A_field_with_no_proposal_shows_no_checkbox()
{
    var page = Render(SessionWithNoProposals());

    Assert.HasCount(0, page.FindAll("input.proposal"));
}

[TestMethod]
public void Ticking_a_proposal_saves_the_session()
{
    // A closed tab loses unsaved work, and a decision is work. Report() is where every other
    // edit on this page passes through for the same reason.
    var session = SessionWithProposal();
    var saves = CountSaves(session);

    Render(session).Find("input.proposal").Change(true);

    Assert.IsTrue(CountSaves(session) > saves);
}
```

Use `.Change(true)` on the checkbox. The known trap is on `input type=text`: `.Change()` dispatches the event without putting text in the DOM, which is why a refused rename leaves stale text no bUnit test can see. A checkbox has no such gap — but do not assert on the rendered `checked` attribute after the change; assert on the model.

- [ ] **Step 2: Run them and watch them fail**

Expected: FAIL — no `input.proposal` in the markup.

- [ ] **Step 3: Add the markup**

Inside the field `<li>`, after the remove button:

```razor
                            @foreach (var proposal in field.Proposals)
                            {
                                @* An offer, not a restatement of what the field already is —
                                   which is why it can be shown here without contradicting the
                                   note above about not second-guessing the editor. Unticked:
                                   nothing inferred from the contents of rows reaches a generated
                                   app unless someone picks it. *@
                                <label class="proposal-row">
                                    <input type="checkbox" class="proposal"
                                           checked="@proposal.Accepted"
                                           @onchange="e => Decide(proposal, e.Value)" />
                                    <span class="proposal-evidence">@proposal.Evidence</span>
                                </label>
                            }
```

and the handler in `@code`:

```csharp
    /// <summary>Takes or drops one proposed rule.</summary>
    /// <remarks>
    /// Saves through the session for the reason <see cref="Report"/> does: a decision is work,
    /// and a closed tab loses work that was never saved.
    /// </remarks>
    private void Decide(RuleProposal proposal, object? value)
    {
        proposal.Accepted = value is true || (value is string text && bool.TryParse(text, out var on) && on);
        Session.ModelEdited();
    }
```

Add `.proposal-row` and `.proposal-evidence` to the page's stylesheet. **Do not rename any existing CSS class** — 27 of them are asserted by bUnit tests.

- [ ] **Step 4: Run the web tests**

```
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "ReviewStage"
```

Expected: PASS, with no existing review-page test regressing.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/Web/ CodeGenerator.Tests/Web/
git commit -m "Put the proposed rules on the review card, unticked"
```

---

### Task 14: A resumed build keeps its facts and its decisions

`BuildSnapshot.Field` persists `Name`, `Type` and `Provenance` and **not `Facts`** — so a resumed build's fields come back factless and `InferValidation` records nothing from them. That is a pre-existing defect (a resumed build generates a weaker app than the same build unresumed), and it sits directly under this phase: proposals ride on the same carrier.

**Files:**
- Modify: `CodeGenerator/App/Resume/BuildSnapshot.cs` (`Field`, `ToDto`, `FromDto`, `CurrentSchemaVersion`)
- Test: `CodeGenerator.Tests/App/BuildSnapshotTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public void A_resumed_field_keeps_what_its_column_declared()
{
    var model = ModelWithFacts(maxLength: 50, nullable: false);

    var restored = RoundTrip(model);

    var facts = restored.Entities.Single().Fields.Single().Facts;
    Assert.IsNotNull(facts, "the facts every inferred field rule is derived from");
    Assert.AreEqual(50, facts.MaxLength);
    Assert.AreEqual(false, facts.IsNullable);
}

[TestMethod]
public void A_resumed_build_infers_the_same_rules_as_an_unresumed_one()
{
    var model = ModelWithFacts(maxLength: 50, nullable: false);

    var before = DomainModelMapper.ToClasses(model, out _);
    var after = DomainModelMapper.ToClasses(RoundTrip(model), out _);

    CollectionAssert.AreEquivalent(
        RuleNames(before).ToArray(), RuleNames(after).ToArray());
}

[TestMethod]
public void A_resumed_build_keeps_the_proposals_that_were_accepted()
{
    var model = ModelWithProposals(acceptFirst: true);

    var restored = RoundTrip(model);

    var proposals = restored.Entities.Single().Fields.Single().Proposals;
    Assert.HasCount(2, proposals);
    Assert.IsTrue(proposals[0].Accepted);
    Assert.IsFalse(proposals[1].Accepted);
}

[TestMethod]
public void The_rows_are_still_not_written_to_disk()
{
    // Unchanged, and asserted rather than intended: the file stays small whatever the data size,
    // and the connection string is never written to .codegen at all.
    var json = Serialize(SnapshotOf(ModelWithProposals(acceptFirst: true)));

    Assert.IsFalse(json.Contains("Rows", StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 2: Run them and watch them fail**

Expected: FAIL — `Facts` and `Proposals` are absent from the DTO, so `RoundTrip` returns them null/empty.

- [ ] **Step 3: Widen the DTO**

```csharp
    internal sealed class Field
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Provenance { get; set; }

        /// <summary>
        /// What the source declared about this field's column.
        /// </summary>
        /// <remarks>
        /// Absent before schema version 2, which meant a resumed build came back factless and
        /// inferred none of the field rules the same build inferred without a resume. The rows
        /// are re-read on resume; the FACTS are not, because nothing re-runs the reader's
        /// schema pass once a model has been restored.
        /// </remarks>
        public Facts? Facts { get; set; }

        /// <summary>
        /// The proposed rules and, the point of keeping them, which were accepted.
        /// </summary>
        /// <remarks>
        /// The scan re-runs from the re-read rows, so the proposals themselves could be
        /// recomputed — the DECISION could not, and that is the user's work.
        /// </remarks>
        public List<Proposal>? Proposals { get; set; }
    }

    internal sealed class Facts
    {
        public bool? IsNullable { get; set; }
        public int? MaxLength { get; set; }
        public bool? IsUnbounded { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public string? DeclaredType { get; set; }
    }

    internal sealed class Proposal
    {
        public string? Kind { get; set; }
        public string? Evidence { get; set; }
        public bool? Accepted { get; set; }
        public string? RuleName { get; set; }
        public List<string>? AllowedValues { get; set; }
        public string? RegexPattern { get; set; }
        public string? CustomRule { get; set; }
    }
```

Map both ways in `ToDto`/`FromDto`. A proposal whose `Kind` does not parse is DROPPED, not restored as a default kind — restoring the wrong kind would tick a rule the user never saw. Bump `CurrentSchemaVersion` to 2; the loader already refuses a file from a NEWER version and fills missing fields from defaults, so an older file still loads, just without facts.

Check `TypeFacts`'s real members before writing the `Facts` DTO — copy them, do not guess.

- [ ] **Step 4: Run and commit**

```
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "BuildSnapshot|Resume"
```

```bash
git add CodeGenerator/App/Resume/BuildSnapshot.cs CodeGenerator.Tests/App/BuildSnapshotTests.cs
git commit -m "Keep the facts and the decisions a resumed build was losing"
```

---

### Task 15: Run a real generated app

Four of five defects in the entry-screens round were invisible to 4,000+ passing tests and were found only by running the apps. The same was true of the Ollama round and the SQL round. This task is the budget for that, and it is not optional.

**Files:**
- No production files unless a defect is found — then the fix goes in with its own regression test.
- Create: `docs/superpowers/plans/2026-09-11-validation-phase-3-run-notes.md` (what was run, what was seen)

- [ ] **Step 1: Generate from SQL**

This machine has a running SQL Server. Pick or create a small database with a master holding a status column and a short code, plus a transaction pointing at it. Then:

```
build app "orders" from sql
```

Work through the review loop: `rules` to see the proposals, `accept` one status and one uniqueness proposal, leave the rest, then `approve wpf`.

Generated apps go to `D:\SampleProjects\<AppName>`, not the working directory. Probe paths must be short — the scratchpad blows MAX_PATH, and MSB3030 "file not found" on a file that exists is the tell.

- [ ] **Step 2: Read the emitted validator before running anything**

Open `Validation/<Master>Validator.Generated.cs`. Check: the stage list has `Entity`, the allowed-values check names the values that were on the card, the uniqueness check names the property that was ticked, and NOTHING names a proposal that was left unticked.

- [ ] **Step 3: Run it and try to break it**

Build and run the app. Then, in the running app:

1. Enter a status that is not one of the accepted values. **Expected:** refused, naming the allowed values.
2. Enter a code a second record already has. **Expected:** refused, naming the property.
3. Save the same record twice unchanged. **Expected:** accepted both times — this is what `exceptId` is for.
4. Delete a master a transaction refers to. **Expected:** refused, with the count in the sentence, ON THE SCREEN — this is what task 1's status line and task 3's pipeline call exist for.
5. Delete a master nothing refers to. **Expected:** deleted.
6. Create a transaction line with no master selected. **Expected:** refused — the zero no longer skips the check.

- [ ] **Step 4: Run the same six against a second target**

Repeat with `approve blazor`. The enforcement is shared but the surfacing is not, and the Blazor delete handler is a different emitted method from the WPF one.

- [ ] **Step 5: Generate from a file source and from a discovered domain**

```
build app "shop" from csv D:\SampleProjects\data\shop.csv
build app: a library with books and members
```

**Expected:** the CSV run proposes rules and constrains nothing until they are accepted; the discovered run has no tables, proposes nothing, and still generates and runs. A crash or an empty app on the discovered path is a defect in this phase, not a limit of it.

- [ ] **Step 6: Write down what happened, and fix what broke**

Record every step's actual outcome in the run-notes file, including the ones that worked. For each defect: a failing test first, then the fix, then the note.

- [ ] **Step 7: Commit**

```bash
git add docs/superpowers/plans/2026-09-11-validation-phase-3-run-notes.md
git commit -m "Record what running the generated apps actually showed"
```

---

## Finishing

- [ ] Full CI-shaped verification:
  ```
  dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
  dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
  ```
- [ ] `gh pr create` against `main`, with the PR body listing the two spec corrections this plan makes (task 7's `EntityStageRules`, task 10's uniqueness gate) and linking #131.
- [ ] Read the CI verdict with `gh run view --json conclusion` — `gh run watch | tail` reports exit 0 even when CI failed.

## Spec corrections this plan makes

Both were found by reading the code the spec describes, and both belong in the spec file when the branch lands:

1. **`EmittableRules` is the Field stage's register only.** The spec says every new proposal kind needs a pairing there. Uniqueness is emitted by the Entity stage, which `Checks` knows nothing about, so adding `unique` to that list would claim an emitter that does not exist — and the guard test reading it would then pass over a rule with no emitter at all. Task 7 adds a second register, `EntityStageRules`, with the same discipline.

2. **"Masters only" is decided from relationships, not from `EntityRoles`.** `EntityRoles.Classify` runs over `ClassDefinition`s in AppScaffolding, long after the scan. At `DataModelBuilder` time the available signal is `entity.Relationships.Count == 0` — the first half of the same transaction test, applied stricter. A child entity that is genuinely a master is then not offered uniqueness; the user can still state it with `add validation`. The reverse error — offering uniqueness on a transaction line whose codes have not repeated yet — would generate an app that refuses a second sale of the same product.

3. **`BuildSnapshot` never persisted `Facts`.** Discovered while planning task 14. A resumed build inferred none of the field rules the same build inferred without a resume. Pre-existing, fixed here because proposals ride on the same carrier and would have been lost the same way.
