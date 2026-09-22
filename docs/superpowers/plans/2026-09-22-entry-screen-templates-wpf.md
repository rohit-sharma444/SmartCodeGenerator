# Entry Screen Templates (Phase 1: axis + WPF) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a six-way `EntryTemplate` choice (Classic, Grouped, TwoColumn, Tabbed, Split, Ribbon) to `build app`, and implement all six for the WPF business layout's master and transaction screens, while leaving Windows Forms and Blazor's rendering unchanged (they follow in later phases).

**Architecture:** A new `EntryTemplate` enum + `EntryTemplateCatalog`, wired through the exact same mechanism `AppLayout`/`AppLayoutCatalog` already use (nullable field on `AppCustomization`, wizard/menu/inline/web/help wiring). The choice is resolved at generation time: `WpfEntryControls`/`WpfBusinessLayout` read `context.Customization.EffectiveEntryTemplate` and emit one of six pre-written source branches — nothing is chosen at the generated app's own run time. A new `EntryTemplates.Supports(targetKey)` gate restricts the prompt to `"wpf"` in this phase, so a Windows Forms or Blazor build can never be asked for (or silently receive) a template its renderer does not implement yet.

**Tech Stack:** C# / .NET, MSTest, Roslyn compile-testing (`EmittedDesktopSource.AssertCompilesTogether`), WPF (emitted XAML + code-behind as string templates).

**Spec:** `docs/superpowers/specs/2026-09-22-entry-screen-templates-design.md`

## Global Constraints

- A build that sets no entry template must golden-match today's WPF business-layout output byte for byte (`EffectiveEntryTemplate` defaults to `Grouped`, which is today's rendering).
- The entry-template prompt/menu/inline option is only reachable when `EntryTemplates.Supports(target.Key)` is true (this phase: `"wpf"` only) **and** the effective window layout resolves to `business`.
- Every emitted C# file must be Roslyn-compile-tested (`EmittedDesktopSource.AssertCompilesTogether`), not substring-asserted alone.
- No entity name may appear in any of `WpfEntryControls`' emitted controls or in `WpfBusinessLayout`'s emitted XAML (existing invariant — `No_entity_name_appears_in_any_control`, `No_entity_name_appears_in_the_window`).
- Windows Forms and Blazor are untouched by this plan: `DesktopViewModelFiles.Build`'s new parameter defaults to `false`, and `WinFormsTarget.cs` is not modified.

---

## Task 1: `EntryTemplate` enum, `EntryTemplateCatalog`, and the target-support gate

**Files:**
- Create: `AppScaffolding/Screens/EntryTemplate.cs`
- Create: `AppScaffolding/Screens/EntryTemplateCatalog.cs`
- Create: `AppScaffolding/Screens/EntryTemplates.cs`
- Test: `CodeGenerator.Tests/App/EntryTemplateCatalogTests.cs`

**Interfaces:**
- Produces: `enum EntryTemplate { Classic, Grouped, Tabbed, TwoColumn, Split, Ribbon }`; `static class EntryTemplateCatalog` with `Keys`, `Descriptions`, `PromptOptions`, `HelpOptions`, `KeyFor(EntryTemplate)`, `TryResolve(string, out EntryTemplate)`; `static class EntryTemplates` with `Supports(string targetKey)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// CodeGenerator.Tests/App/EntryTemplateCatalogTests.cs
using AppScaffolding.Screens;

namespace CodeGenerator.Tests.App;

[TestClass]
public class EntryTemplateCatalogTests
{
    [TestMethod]
    public void Keys_ListEveryTemplateWithClassicFirst()
    {
        Assert.AreEqual("classic", EntryTemplateCatalog.Keys[0]);
        Assert.HasCount(Enum.GetValues<EntryTemplate>().Length, EntryTemplateCatalog.Keys);
    }

    [TestMethod]
    public void KeyFor_HyphenatesTwoColumn()
    {
        // One token, never two words: InlineCustomization scans the line right to left by
        // whitespace, the same reason "master-detail" is hyphenated.
        Assert.AreEqual("two-column", EntryTemplateCatalog.KeyFor(EntryTemplate.TwoColumn));
        Assert.DoesNotContain(" ", EntryTemplateCatalog.KeyFor(EntryTemplate.TwoColumn));
    }

    [TestMethod]
    public void TryResolve_ReadsEveryKeyCaseInsensitively()
    {
        foreach (var template in Enum.GetValues<EntryTemplate>())
        {
            Assert.IsTrue(EntryTemplateCatalog.TryResolve(
                EntryTemplateCatalog.KeyFor(template).ToUpperInvariant(), out var parsed));
            Assert.AreEqual(template, parsed);
        }
    }

    [TestMethod]
    public void TryResolve_RefusesAnythingElse()
    {
        Assert.IsFalse(EntryTemplateCatalog.TryResolve("kanban", out _));
        Assert.IsFalse(EntryTemplateCatalog.TryResolve("", out _));
        Assert.IsFalse(EntryTemplateCatalog.TryResolve("two column", out _));
    }

    [TestMethod]
    public void EveryTemplateHasADescription()
    {
        foreach (var template in Enum.GetValues<EntryTemplate>())
            Assert.IsNotEmpty(EntryTemplateCatalog.Descriptions[template]);
    }

    [TestMethod]
    public void OptionRenderings_ListTheKeysInOrder()
    {
        Assert.AreEqual("classic/grouped/tabbed/two-column/split/ribbon", EntryTemplateCatalog.PromptOptions);
        Assert.AreEqual("classic|grouped|tabbed|two-column|split|ribbon", EntryTemplateCatalog.HelpOptions);
    }

    [TestMethod]
    public void Supports_IsTrueForWpfOnlyInThisPhase()
    {
        Assert.IsTrue(EntryTemplates.Supports("wpf"));
        Assert.IsTrue(EntryTemplates.Supports("WPF"));
        Assert.IsFalse(EntryTemplates.Supports("winforms"));
        Assert.IsFalse(EntryTemplates.Supports("blazor"));
        Assert.IsFalse(EntryTemplates.Supports("console"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~EntryTemplateCatalogTests`
Expected: FAIL to compile — `EntryTemplate`, `EntryTemplateCatalog`, `EntryTemplates` do not exist yet.

- [ ] **Step 3: Write the enum**

```csharp
// AppScaffolding/Screens/EntryTemplate.cs
namespace AppScaffolding.Screens;

/// <summary>
/// How a Business-layout entry form arranges a record's fields.
/// </summary>
/// <remarks>
/// <see cref="Grouped"/> is what this generator emitted before this choice existed, and every
/// caller that resolves <see cref="AppCustomization.EffectiveEntryTemplate"/> treats it as the
/// value that must reproduce that output byte for byte.
/// </remarks>
public enum EntryTemplate
{
    /// <summary>One flat column of fields, no grouping.</summary>
    Classic,

    /// <summary>Fields grouped into titled panels, packed three columns wide.</summary>
    Grouped,

    /// <summary>Fields grouped into titled panels, one panel per tab.</summary>
    Tabbed,

    /// <summary>Fields grouped into titled panels, packed two columns wide.</summary>
    TwoColumn,

    /// <summary>The record list and its editor shown together, the way the master screen already
    /// shows a master record — applied to transaction screens too.</summary>
    Split,

    /// <summary>The action bar as an icon command strip instead of plain buttons.</summary>
    Ribbon
}
```

- [ ] **Step 4: Write the catalog**

```csharp
// AppScaffolding/Screens/EntryTemplateCatalog.cs
namespace AppScaffolding.Screens;

/// <summary>
/// The entry-template vocabulary: the CLI keys and their descriptions, in display order.
/// </summary>
/// <remarks>
/// One catalogue, the same shape as <see cref="AppLayoutCatalog"/>: the inline parser, the wizard
/// prompt and the help catalogue all read from here and no surface keeps its own list.
/// </remarks>
public static class EntryTemplateCatalog
{
    private static readonly (EntryTemplate Template, string Key, string Description)[] All =
    [
        (EntryTemplate.Classic, "classic", "one flat column of fields, no grouping"),
        (EntryTemplate.Grouped, "grouped", "fields grouped into titled panels, packed three columns wide"),
        (EntryTemplate.Tabbed, "tabbed", "fields grouped into titled panels, one panel per tab"),
        // Hyphenated deliberately: the inline parser tokenizes on whitespace.
        (EntryTemplate.TwoColumn, "two-column", "fields grouped into titled panels, packed two columns wide"),
        (EntryTemplate.Split, "split",
            "the record list and its editor shown together, as the master screen already does"),
        (EntryTemplate.Ribbon, "ribbon", "the action bar as an icon command strip instead of plain buttons"),
    ];

    /// <summary>Template keys in display order. Classic is first because it is the simplest shape,
    /// not because it is the default: <see cref="AppCustomization.EffectiveEntryTemplate"/> answers
    /// "grouped" for a build that names nothing.</summary>
    public static IReadOnlyList<string> Keys { get; } = All.Select(x => x.Key).ToList();

    public static IReadOnlyDictionary<EntryTemplate, string> Descriptions { get; } =
        All.ToDictionary(x => x.Template, x => x.Description);

    /// <summary>The prompt/help rendering: "classic/grouped/tabbed/two-column/split/ribbon".</summary>
    public static string PromptOptions { get; } = string.Join("/", Keys);

    /// <summary>The pipe rendering the help catalogue uses.</summary>
    public static string HelpOptions { get; } = string.Join("|", Keys);

    public static string KeyFor(EntryTemplate template) => All.First(x => x.Template == template).Key;

    /// <summary>Resolves a key (case-insensitively) to a template.</summary>
    public static bool TryResolve(string key, out EntryTemplate template)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                template = candidate.Template;
                return true;
            }
        }

        template = EntryTemplate.Grouped;
        return false;
    }
}
```

- [ ] **Step 5: Write the target-support gate**

```csharp
// AppScaffolding/Screens/EntryTemplates.cs
namespace AppScaffolding.Screens;

/// <summary>
/// Which targets the entry-template choice applies to.
/// </summary>
/// <remarks>
/// Phase 1 ships WPF's six templates only. Windows Forms and Blazor keep today's single rendering
/// until their own phases land, and this is the one place that decides whether the prompt, menu,
/// inline option and web dropdown are even reachable for a given target — so a build for a target
/// this phase does not support can never set an entry template its renderer does not implement.
/// </remarks>
public static class EntryTemplates
{
    private static readonly string[] SupportedTargetKeys = ["wpf"];

    public static bool Supports(string targetKey)
        => SupportedTargetKeys.Contains(targetKey, StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~EntryTemplateCatalogTests`
Expected: PASS (6 tests)

- [ ] **Step 7: Commit**

```bash
git add AppScaffolding/Screens/EntryTemplate.cs AppScaffolding/Screens/EntryTemplateCatalog.cs AppScaffolding/Screens/EntryTemplates.cs CodeGenerator.Tests/App/EntryTemplateCatalogTests.cs
git commit -m "feat(scaffold): the entry-template vocabulary and its target gate"
```

---

## Task 2: `AppCustomization.EntryTemplate` and `EffectiveEntryTemplate`

**Files:**
- Modify: `AppScaffolding/AppCustomization.cs:35-71`
- Test: `CodeGenerator.Tests/App/AppCustomizationTests.cs`

**Interfaces:**
- Consumes: `AppScaffolding.Screens.EntryTemplate` (Task 1).
- Produces: `AppCustomization.EntryTemplate` (settable via `with`), `AppCustomization.EffectiveEntryTemplate` (`Screens.EntryTemplate`, defaults to `Grouped`).

- [ ] **Step 1: Write the failing test**

Append to `CodeGenerator.Tests/App/AppCustomizationTests.cs`:

```csharp
    [TestMethod]
    public void The_default_entry_template_is_grouped()
        => Assert.AreEqual(AppScaffolding.Screens.EntryTemplate.Grouped, AppCustomization.Default.EffectiveEntryTemplate);

    [TestMethod]
    public void An_explicit_entry_template_is_effective()
    {
        var customization = AppCustomization.Default with
        {
            EntryTemplate = AppScaffolding.Screens.EntryTemplate.Tabbed,
        };

        Assert.AreEqual(AppScaffolding.Screens.EntryTemplate.Tabbed, customization.EffectiveEntryTemplate);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~AppCustomizationTests`
Expected: FAIL to compile — `AppCustomization` has no `EntryTemplate` member.

- [ ] **Step 3: Add the field and the effective property**

In `AppScaffolding/AppCustomization.cs`, change the record declaration (lines 35-48):

```csharp
public sealed record AppCustomization(
    string Company,
    AppTheme Theme,
    string? Accent,
    string TitlePattern,
    string? IconPath,
    bool Validation,
    bool Logging,
    bool SampleSeedData,
    bool Login,
    string Transactions,
    string Masters,
    string NamespacePrefix,
    AppLayout? Layout,
    Screens.EntryTemplate? EntryTemplate)
```

Change `Default` (lines 58-71) to add the new field:

```csharp
    public static AppCustomization Default { get; } = new(
        Company: string.Empty,
        Theme: AppTheme.Default,
        Accent: null,
        TitlePattern: AppPlaceholder,
        IconPath: null,
        Validation: true,
        Logging: false,
        SampleSeedData: true,
        Login: true,
        Transactions: string.Empty,
        Masters: string.Empty,
        NamespacePrefix: string.Empty,
        Layout: null,
        EntryTemplate: null);
```

Add, immediately after `EffectiveLayout` (after line 87):

```csharp
    /// <summary>
    /// The entry-screen template to emit: whatever was named, or <see cref="Screens.EntryTemplate.Grouped"/>
    /// when nothing was.
    /// </summary>
    /// <remarks>
    /// Nullable for the identical reason <see cref="Layout"/> is: "not mentioned" and "grouped" are
    /// different answers to <c>BuildAppCommand.Merge</c>, and the emit path never cares about that
    /// distinction and uses this instead. Grouped, not Classic, because Grouped is what the Business
    /// layout's entry screens already emit — a build naming no entry template must golden-match
    /// today's output.
    /// </remarks>
    public Screens.EntryTemplate EffectiveEntryTemplate => EntryTemplate ?? Screens.EntryTemplate.Grouped;
```

- [ ] **Step 4: Fix every other positional construction of `AppCustomization`**

Run: `dotnet build` and read the errors — every `new AppCustomization(...)` positional call site (there should be none outside `AppCustomization.cs` itself; all other code uses `AppCustomization.Default with { ... }`) needs the new argument, or add it with a default. If any positional call site is found, add `entryTemplate: null` (or the appropriate value) as its 14th argument.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~AppCustomizationTests`
Expected: PASS. Also run `dotnet build` for the whole solution to confirm no other file broke.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/AppCustomization.cs CodeGenerator.Tests/App/AppCustomizationTests.cs
git commit -m "feat(scaffold): AppCustomization carries an entry-template choice"
```

---

## Task 3: Console wizard prompt

**Files:**
- Modify: `CodeGenerator/App/CustomizationWizard.cs:1-186`
- Test: `CodeGenerator.Tests/App/CustomizationWizardTests.cs`

**Interfaces:**
- Consumes: `EntryTemplateCatalog`, `EntryTemplates.Supports`, `AppCustomization.EntryTemplate`/`EffectiveEntryTemplate` (Tasks 1-2).
- Produces: `CustomizationWizard.Run(...)` now also asks and sets `EntryTemplate`.

- [ ] **Step 1: Write the failing tests**

Append to `CodeGenerator.Tests/App/CustomizationWizardTests.cs`:

```csharp
    [TestMethod]
    public void The_entry_template_is_asked_only_when_business_is_effective()
    {
        // sidebar is not business: no entry-template prompt, same input works unchanged.
        var (result, output) = RunFor(new WpfTarget(), "y\nsidebar\n\n\n" + new string('\n', 12));

        Assert.IsNotNull(result);
        Assert.DoesNotContain("Entry template", output);
    }

    [TestMethod]
    public void The_entry_template_is_asked_when_business_is_kept_as_the_default()
    {
        var (result, output) = RunFor(new WpfTarget(), "y\n\n\n\n" + new string('\n', 12));

        Assert.IsNotNull(result);
        StringAssert.Contains(output, "Entry template: classic/grouped/tabbed/two-column/split/ribbon");
        Assert.IsNull(result.EntryTemplate);
    }

    [TestMethod]
    public void An_explicit_entry_template_answer_is_recorded()
    {
        var (result, _) = RunFor(new WpfTarget(), "y\n\ntabbed\n" + new string('\n', 12));

        Assert.IsNotNull(result);
        Assert.AreEqual(AppScaffolding.Screens.EntryTemplate.Tabbed, result.EntryTemplate);
    }

    [TestMethod]
    public void An_unusable_entry_template_answer_keeps_what_was_offered()
    {
        var start = AppCustomization.Default with { EntryTemplate = AppScaffolding.Screens.EntryTemplate.Split };

        var (result, _) = RunFor(new WpfTarget(), "y\n\nkanban\n" + new string('\n', 12), start);

        Assert.IsNotNull(result);
        Assert.AreEqual(AppScaffolding.Screens.EntryTemplate.Split, result.EntryTemplate);
    }
```

Update the existing caret-alignment test (it now asks a fourteenth question when a WPF run keeps the default business layout):

```csharp
    [TestMethod]
    public void Every_prompt_puts_its_caret_in_the_same_column()
    {
        var (_, output) = RunFor(new WpfTarget(), "y\n\n\n" + new string('\n', 14),
            AppCustomization.Default with { Company = "Codis Ltd", Theme = AppTheme.Dark });

        var block = output[(output.IndexOf("clears an optional one.", StringComparison.Ordinal) + 1)..];

        var carets = System.Text.RegularExpressions.Regex.Matches(block, "> ")
            .Select(match => match.Index)
            .ToList();

        Assert.HasCount(14, carets, "a desktop run at the business default asks fourteen questions");

        var widths = carets.Zip(carets.Skip(1), (a, b) => b - a).Distinct().ToList();

        Assert.HasCount(1, widths,
            "every prompt should be the same width so the carets line up, found widths: "
            + string.Join(", ", widths));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~CustomizationWizardTests`
Expected: The four new tests FAIL (no "Entry template" text is written, `result.EntryTemplate` is always null); `Every_prompt_puts_its_caret_in_the_same_column` FAILS (still 13 carets).

- [ ] **Step 3: Add the prompt**

In `CodeGenerator/App/CustomizationWizard.cs`, add `using AppScaffolding.Screens;` beside the existing `using AppScaffolding.Layouts;` (line 2).

Insert, immediately after the closing `}` of the existing `if (asksLayout) { ... }` block (after line 99, before the `// Asked beside the layout...` comment on line 101):

```csharp

        // Beside the layout question, and gated on the layout this build will actually use: asking
        // about field arrangement when the window will not be the business shell has no effect and
        // would falsely imply one. Gated on the RESOLVED layout, not the offered one, so an
        // unusable layout answer (which keeps the previously offered layout) still asks or skips
        // correctly.
        var entryTemplate = start.EntryTemplate;
        var asksEntryTemplate = asksLayout && target is not null && EntryTemplates.Supports(target.Key)
            && (layout ?? AppCustomization.Default.EffectiveLayout) == AppLayout.Business;

        if (asksEntryTemplate)
        {
            writer.WriteLine($"  Entry template: {EntryTemplateCatalog.PromptOptions}");

            var offeredTemplate =
                EntryTemplateCatalog.KeyFor(entryTemplate ?? AppCustomization.Default.EffectiveEntryTemplate);

            var chosenTemplate = Ask(reader, writer, "Entry template", offeredTemplate);
            if (chosenTemplate is null) return null;

            if (EntryTemplateCatalog.TryResolve(chosenTemplate, out var parsedTemplate)
                && (entryTemplate is not null || parsedTemplate != AppCustomization.Default.EffectiveEntryTemplate))
            {
                entryTemplate = parsedTemplate;
            }
        }
```

Add `EntryTemplate = entryTemplate,` to the final `return start with { ... }` (beside `Layout = layout,` at line 172).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~CustomizationWizardTests`
Expected: PASS, including every pre-existing test in the file (re-run the whole file, not just the new tests).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/CustomizationWizard.cs CodeGenerator.Tests/App/CustomizationWizardTests.cs
git commit -m "feat(wizard): ask for an entry template when the business layout is effective"
```

---

## Task 4: Review menu (drop-down) wiring

**Files:**
- Modify: `CodeGenerator/App/CustomizationMenus.cs:1-245`
- Test: `CodeGenerator.Tests/App/CustomizationMenuTests.cs`

**Interfaces:**
- Consumes: same as Task 3, plus the existing `IndexOf(IReadOnlyList<string>, string)` private helper this file already uses for `AskLayout`.
- Produces: `CustomizationMenus.Run(...)` now also asks and sets `EntryTemplate` via a menu row.

- [ ] **Step 1: Write the failing tests**

Update the shared helper in `CodeGenerator.Tests/App/CustomizationMenuTests.cs` (around line 83-84) so every existing caller keeps working once the new question is inserted:

```csharp
    /// <summary>As <see cref="KeepEverything"/>, for a target that is also asked its layout and,
    /// when that layout resolves to business, its entry template. The extra row is harmless when
    /// the resolved layout is NOT business (e.g. a remembered Dashboard): every answer here is
    /// Keep, and Keep means "take whichever question's own default", not a specific row number, so
    /// an unconsumed extra Keep never misaligns a later question.</summary>
    private static int?[] KeepEverythingWithLayout()
        => [0, Keep, Keep, .. Enumerable.Repeat<int?>(Keep, Questions.Length)];
```

Append new tests:

```csharp
    [TestMethod]
    public void The_entry_template_question_is_asked_only_when_business_is_effective()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("wpf", out var wpf));

        // Business is the default, so keeping the layout row keeps business, and the entry
        // template question follows it.
        var (_, withBusiness) = Run(AppCustomization.Default, KeepEverythingWithLayout(), wpf);

        var (labels, _) = withBusiness.Question("Entry template");

        Assert.HasCount(EntryTemplateCatalog.Keys.Count, labels);
        StringAssert.Contains(labels[0], "classic");
    }

    [TestMethod]
    public void The_entry_template_question_is_skipped_for_a_non_business_layout()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("wpf", out var wpf));

        var saved = AppCustomization.Default with { Layout = AppLayout.Dashboard };

        var (_, chooser) = Run(saved, KeepEverythingWithLayout(), wpf);

        Assert.IsFalse(chooser.Asked.Any(a => a.Title == "Entry template"));
    }

    [TestMethod]
    public void The_entry_template_menu_preselects_the_remembered_template()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("wpf", out var wpf));

        var saved = AppCustomization.Default with { EntryTemplate = AppScaffolding.Screens.EntryTemplate.Tabbed };

        var (result, chooser) = Run(saved, KeepEverythingWithLayout(), wpf);

        Assert.AreEqual(
            EntryTemplateCatalog.Keys.ToList().IndexOf("tabbed"),
            chooser.Question("Entry template").Default);

        Assert.IsNotNull(result);
        Assert.AreEqual(AppScaffolding.Screens.EntryTemplate.Tabbed, result.EntryTemplate);
    }

    [TestMethod]
    public void Taking_the_default_entry_template_row_names_no_template_at_all()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("wpf", out var wpf));

        var (result, _) = Run(AppCustomization.Default, KeepEverythingWithLayout(), wpf);

        Assert.IsNotNull(result);
        Assert.IsNull(result.EntryTemplate);
    }
```

Add `using AppScaffolding.Screens;` to the top of `CustomizationMenuTests.cs`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~CustomizationMenuTests`
Expected: New tests FAIL (no "Entry template" question is ever asked).

- [ ] **Step 3: Add the menu question**

In `CodeGenerator/App/CustomizationMenus.cs`, add `using AppScaffolding.Screens;` beside the existing usings (line 1-2).

Insert, immediately after the closing `}` of the existing `if (target is not null && AppLayouts.Supports(target.Key)) { ... }` block (after line 75, before the `// Beside the layout question...` comment on line 77):

```csharp

        var entryTemplate = start.EntryTemplate;

        if (target is not null && EntryTemplates.Supports(target.Key)
            && (layout ?? AppCustomization.Default.EffectiveLayout) == AppLayout.Business)
        {
            var chosenTemplate = AskEntryTemplate(choose, entryTemplate);

            if (chosenTemplate is null)
                return null;

            if (entryTemplate is not null || chosenTemplate != AppCustomization.Default.EffectiveEntryTemplate)
                entryTemplate = chosenTemplate;
        }
```

Add `EntryTemplate = entryTemplate,` to the final `return start with { ... }` (beside `Layout = layout,` at line 173).

Add the private helper, immediately after `AskLayout` (after line 245):

```csharp

    /// <summary>The entry template chosen, or null to cancel.</summary>
    private static EntryTemplate? AskEntryTemplate(
        Func<string, IReadOnlyList<string>, int, int?> choose, EntryTemplate? current)
    {
        var keys = EntryTemplateCatalog.Keys;

        var labels = keys
            .Select(key =>
            {
                EntryTemplateCatalog.TryResolve(key, out var template);

                return $"{key} — {EntryTemplateCatalog.Descriptions[template]}";
            })
            .ToList();

        var offered = EntryTemplateCatalog.KeyFor(current ?? AppCustomization.Default.EffectiveEntryTemplate);

        var row = choose("Entry template", labels, IndexOf(keys, offered));

        if (row is null)
            return null;

        EntryTemplateCatalog.TryResolve(keys[row.Value], out var chosen);

        return chosen;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~CustomizationMenuTests`
Expected: PASS, including every pre-existing test in the file.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/CustomizationMenus.cs CodeGenerator.Tests/App/CustomizationMenuTests.cs
git commit -m "feat(wizard): the entry-template question as a menu row"
```

---

## Task 5: Inline token

**Files:**
- Modify: `CodeGenerator/App/InlineCustomization.cs:34-184`
- Test: `CodeGenerator.Tests/App/InlineCustomizationTests.cs`

**Interfaces:**
- Consumes: `EntryTemplateCatalog.TryResolve` (Task 1).
- Produces: an `entry-template` inline value key.

- [ ] **Step 1: Write the failing tests**

Append to `CodeGenerator.Tests/App/InlineCustomizationTests.cs`:

```csharp
    [TestMethod]
    public void An_entry_template_token_is_recognised()
    {
        var (remaining, customization) = Split("a shop entry-template tabbed");

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual(AppScaffolding.Screens.EntryTemplate.Tabbed, customization.EntryTemplate);
    }

    [TestMethod]
    public void Entry_template_grouped_is_recorded_as_typed_rather_than_treated_as_absent()
    {
        var (_, customization) = Split("a shop entry-template grouped");

        Assert.AreEqual(AppScaffolding.Screens.EntryTemplate.Grouped, customization.EntryTemplate);
        Assert.IsNotNull(customization.EntryTemplate);
    }

    [TestMethod]
    public void An_unusable_entry_template_is_left_in_the_description()
    {
        var (remaining, customization) = Split("a shop entry-template kanban");

        Assert.AreEqual("a shop entry-template kanban", remaining);
        Assert.IsNull(customization.EntryTemplate);
    }

    [TestMethod]
    public void Entry_template_combines_with_other_options()
    {
        var (remaining, customization) =
            Split("a shop company \"Codis Ltd\" entry-template split theme dark");

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual(AppScaffolding.Screens.EntryTemplate.Split, customization.EntryTemplate);
        Assert.AreEqual(AppTheme.Dark, customization.Theme);
        Assert.AreEqual("Codis Ltd", customization.Company);
    }
```

(Reuse whatever private `Split` helper the existing tests in that file already call — the same one `Layout_tabs_is_recorded_as_typed_rather_than_treated_as_absent` and neighboring tests use.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~InlineCustomizationTests`
Expected: FAIL — `entry-template` is not a recognised key, so it is left in the description untouched and `customization.EntryTemplate` stays null.

- [ ] **Step 3: Add the value key**

In `CodeGenerator/App/InlineCustomization.cs`, add `"entry-template"` to `ValueKeys` (line 35-39):

```csharp
    private static readonly string[] ValueKeys =
    [
        "company", "theme", "accent", "icon", "title", "namespace", "layout", "entry-template",
        "transactions", "masters",
    ];
```

Add a case to `ApplyValue`'s switch (beside the existing `"layout"` case, after line 179):

```csharp

            case "entry-template":
                // Refused rather than ignored when unusable, like `layout kanban`: the words are
                // more likely part of the description than a template name we should guess at.
                return EntryTemplateCatalog.TryResolve(value, out var entryTemplate)
                    ? customization with { EntryTemplate = entryTemplate }
                    : null;
```

Add `using AppScaffolding.Screens;` to the top of `InlineCustomization.cs` (beside the existing `using AppScaffolding;`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~InlineCustomizationTests`
Expected: PASS, including every pre-existing test in the file.

- [ ] **Step 5: Echo it back in the web wizard's "same run as a command" line**

`CodeGenerator/Web/Workflow/BuildCommandEcho.cs` builds the inline-equivalent command line `OutputStage` shows under "The same run as a command", and already echoes `layout` when set (lines 77-78); `entry-template` needs the same treatment or a run that used it cannot be replayed from the echoed line. `CodeGenerator.Tests/Web/BuildCommandEchoTests.cs`'s `Every_customization_the_page_can_set_comes_back_as_itself` round-trips every option through `BuildCommandEcho.For` and back through the parser — extend both.

In `BuildCommandEchoTests.cs`, add to the `asked` customization (beside `Layout = AppLayout.MasterDetail,`, line 94):

```csharp
            EntryTemplate = AppScaffolding.Screens.EntryTemplate.Tabbed,
```

Add beside the `Layout` assertion (after line 111):

```csharp
        Assert.AreEqual(asked.EntryTemplate, parsed.Customization.EntryTemplate);
```

Run: `dotnet test --filter FullyQualifiedName~Every_customization_the_page_can_set_comes_back_as_itself`
Expected: FAIL — `BuildCommandEcho.For` does not echo `entry-template` yet, so the reparsed customization's `EntryTemplate` is null.

In `BuildCommandEcho.cs`, add beside the `Layout` clause (after line 78):

```csharp

        if (customization.EntryTemplate is { } entryTemplate)
            options += $" entry-template {AppScaffolding.Screens.EntryTemplateCatalog.KeyFor(entryTemplate)}";
```

Run: `dotnet test --filter FullyQualifiedName~BuildCommandEchoTests`
Expected: PASS, including every pre-existing test in the file.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/App/InlineCustomization.cs CodeGenerator.Tests/App/InlineCustomizationTests.cs CodeGenerator/Web/Workflow/BuildCommandEcho.cs CodeGenerator.Tests/Web/BuildCommandEchoTests.cs
git commit -m "feat(cli): entry-template as an inline build app option, echoed back by the web wizard"
```

---

## Task 6: Review summary line

**Files:**
- Modify: `CodeGenerator/App/CustomizationSummary.cs:21-74`
- Test: create `CodeGenerator.Tests/App/CustomizationSummaryTests.cs` if none exists, else extend it

**Interfaces:**
- Consumes: `EntryTemplateCatalog.KeyFor` (Task 1).
- Produces: an "Entry template: <key>" line in `CustomizationSummary.Lines`.

- [ ] **Step 1: Check for an existing test file**

Run: `find . -iname "CustomizationSummaryTests.cs"` (Bash) or the PowerShell equivalent. If it exists, add the test below to it; otherwise create it with this content plus a `using AppScaffolding.Screens;`.

- [ ] **Step 2: Write the failing test**

```csharp
using AppScaffolding;
using AppScaffolding.Screens;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class CustomizationSummaryTests
{
    [TestMethod]
    public void An_explicit_entry_template_is_named()
    {
        var customization = AppCustomization.Default with { EntryTemplate = EntryTemplate.Tabbed };

        var lines = CustomizationSummary.Lines(customization);

        CollectionAssert.Contains(lines.ToList(), "  Entry template: tabbed");
    }

    [TestMethod]
    public void No_entry_template_produces_no_line()
    {
        var lines = CustomizationSummary.Lines(AppCustomization.Default);

        Assert.IsFalse(lines.Any(l => l.Contains("Entry template")));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CustomizationSummaryTests`
Expected: FAIL — no such line is ever added.

- [ ] **Step 4: Add the line**

In `CodeGenerator/App/CustomizationSummary.cs`, insert immediately after the existing `Layout` line (after line 51):

```csharp

        if (customization.EntryTemplate is not null)
            lines.Add($"  Entry template: {EntryTemplateCatalog.KeyFor(customization.EntryTemplate.Value)}");
```

Add `using AppScaffolding.Screens;` to the top of the file.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~CustomizationSummaryTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/App/CustomizationSummary.cs CodeGenerator.Tests/App/CustomizationSummaryTests.cs
git commit -m "feat(wizard): name an explicit entry template in the review summary"
```

---

## Task 7: A dedicated "Entry Template" workflow stage

**Files:**
- Modify: `CodeGenerator/Web/Workflow/BuildStage.cs`
- Modify: `CodeGenerator/Web/Workflow/StageGraph.cs`
- Modify: `CodeGenerator/Web/Components/BuildWorkflow.razor`
- Create: `CodeGenerator/Web/Components/Stages/EntryTemplateStage.razor`

**Interfaces:**
- Consumes: `EntryTemplateCatalog.Keys`/`Descriptions`/`TryResolve`, `EntryTemplates.Supports` (Task 1); `Session.Target`, `Session.Customization`, `Session.SetCustomization` (existing `BuildSession` members every other stage already uses).
- Produces: a tenth workflow node, `BuildStage.EntryTemplate`, sitting between `Output` and `Generate` — "how master and transaction screens arrange their fields" gets its own step, distinct from `Output`'s master/transaction *role* table, matching the request for a dedicated stage rather than one more field folded into `LookStage`.

The user asked for this explicitly, superseding the earlier design (an `EntryTemplate` dropdown inside `LookStage`) — the choice is thematically about how master/transaction *screens* look, not about branding/theme/features, and deserves its own place in the diagram next to where those screens' *roles* are already decided (`Output`).

Per `BuildStage.cs`'s own remarks, every value in the enum is **always** a node in the diagram — there is no "skip this stage" mechanism anywhere in this workflow (`StageGraph.Nodes`/`Next`/`Previous`/`Advance` walk it unconditionally). So when the choice does not apply (a non-WPF target, or a WPF target not using the `business` layout), the stage still appears, but its panel says so and offers nothing to choose — the same idiom `LookStage`'s gate checkbox and `OutputStage`'s `plan is null` branch already use for "this step has nothing to ask on this run."

- [ ] **Step 1: Add the enum member**

In `CodeGenerator/Web/Workflow/BuildStage.cs`, insert between `Output,`'s closing (line 45) and `Generate,` (line 47):

```csharp

    /// <summary>
    /// How a business-shell master or transaction screen arranges its fields.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Output"/>, which decides WHICH classes get a master or transaction
    /// screen at all — this decides how the screens that exist look. Nothing downstream reads it
    /// until Generate, so it invalidates nothing (see <see cref="StageGraph.Invalidates"/>), the
    /// same as <see cref="Look"/> and <see cref="Output"/>.
    /// <para>
    /// Written to a snapshot by name, never by ordinal (see the remarks on <see cref="Output"/>) —
    /// do not rename this once it has shipped.
    /// </para>
    /// </remarks>
    EntryTemplate,
```

- [ ] **Step 2: Add the node and the invalidation rule**

In `CodeGenerator/Web/Workflow/StageGraph.cs`, insert into `Nodes` between the `Output` and `Generate` rows (between lines 24 and 25):

```csharp
        new(BuildStage.EntryTemplate, "Entry Template", "How master and transaction screens arrange their fields"),
```

Add a case to `Invalidates`'s switch (beside `BuildStage.Output => [],`, after line 99):

```csharp

            // Nothing downstream reads this until Generate, and Generate has no stored answer to
            // discard — so changing it costs nothing, exactly as Output and Look do.
            BuildStage.EntryTemplate => [],
```

- [ ] **Step 3: Fix the tests that hardcode the node count and order**

`CodeGenerator.Tests/Web/StageGraphTests.cs` pins both the total node count and `Output`'s immediate successor; both must move with the new node.

Replace `Has_nine_nodes_in_workflow_order` (lines 9-19):

```csharp
    [TestMethod]
    public void Has_ten_nodes_in_workflow_order()
    {
        Assert.HasCount(10, StageGraph.Nodes);
        Assert.AreEqual(BuildStage.Describe, StageGraph.Nodes[0].Stage);
        Assert.AreEqual(BuildStage.EntryTemplate, StageGraph.Nodes[7].Stage);
        Assert.AreEqual(BuildStage.Generate, StageGraph.Nodes[8].Stage);

        // Running it is its own stage, not a button on the generate panel: it is the step that
        // tells the user whether what was written actually works.
        Assert.AreEqual(BuildStage.Run, StageGraph.Nodes[9].Stage);
    }
```

Replace `Output_follows_review_and_precedes_generate` (lines 21-29):

```csharp
    [TestMethod]
    public void Output_follows_review_and_precedes_entry_template()
    {
        // The split: Review settles what the classes ARE, Output settles how they connect, which
        // get a screen, and what gets written; Entry Template settles how those screens look.
        Assert.AreEqual(BuildStage.Output, StageGraph.Next(BuildStage.Review));
        Assert.AreEqual(BuildStage.EntryTemplate, StageGraph.Next(BuildStage.Output));
        Assert.AreEqual(BuildStage.Review, StageGraph.Previous(BuildStage.Output));
    }

    [TestMethod]
    public void Entry_template_follows_output_and_precedes_generate()
    {
        Assert.AreEqual(BuildStage.Generate, StageGraph.Next(BuildStage.EntryTemplate));
        Assert.AreEqual(BuildStage.Output, StageGraph.Previous(BuildStage.EntryTemplate));
    }
```

Add `BuildStage.EntryTemplate` to `Changing_an_answer_read_only_at_generate_invalidates_nothing` (lines 126-134), beside `BuildStage.Look`:

```csharp
    [TestMethod]
    public void Changing_an_answer_read_only_at_generate_invalidates_nothing()
    {
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.AppType));
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.Look));
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.Review));
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.EntryTemplate));
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.Generate));
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.Run));
    }
```

Run: `dotnet test --filter FullyQualifiedName~StageGraphTests`
Expected: FAIL at this point (`BuildStage.EntryTemplate` does not exist until Step 1, and `Nodes`/`Invalidates` do not know it until Step 2 — if executing strictly TDD-style, do Steps 1-2 first, then this step's edits should already pass; if they still fail, re-check that `Nodes`'s new entry sits at index 7, i.e. immediately after `Output` and before `Generate`, matching the order in the plan above).

- [ ] **Step 4: Wire it into the workflow switch**

In `CodeGenerator/Web/Components/BuildWorkflow.razor`, add `@using CodeGenerator.Web.Components.Stages` is already present; add a case between `BuildStage.Output` and `BuildStage.Generate` (between lines 140 and 141):

```razor
                case BuildStage.EntryTemplate:
                    <EntryTemplateStage @key="BuildStage.EntryTemplate" Session="Session"
                                        Navigation="_navigation" OnAdvance="Refresh" />
                    break;
```

- [ ] **Step 5: Write the stage component**

```razor
@* CodeGenerator/Web/Components/Stages/EntryTemplateStage.razor *@
@using AppScaffolding
@using AppScaffolding.Layouts
@using AppScaffolding.Screens
@using CodeGenerator.Web.Workflow

@* Node 8: how master and transaction screens arrange their fields, distinct from Output's
   choice of WHICH classes get one of those screens at all. Always present in the diagram —
   this workflow has no mechanism to skip a node — so when the choice does not apply, the panel
   says so and offers nothing to choose, the same idiom LookStage's gate and OutputStage's
   `plan is null` branch already use. *@

@if (Applicable)
{
    <div class="fields">
        <label>
            Template
            <select class="entry-template answer" value="@_template"
                    @onchange="e => _template = e.Value?.ToString() ?? _template">
                @foreach (var key in EntryTemplateCatalog.Keys)
                {
                    EntryTemplateCatalog.TryResolve(key, out var parsed);

                    <option value="@key">@key — @EntryTemplateCatalog.Descriptions[parsed]</option>
                }
            </select>
        </label>
    </div>
}
else
{
    <p class="not-applicable">
        Only available for a WPF application using the business layout — this run will use the
        standard grouped entry screens.
    </p>
}

<div class="actions">
    <button type="button" class="cancel" @onclick="() => Session.RequestCancel()">Cancel</button>
</div>

@code {
    [Parameter, EditorRequired] public required BuildSession Session { get; set; }
    [Parameter, EditorRequired] public required StageNavigation Navigation { get; set; }
    [Parameter] public EventCallback OnAdvance { get; set; }

    private string _template = "grouped";

    /// <summary>Whether this target and this run's layout can even use the choice — mirrors the
    /// same gate <see cref="EntryTemplates"/> enforces at generation time, so the wizard never
    /// offers an answer the generator would ignore.</summary>
    private bool Applicable
        => Session.Target is not null
           && EntryTemplates.Supports(Session.Target.Key)
           && Session.Customization.EffectiveLayout == AppLayout.Business;

    protected override void OnInitialized()
    {
        Navigation.Offer(Next, ready: () => true);

        _template = EntryTemplateCatalog.KeyFor(Session.Customization.EffectiveEntryTemplate);
    }

    private Task<bool> Next()
    {
        // Nothing to apply when the choice does not apply: the customization is left exactly as
        // it arrived, so a WinForms or non-business run's EntryTemplate stays whatever it already
        // was (null, since nothing upstream of this stage can set it either).
        if (Applicable && EntryTemplateCatalog.TryResolve(_template, out var template))
        {
            Session.SetCustomization(Session.Customization with { EntryTemplate = template });
        }

        return Task.FromResult(true);
    }
}
```

- [ ] **Step 6: Run the full test suite for this task**

Run: `dotnet test --filter FullyQualifiedName~StageGraphTests` (expect PASS, all tests including the new/renamed ones) and `dotnet build` for the whole solution (`EntryTemplateStage.razor` must compile against `BuildSession`/`StageNavigation`'s real signatures).

- [ ] **Step 7: Manual verification**

Run the web app (per this repo's `run` skill or its own dev-server instructions), open the `build app` web wizard: (a) for a WPF target with the `business` layout, confirm the Entry Template node offers all six templates, pre-selected to the current customization's effective value, and that choosing one and completing the build produces the expected WPF output (cross-check against Task 15's manual runs); (b) for a WPF target with a non-business layout, or a non-WPF target, confirm the node still appears in the diagram but shows the "not applicable" message and Continue passes through with no change; (c) confirm the diagram still lays out all ten nodes without visual breakage, and that revisiting the Output node and changing a screen role still leaves the Entry Template answer untouched (per `Invalidates => []`).

- [ ] **Step 8: Commit**

```bash
git add CodeGenerator/Web/Workflow/BuildStage.cs CodeGenerator/Web/Workflow/StageGraph.cs CodeGenerator/Web/Components/BuildWorkflow.razor "CodeGenerator/Web/Components/Stages/EntryTemplateStage.razor" CodeGenerator.Tests/Web/StageGraphTests.cs
git commit -m "feat(web): a dedicated workflow stage for the entry-template choice"
```

---

## Task 8: Help catalogue and `docs/commands.md`/manual regeneration

**Files:**
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs:670-687`
- Test: `CodeGenerator.Tests/Core/CommandDictionaryDataTests.cs`

**Interfaces:**
- Consumes: `EntryTemplateCatalog.Keys` (Task 1).

- [ ] **Step 1: Write the failing test**

Locate the existing loop in `CodeGenerator.Tests/Core/CommandDictionaryDataTests.cs` (around line 101-107):

```csharp
        foreach (var key in AppScaffolding.AppLayoutCatalog.Keys)
            StringAssert.Contains(buildApp, key, $"layout key '{key}' is not in the help catalog.");
```

Add immediately after it:

```csharp

        foreach (var key in AppScaffolding.Screens.EntryTemplateCatalog.Keys)
            StringAssert.Contains(buildApp, key, $"entry-template key '{key}' is not in the help catalog.");
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CommandDictionaryDataTests`
Expected: FAIL — none of the six entry-template keys appear in the `Build App` help text yet.

- [ ] **Step 3: Extend the help text**

In `CodeGenerationEngine/Core/CommandDictionary.cs`, the `Build App` entry whose `Description` explains `layout` (lines 670-687) gets one more clause appended, right after the existing layout clause and before the closing sentence about `.codegen/app-profile.json`:

```csharp
                        + "layout tabs|sidebar|master-detail|dashboard|business chooses the shape "
                        + "of the window (wpf and winforms only); business is the default and is "
                        + "the shell with a dashboard home, a rail grouped into masters and "
                        + "transactions, and an editor for every record; the web app is always "
                        + "that shell, which is why naming a layout does not apply to it. "
                        + "entry-template classic|grouped|tabbed|two-column|split|ribbon chooses "
                        + "how a business-shell entry form arranges its fields (wpf only, for now, "
                        + "and only when the layout is business); grouped is the default. A "
                        + "layout you chose on an earlier interactive run is remembered in "
                        + ".codegen/app-profile.json and kept, default or not, so a run that "
                        + "is using a remembered one says so and names the file.",
```

(This replaces the existing four lines from `+ "that shell, which is why..."` through `+ "is using a remembered one says so and names the file."` — insert the new `entry-template` clause between them, keeping every existing word intact.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~CommandDictionaryDataTests`
Expected: PASS.

- [ ] **Step 5: Regenerate `docs/commands.md` and the manual appendix**

Per the existing convention (`docs/commands.md` and the manual appendix are both generated from `CommandDictionary`), run whatever generation command/test this repo uses to refresh them (check `CodeGenerator.Tests` for a test that writes/compares `docs/commands.md`, e.g. search for `"docs/commands.md"` or `"commands.md"` in the test tree, and run it — it should now regenerate the file with the new `entry-template` text included). Confirm with `git diff docs/commands.md` that only the expected new text appears.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/Core/CommandDictionary.cs CodeGenerator.Tests/Core/CommandDictionaryDataTests.cs docs/commands.md
git commit -m "docs: entry-template in the build app help catalogue"
```

---

## Task 9: `FieldGroups` gains a column count and a flat ordering

**Files:**
- Modify: `AppScaffolding/Screens/FieldGroupsTemplate.cs:174-249`
- Test: `CodeGenerator.Tests/App/FieldGroupsTests.cs`

**Interfaces:**
- Produces (in the EMITTED `FieldGroups` class): `Flat(Type)` (new), `For(Type)` (unchanged signature, now implemented via `Flat`), `Columns(Type, int columnCount = 3)` (gains a parameter, default preserves today's behavior).

- [ ] **Step 1: Write the failing tests**

Append to `CodeGenerator.Tests/App/FieldGroupsTests.cs` (reusing the file's existing `Columns(modelStub, typeName, out assembly)` helper and `ModelStub`):

```csharp
    private static List<List<string>> Columns(string modelStub, string typeName, int columnCount)
    {
        var source = EmittedSource.Flatten(
            modelStub,
            FieldGroupsTemplate.Source("Acme").Replace("namespace Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
        var columns = (System.Collections.IEnumerable)assembly.GetType("FieldGroups")!
            .GetMethod("Columns")!.Invoke(null, [assembly.GetType(typeName)!, columnCount])!;

        return columns.Cast<System.Collections.IEnumerable>()
            .Select(c => c.Cast<object>()
                .Select(g => (string)g.GetType().GetProperty("Title")!.GetValue(g)!)
                .ToList())
            .ToList();
    }

    [TestMethod]
    public void A_column_count_of_two_packs_into_two_columns()
    {
        var columns = Columns(ModelStub, "Customer", columnCount: 2);

        Assert.HasCount(2, columns);
        Assert.HasCount(4, columns.SelectMany(c => c).ToList());
    }

    [TestMethod]
    public void The_default_column_count_is_still_three()
        => Assert.HasCount(3, Columns(ModelStub, "Customer"));

    [TestMethod]
    public void Flat_excludes_the_key_fields_and_long_text_but_keeps_declaration_order()
    {
        var source = EmittedSource.Flatten(
            ModelStub,
            FieldGroupsTemplate.Source("Acme").Replace("namespace Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
        var type = assembly.GetType("Customer")!;

        var flat = ((System.Collections.IEnumerable)assembly.GetType("FieldGroups")!
                .GetMethod("Flat")!.Invoke(null, [type])!)
            .Cast<System.Reflection.PropertyInfo>()
            .Select(p => p.Name)
            .ToList();

        var key = assembly.GetType("FieldGroups")!.GetMethod("Key")!.Invoke(null, [type]);
        var codeName = (string?)key!.GetType().GetProperty("Code")!.GetValue(key) is null
            ? null
            : ((System.Reflection.PropertyInfo)key.GetType().GetProperty("Code")!.GetValue(key)!).Name;

        if (codeName is not null)
            Assert.DoesNotContain(codeName, flat);

        Assert.IsGreaterThan(0, flat.Count);
    }
```

(Check `ModelStub`'s exact field content in the existing file before writing precise assertions on which names appear — reuse it exactly as the file already defines it; the assertions above are intentionally shape-based rather than naming `ModelStub`'s fields, so they hold regardless of its exact contents.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~FieldGroupsTests`
Expected: FAIL — `Columns` takes one argument today, and `Flat` does not exist.

- [ ] **Step 3: Refactor `For` to expose `Flat`, and parameterize `Columns`**

In `AppScaffolding/Screens/FieldGroupsTemplate.cs`, replace the body of the emitted `For` method (lines 175-214) and add `Flat` immediately before it:

```csharp
    /// <summary>The fields an entry form shows, in declaration order, with the key fields and the
    /// long-text fields excluded — for a template with no grouping.</summary>
    public static IReadOnlyList<PropertyInfo> Flat(Type type)
    {
        var key = Key(type);

        return Displayable(type)
            .Where(p => p != key.Code && p != key.Date && p != key.Status)
            .Where(p => !IsLongText(p))
            .ToList();
    }

    /// <summary>The titled panels, in display order. Never contains an empty group.</summary>
    public static IReadOnlyList<FieldGroup> For(Type type)
    {
        var pool = Flat(type);

        var basic = new List<PropertyInfo>();
        var address = new List<PropertyInfo>();
        var contact = new List<PropertyInfo>();
        var other = new List<PropertyInfo>();

        foreach (var property in pool)
        {
            var name = LabelOf(property).ToLowerInvariant();

            if (AddressWords.Any(name.Contains))
                address.Add(property);
            else if (ContactWords.Any(name.Contains))
                contact.Add(property);
            else if (OtherWords.Any(name.Contains) || IsDate(property))
                other.Add(property);
            else
                basic.Add(property);
        }

        // A heading with nothing under it is noise -- the rule DashboardViewModel.Group follows.
        return new[]
            {
                new FieldGroup(Basic, basic.Take(PanelLimit).ToList()),
                new FieldGroup(More, basic.Skip(PanelLimit).ToList()),
                new FieldGroup(Address, address),
                new FieldGroup(Contact, contact),
                new FieldGroup(Other, other),
            }
            .Where(g => g.Fields.Count > 0)
            .ToList();
    }
```

Replace `Columns` (lines 229-249):

```csharp
    /// <summary>
    /// The panels packed into <paramref name="columnCount"/> columns.
    /// </summary>
    /// <remarks>
    /// Always <paramref name="columnCount"/> lists, so a view can lay out a fixed grid; any of them
    /// may be empty, and the view collapses an empty one so a small model is not mostly whitespace.
    /// Each group goes to whichever column holds the fewest fields, ties going left. The default of
    /// three reproduces the master mockup for the four-group case: Basic alone, Address alone,
    /// Contact and Other stacked.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<FieldGroup>> Columns(Type type, int columnCount = 3)
    {
        var columns = new List<FieldGroup>[columnCount];

        for (var i = 0; i < columnCount; i++)
            columns[i] = new List<FieldGroup>();

        var weights = new int[columnCount];

        foreach (var group in For(type))
        {
            var target = 0;

            for (var i = 1; i < weights.Length; i++)
            {
                if (weights[i] < weights[target])
                    target = i;
            }

            columns[target].Add(group);
            weights[target] += group.Fields.Count;
        }

        return columns;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~FieldGroupsTests`
Expected: PASS, including every pre-existing test in the file (`There_are_always_three_columns` etc. call `Columns(type)` with the default, unaffected).

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Screens/FieldGroupsTemplate.cs CodeGenerator.Tests/App/FieldGroupsTests.cs
git commit -m "feat(scaffold): FieldGroups gains a column count and a flat ordering"
```

---

## Task 10: WPF entry form — Classic, TwoColumn and Tabbed

**Files:**
- Modify: `AppScaffolding/Layouts/Wpf/WpfEntryControls.cs:1-444` (the `Form` method)
- Modify: `AppScaffolding/Layouts/Wpf/WpfBusinessLayout.cs:30-48` (thread the template through, done fully in Task 14 — this task only changes `WpfEntryControls.Form`)
- Test: `CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs`

**Interfaces:**
- Consumes: `FieldGroups.Flat`, `FieldGroups.For`, `FieldGroups.Columns(type, columnCount)` (Task 9); `LayoutContext.Customization.EffectiveEntryTemplate` (Task 2).
- Produces: `WpfEntryControls.Form(LayoutContext)` now emits one of four `Build()` bodies depending on the effective template.

- [ ] **Step 1: Write the failing tests**

Append to `CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs`:

```csharp
    private static LayoutContext ContextFor(AppScaffolding.Screens.EntryTemplate template)
        => Context(AppCustomization.Default with { EntryTemplate = template });

    [TestMethod]
    public void Classic_lays_out_one_flat_column_with_no_panels()
    {
        var source = WpfEntryControls.Form(ContextFor(AppScaffolding.Screens.EntryTemplate.Classic));

        StringAssert.Contains(source, "FieldGroups.Flat(");
        Assert.DoesNotContain("Panel(group)", source);
    }

    [TestMethod]
    public void TwoColumn_packs_FieldGroups_into_two_columns()
    {
        var source = WpfEntryControls.Form(ContextFor(AppScaffolding.Screens.EntryTemplate.TwoColumn));

        StringAssert.Contains(source, "FieldGroups.Columns(Record.GetType(), 2)");
    }

    [TestMethod]
    public void Grouped_still_packs_into_the_default_three_columns()
    {
        var source = WpfEntryControls.Form(ContextFor(AppScaffolding.Screens.EntryTemplate.Grouped));

        StringAssert.Contains(source, "FieldGroups.Columns(Record.GetType())");
    }

    [TestMethod]
    public void Tabbed_lays_each_group_out_as_its_own_tab()
    {
        var source = WpfEntryControls.Form(ContextFor(AppScaffolding.Screens.EntryTemplate.Tabbed));

        StringAssert.Contains(source, "new TabControl()");
        StringAssert.Contains(source, "FieldGroups.For(");
        StringAssert.Contains(source, "Header = group.Title");
    }

    [TestMethod]
    public void Every_entry_template_still_compiles_with_FieldGroups_and_RequiredFields()
    {
        foreach (var template in Enum.GetValues<AppScaffolding.Screens.EntryTemplate>())
        {
            var context = ContextFor(template);

            EmittedDesktopSource.AssertCompilesTogether(
                ("Screens/FieldGroups.cs", FieldGroupsTemplate.Source("Payroll")),
                ("Screens/RequiredFields.cs", RequiredFieldsTemplate.Source("Payroll", null)),
                ($"Views/{WpfEntryControls.HeaderFileName}", WpfEntryControls.Header(context)),
                ($"Views/{WpfEntryControls.FormFileName}", WpfEntryControls.Form(context)),
                ($"Views/{WpfEntryControls.NotesFileName}", WpfEntryControls.Notes(context)));
        }
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~WpfBusinessLayoutTests`
Expected: `Grouped_still_packs_into_the_default_three_columns` PASSES already (current behavior); the Classic/TwoColumn/Tabbed tests FAIL, since `Form` ignores the entry template entirely today.

- [ ] **Step 3: Branch `Form`'s `Build()` body on the effective template**

In `AppScaffolding/Layouts/Wpf/WpfEntryControls.cs`, add `using AppScaffolding.Screens;` at the top of the file (before `namespace AppScaffolding.Layouts.Wpf;`).

Immediately before the `return $$"""` line that opens `Form`'s raw string literal (the line right after the `var surfaceAssign = ...;` block, before `return $$"""` — currently line 193), insert:

```csharp

        var buildMethod = context.Customization.EffectiveEntryTemplate switch
        {
            EntryTemplate.Classic => """
                    private void Build()
                    {
                        Children.Clear();
                        ColumnDefinitions.Clear();

                        if (Record is null)
                            return;

                        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                        var stack = new StackPanel { Margin = new Thickness(6) };

                        foreach (var field in FieldGroups.Flat(Record.GetType()))
                            stack.Children.Add(Row(field));

                        SetColumn(stack, 0);
                        Children.Add(stack);
                    }
                """,

            EntryTemplate.TwoColumn => """
                    private void Build()
                    {
                        Children.Clear();
                        ColumnDefinitions.Clear();

                        if (Record is null)
                            return;

                        var columns = FieldGroups.Columns(Record.GetType(), 2);

                        for (var i = 0; i < columns.Count; i++)
                        {
                            if (columns[i].Count == 0)
                                continue;

                            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                            var stack = new StackPanel { Margin = new Thickness(6) };

                            foreach (var group in columns[i])
                                stack.Children.Add(Panel(group));

                            SetColumn(stack, ColumnDefinitions.Count - 1);
                            Children.Add(stack);
                        }
                    }
                """,

            EntryTemplate.Tabbed => """
                    private void Build()
                    {
                        Children.Clear();
                        ColumnDefinitions.Clear();

                        if (Record is null)
                            return;

                        var tabs = new TabControl();

                        foreach (var group in FieldGroups.For(Record.GetType()))
                        {
                            var stack = new StackPanel { Margin = new Thickness(10) };

                            foreach (var field in group.Fields)
                                stack.Children.Add(Row(field));

                            tabs.Items.Add(new TabItem { Header = group.Title, Content = new ScrollViewer { Content = stack } });
                        }

                        Children.Add(tabs);
                    }
                """,

            // Grouped, Split and Ribbon all use the three-column grouped form: Split and Ribbon
            // change composition and chrome respectively, not field arrangement.
            _ => """
                    private void Build()
                    {
                        Children.Clear();
                        ColumnDefinitions.Clear();

                        if (Record is null)
                            return;

                        var columns = FieldGroups.Columns(Record.GetType());

                        for (var i = 0; i < columns.Count; i++)
                        {
                            if (columns[i].Count == 0)
                                continue;

                            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                            var stack = new StackPanel { Margin = new Thickness(6) };

                            foreach (var group in columns[i])
                                stack.Children.Add(Panel(group));

                            SetColumn(stack, ColumnDefinitions.Count - 1);
                            Children.Add(stack);
                        }
                    }
                """,
        };
```

Then, inside the `Form` method's raw string literal, replace the existing `private void Build() { ... }` method (the block currently at lines 234-260, between the `SetSurface` method and the `Panel` method) with a single splice:

```csharp
{{buildMethod}}
```

(Leave `Panel`, `Row`, `Editor` and `Rows` exactly as they are — only the `Build()` method body is replaced by the interpolation.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~WpfBusinessLayoutTests`
Expected: PASS, including every pre-existing test in the file (`The_form_lays_out_the_columns_FieldGroups_packs`, `The_form_binds_every_field_it_draws`, etc. — all exercise `Context()`, which defaults to `EntryTemplate: null` → `Grouped`, so they hit the unchanged `_ =>` branch).

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Layouts/Wpf/WpfEntryControls.cs CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs
git commit -m "feat(wpf): Classic, TwoColumn and Tabbed entry-form templates"
```

---

## Task 11: WPF Ribbon action bar

**Files:**
- Modify: `AppScaffolding/Layouts/Wpf/WpfBusinessLayout.cs:50-436` (the `Xaml` method)
- Test: `CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs`

**Interfaces:**
- Consumes: `LayoutContext.Customization.EffectiveEntryTemplate` (Task 2).
- Produces: when the effective template is `Ribbon`, the Master, Transaction-list and Editor action bars render as an icon-and-label strip instead of plain `Button`s; every other template keeps today's bars unchanged.

- [ ] **Step 1: Write the failing tests**

Append to `CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs`. WPF markup has no `class` attribute; the ribbon bar is marked with `Tag="ribbon"` instead — a `Tag` carries no behaviour of its own, which is exactly why it is free for a test needle like this.

```csharp
    private static string XamlFor(AppScaffolding.Screens.EntryTemplate template)
        => Xaml(AppCustomization.Default with { EntryTemplate = template });

    /// <summary>As the file's existing <c>TemplateSlice(key)</c>, but against a specific entry
    /// template rather than always the default.</summary>
    private static string TemplateSlice(string key, AppScaffolding.Screens.EntryTemplate template)
    {
        var xaml = XamlFor(template);
        var from = xaml.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, from, $"no template keyed '{key}'");

        var depth = 1;
        var at = from;

        while (depth > 0)
        {
            var opened = xaml.IndexOf("<DataTemplate", at + 1, StringComparison.Ordinal);
            var closed = xaml.IndexOf("</DataTemplate>", at + 1, StringComparison.Ordinal);

            Assert.IsGreaterThan(-1, closed, $"'{key}' is never closed");

            if (opened >= 0 && opened < closed)
            {
                depth++;
                at = opened;
                continue;
            }

            depth--;
            at = closed;
        }

        return xaml.Substring(from, at - from);
    }

    [TestMethod]
    public void Ribbon_renders_the_master_action_bar_as_an_icon_strip()
    {
        var master = TemplateSlice("MasterTemplate", AppScaffolding.Screens.EntryTemplate.Ribbon);

        StringAssert.Contains(master, "Tag=\"ribbon\"");
        StringAssert.Contains(master, "Command=\"{Binding SaveCommand}\"");
        StringAssert.Contains(master, "Command=\"{Binding DeleteCommand}\"");
    }

    [TestMethod]
    public void Non_ribbon_templates_keep_the_plain_button_bar()
    {
        var master = TemplateSlice("MasterTemplate", AppScaffolding.Screens.EntryTemplate.Grouped);

        Assert.DoesNotContain("Tag=\"ribbon\"", master);
        StringAssert.Contains(master, "<Button Content=\"Save\"");
    }

    [TestMethod]
    public void Ribbon_still_offers_every_command_the_default_bar_offers()
    {
        var master = TemplateSlice("MasterTemplate", AppScaffolding.Screens.EntryTemplate.Ribbon);

        foreach (var command in new[]
                 { "NewCommand", "EditCommand", "DeleteCommand", "SaveCommand", "ClearCommand", "CancelCommand" })
        {
            StringAssert.Contains(master, command);
        }
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~WpfBusinessLayoutTests`
Expected: The Ribbon tests FAIL (`Tag="ribbon"` is never emitted; the action bar is always the plain button row regardless of template).

- [ ] **Step 3: Emit the ribbon bar**

In `AppScaffolding/Layouts/Wpf/WpfBusinessLayout.cs`'s `Xaml` method, add near the top (beside the existing `accentBrush`/`additionalTitle` local variables, before the `return $$$"""` line):

```csharp
        var isRibbon = context.Customization.EffectiveEntryTemplate == Screens.EntryTemplate.Ribbon;

        // WPF's <Style> can style every Button in a scope without repeating six attributes six
        // times, but a StaticResource style can't be reused across the two DIFFERENT button sets
        // (Master's six commands, the Editor's two) without also being shared, so each bar carries
        // its own inline styling instead -- consistent with how every other brush in this file is
        // assigned, and avoiding a fifth Window.Resources entry for a chrome choice this narrow.
        static string RibbonButton(string content, string command, string width = "76")
            => $"""
                <StackPanel Orientation="Vertical" Margin="3,0" Width="{width}">
                    <Button Content="{content}" Command="{{Binding {command}}}" Padding="4,6" />
                </StackPanel>
                """;
```

Then, for each of the three action bars, branch between the existing plain button `StackPanel` and a ribbon-styled equivalent, marked `Tag="ribbon"` (XAML has no `class` attribute; `Tag` is the free-form marker WPF actually offers, and carries no behaviour of its own). Replace the Master action bar's `<Border DockPanel.Dock="Bottom">` block (lines 145-157):

```csharp
                <Border DockPanel.Dock="Bottom" Background="{{{railFill}}}" Padding="10,6">
                    <DockPanel>
                        <TextBlock Text="{Binding Status}" Foreground="{{{muted}}}" VerticalAlignment="Center" />
{{{(isRibbon
    ? $"""
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Tag="ribbon">
                            {RibbonButton("New", "NewCommand")}
                            {RibbonButton("Edit", "EditCommand")}
                            {RibbonButton("Delete", "DeleteCommand")}
                            {RibbonButton("Save", "SaveCommand")}
                            {RibbonButton("Clear", "ClearCommand")}
                            {RibbonButton("Cancel", "CancelCommand")}
                        </StackPanel>
""" : """
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                            <Button Content="New" Command="{Binding NewCommand}" Width="80" Margin="4,0" />
                            <Button Content="Edit" Command="{Binding EditCommand}" Width="80" Margin="4,0" />
                            <Button Content="Delete" Command="{Binding DeleteCommand}" Width="80" Margin="4,0" />
                            <Button Content="Save" Command="{Binding SaveCommand}" Width="80" Margin="16,0,4,0" />
                            <Button Content="Clear" Command="{Binding ClearCommand}" Width="80" Margin="4,0" />
                            <Button Content="Cancel" Command="{Binding CancelCommand}" Width="80" Margin="4,0" />
                        </StackPanel>
""")}}}
                    </DockPanel>
                </Border>
```

Apply the same pattern to the Editor action bar (lines 249-256, Save/Cancel only) and, if desired, the Transaction list's New/Open/Delete bar (lines 202-208) — both follow the identical `isRibbon ? ribbonMarkup : plainMarkup` shape, swapped in as the `<StackPanel>` inside their own `<Border>`/`<Grid>`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~WpfBusinessLayoutTests`
Expected: PASS, including `The_window_is_well_formed_xml` (both the ribbon and non-ribbon XAML must still parse) — add `Xaml(AppCustomization.Default with { EntryTemplate = AppScaffolding.Screens.EntryTemplate.Ribbon })` to that test's array of XAML strings it parses.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Layouts/Wpf/WpfBusinessLayout.cs CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs
git commit -m "feat(wpf): ribbon-style action bar for the Ribbon entry template"
```

---

## Task 12: `EditsInline` threading for Split (view-model layer)

**Files:**
- Modify: `AppScaffolding/Screens/TransactionScreenTemplate.cs`
- Modify: `AppScaffolding/Targets/DesktopViewModelFiles.cs:26-72`
- Modify: `AppScaffolding/Targets/WpfTarget.cs:38-56`
- Test: `CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs` (or a new `TransactionScreenTemplateTests.cs` if the emitted-source pattern in `FieldGroupsTests.cs` is preferred — use whichever this repo already has for `TransactionScreenTemplate`; if none, create `CodeGenerator.Tests/App/TransactionScreenTemplateTests.cs`)

**Interfaces:**
- Produces: `TransactionScreenTemplate.Source(rootNamespace, bool editsInline = false)`; `DesktopViewModelFiles.Build(...)` gains a trailing `bool transactionsEditInline = false` parameter; `WpfTarget` passes `customization.EffectiveEntryTemplate == Screens.EntryTemplate.Split`. Windows Forms (`WinFormsTarget.cs`) is **not modified** — it keeps passing the default `false`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/TransactionScreenTemplateTests.cs`:

```csharp
using AppScaffolding.Screens;

namespace CodeGenerator.Tests.App;

[TestClass]
public class TransactionScreenTemplateTests
{
    [TestMethod]
    public void EditsInline_defaults_to_false()
        => StringAssert.Contains(
            TransactionScreenTemplate.Source("Payroll"),
            "protected override bool EditsInline => false;");

    [TestMethod]
    public void EditsInline_can_be_emitted_true_for_split()
        => StringAssert.Contains(
            TransactionScreenTemplate.Source("Payroll", editsInline: true),
            "protected override bool EditsInline => true;");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~TransactionScreenTemplateTests`
Expected: `EditsInline_can_be_emitted_true_for_split` FAILS to compile — `Source` takes one argument today.

- [ ] **Step 3: Parameterize `TransactionScreenTemplate.Source`**

In `AppScaffolding/Screens/TransactionScreenTemplate.cs`, change the signature (line 15):

```csharp
    public static string Source(string rootNamespace, bool editsInline = false) => $$"""
```

Change the hardcoded override inside the literal (around line 65):

```csharp
    /// <inheritdoc />
    protected override bool EditsInline => {{(editsInline ? "true" : "false")}};
```

- [ ] **Step 4: Thread it through `DesktopViewModelFiles` and `WpfTarget`**

In `AppScaffolding/Targets/DesktopViewModelFiles.cs`, add a trailing parameter to `Build` (line 26-29):

```csharp
    public static List<GeneratedFile> Build(
        string rootNamespace, IReadOnlyList<string> entityNames, string relayCommandSource,
        bool validation = true, bool seed = true, bool login = false,
        EntityRoleMap? roles = null, IReadOnlyList<ClassDefinition>? classes = null,
        bool transactionsEditInline = false)
```

Change the `TransactionScreenViewModel.cs` file entry (line 66-67):

```csharp
            files.Add(new("ViewModels", "TransactionScreenViewModel.cs",
                TransactionScreenTemplate.Source(rootNamespace, transactionsEditInline)));
```

In `AppScaffolding/Targets/WpfTarget.cs`, compute the flag beside `screenRoles` (after line 46) and pass it through the `DesktopViewModelFiles.Build` call (lines 53-56):

```csharp
        var screenRoles = customization.EffectiveLayout == AppLayout.Business
            ? roles ?? Screens.EntityRoleMap.Empty
            : null;

        // Only meaningful with the business shell active, which is exactly when screenRoles is
        // non-null -- a transaction has no embedded editor to route into otherwise, so this stays
        // false whenever screenRoles does.
        var transactionsEditInline =
            screenRoles is not null && customization.EffectiveEntryTemplate == Screens.EntryTemplate.Split;

        // The namespace the emitted code declares; projectName still names the app in its title.
        var ns = customization.RootNamespaceFor(projectName);

        // Shared with WinFormsTarget. The WPF variant of RelayCommand requeries through
        // CommandManager, which is what re-enables Delete when the grid selection changes.
        var files = DesktopViewModelFiles.Build(
            ns, entityNames, ViewModelTemplates.RelayCommand(ns),
            customization.Validation, customization.SampleSeedData, customization.Login,
            screenRoles, classes, transactionsEditInline);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~TransactionScreenTemplateTests` and `dotnet build` for the whole solution (to confirm `WinFormsTarget.cs`'s unchanged call site still compiles against the new optional parameter).
Expected: PASS; build succeeds with no changes needed in `WinFormsTarget.cs`.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/Screens/TransactionScreenTemplate.cs AppScaffolding/Targets/DesktopViewModelFiles.cs AppScaffolding/Targets/WpfTarget.cs CodeGenerator.Tests/App/TransactionScreenTemplateTests.cs
git commit -m "feat(scaffold): a transaction can edit inline, driven by the WPF Split entry template"
```

---

## Task 13: WPF Split transaction template (view layer)

**Files:**
- Modify: `AppScaffolding/Layouts/Wpf/WpfBusinessLayout.cs:50-436` (the `Xaml` method)
- Test: `CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs`

**Interfaces:**
- Consumes: `LayoutContext.Customization.EffectiveEntryTemplate` (Task 2); `RecordEditorViewModel<T>`'s `Draft`, `Lines`, `Totals`, `HasLines`, `HasAdditional` members (already emitted, confirmed in `AppScaffolding/Screens/RecordEditorTemplate.cs`), reached here through `MasterScreenViewModel<T>.Editor` (inherited by `TransactionScreenViewModel<T>`).
- Produces: when the effective template is `Split`, `TransactionTemplate` is replaced by a combined header+form+lines/totals+notes+list view (the same shape `MasterTemplate` already uses for masters), and the standalone `EditorTemplate`/its selector arm are omitted, since `EditsInline: true` (Task 12) means no screen ever raises `EditorRequested`. Every other template keeps today's separate `TransactionTemplate`/`EditorTemplate` pair unchanged.

- [ ] **Step 1: Write the failing tests**

Append to `CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs`:

```csharp
    [TestMethod]
    public void Split_gives_the_transaction_screen_an_embedded_editor()
    {
        var xaml = XamlFor(AppScaffolding.Screens.EntryTemplate.Split);

        var transaction = TemplateSlice("TransactionTemplate", AppScaffolding.Screens.EntryTemplate.Split);

        StringAssert.Contains(transaction, "<views:EntryHeader");
        StringAssert.Contains(transaction, "<views:EntryForm");
        StringAssert.Contains(transaction, "Record=\"{Binding Editor.Draft}\"");
        StringAssert.Contains(transaction, "ItemsSource=\"{Binding Editor.Lines}\"");
        StringAssert.Contains(transaction, "ItemsSource=\"{Binding Editor.Totals}\"");
        StringAssert.Contains(transaction, "<DataGrid");

        // The list is still there, still read-only, still filtered by date -- Split changes
        // composition, not what the list itself offers.
        StringAssert.Contains(transaction, "SelectedDate=\"{Binding From}\"");
    }

    [TestMethod]
    public void Split_removes_the_now_unreachable_standalone_editor()
    {
        var xaml = XamlFor(AppScaffolding.Screens.EntryTemplate.Split);

        Assert.DoesNotContain("x:Key=\"EditorTemplate\"", xaml);
        Assert.DoesNotContain("Editor=\"{StaticResource EditorTemplate}\"", xaml);
    }

    [TestMethod]
    public void Non_split_templates_keep_the_separate_editor()
    {
        var xaml = XamlFor(AppScaffolding.Screens.EntryTemplate.Grouped);

        StringAssert.Contains(xaml, "x:Key=\"EditorTemplate\"");
        StringAssert.Contains(xaml, "Editor=\"{StaticResource EditorTemplate}\"");
    }

    [TestMethod]
    public void Split_xaml_is_still_well_formed()
        => Assert.IsNotNull(System.Xml.Linq.XDocument.Parse(XamlFor(AppScaffolding.Screens.EntryTemplate.Split)).Root);
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~WpfBusinessLayoutTests`
Expected: The four new tests FAIL — today's `TransactionTemplate` is identical regardless of entry template, and `EditorTemplate` is always emitted.

- [ ] **Step 3: Branch the transaction template and the selector**

In `AppScaffolding/Layouts/Wpf/WpfBusinessLayout.cs`'s `Xaml` method, add beside `isRibbon` (from Task 11):

```csharp
        var isSplit = context.Customization.EffectiveEntryTemplate == Screens.EntryTemplate.Split;
```

Replace the `TransactionTemplate` block (lines 194-239) with a computed local built BEFORE the `return $$$"""` line:

```csharp
        var transactionTemplate = isSplit
            ? $$$"""
        <DataTemplate x:Key="TransactionTemplate">
            <DockPanel>
                <views:EntryHeader DockPanel.Dock="Top"
                                   Heading="{Binding Title}"
                                   Record="{Binding Editor.Draft}" />
                <Border DockPanel.Dock="Bottom" Background="{{{railFill}}}" Padding="10,6">
                    <DockPanel>
                        <TextBlock Text="{Binding Status}" Foreground="{{{muted}}}" VerticalAlignment="Center" />
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                            <Button Content="New" Command="{Binding NewCommand}" Width="80" Margin="4,0" />
                            <Button Content="Edit" Command="{Binding EditCommand}" Width="80" Margin="4,0" />
                            <Button Content="Delete" Command="{Binding DeleteCommand}" Width="80" Margin="4,0" />
                            <Button Content="Save" Command="{Binding SaveCommand}" Width="80" Margin="16,0,4,0" />
                            <Button Content="Clear" Command="{Binding ClearCommand}" Width="80" Margin="4,0" />
                            <Button Content="Cancel" Command="{Binding CancelCommand}" Width="80" Margin="4,0" />
                        </StackPanel>
                    </DockPanel>
                </Border>
                <views:EntryForm DockPanel.Dock="Top" Record="{Binding Editor.Draft}" Margin="10" />

                <Border DockPanel.Dock="Top"
                        BorderBrush="{{{rule}}}"
                        BorderThickness="1"
                        Margin="10,0,10,10"
                        Padding="12,8"
                        MaxHeight="180"
                        Visibility="{Binding Editor.HasAdditional, Converter={StaticResource BoolToVisibility}}">
                    <DockPanel>
                        <TextBlock DockPanel.Dock="Top"
                                   Text="{{{additionalTitle}}}"
                                   FontWeight="SemiBold"
                                   Foreground="{{{accentBrush}}}"
                                   Margin="0,0,0,6" />
                        <ScrollViewer VerticalScrollBarVisibility="Auto">
                            <views:EntryNotes Record="{Binding Editor.Draft}" />
                        </ScrollViewer>
                    </DockPanel>
                </Border>

                <Grid DockPanel.Dock="Top"
                      Margin="10,0,10,10"
                      Visibility="{Binding Editor.HasLines, Converter={StaticResource BoolToVisibility}}">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TabControl Grid.Column="0" ItemsSource="{Binding Editor.Lines}" MaxHeight="220">
                        <TabControl.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding Title}" />
                            </DataTemplate>
                        </TabControl.ItemTemplate>
                        <TabControl.ContentTemplate>
                            <DataTemplate>
                                <DockPanel>
                                    <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" Margin="0,6,0,0">
                                        <Button Content="Add Row" Click="OnAddLine" Width="88" />
                                        <Button Content="Delete Row" Click="OnRemoveLine" Width="96" Margin="6,0,0,0" />
                                        <Button Content="Clear All" Click="OnClearLines" Width="88" Margin="6,0,0,0" />
                                    </StackPanel>
                                    <DataGrid ItemsSource="{Binding Items}"
                                              SelectedItem="{Binding Selected}"
                                              AutoGenerateColumns="True"
                                              AutoGeneratingColumn="OnAutoGeneratingColumn"
                                              CellEditEnding="OnLineCellEdited"
                                              CanUserAddRows="False"
                                              HeadersVisibility="Column"
                                              GridLinesVisibility="Horizontal"
                                              BorderBrush="{{{rule}}}" />
                                </DockPanel>
                            </DataTemplate>
                        </TabControl.ContentTemplate>
                    </TabControl>
                    <Border Grid.Column="1"
                            BorderBrush="{{{rule}}}"
                            BorderThickness="1"
                            Margin="12,0,0,0"
                            Padding="12,8"
                            MinWidth="240"
                            VerticalAlignment="Top">
                        <ItemsControl ItemsSource="{Binding Editor.Totals}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <DockPanel Margin="0,3">
                                        <TextBlock Text="{Binding Column}" Foreground="{{{muted}}}" />
                                        <TextBlock Text="{Binding Value, StringFormat=N2}"
                                                   HorizontalAlignment="Right"
                                                   TextAlignment="Right"
                                                   Foreground="{{{text}}}" />
                                    </DockPanel>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </Border>
                </Grid>

                <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="10,0,10,8">
                    <TextBlock Text="From" VerticalAlignment="Center" Foreground="{{{muted}}}" />
                    <DatePicker SelectedDate="{Binding From}" Width="120" Margin="6,0,12,0" />
                    <TextBlock Text="To" VerticalAlignment="Center" Foreground="{{{muted}}}" />
                    <DatePicker SelectedDate="{Binding To}" Width="120" Margin="6,0" />
                    <TextBox Text="{Binding Search, UpdateSourceTrigger=PropertyChanged}"
                             Width="150" Margin="12,0,0,0" />
                </StackPanel>
                <ItemsControl DockPanel.Dock="Top" Margin="10,0,10,8" ItemsSource="{Binding Totals}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <StackPanel Orientation="Horizontal" />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal" Margin="0,0,16,0">
                                <TextBlock Text="{Binding Column}" Foreground="{{{muted}}}" Margin="0,0,6,0" />
                                <TextBlock Text="{Binding Value}" FontWeight="Bold" Foreground="{{{text}}}" />
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <DataGrid Margin="10,0,10,10"
                          ItemsSource="{Binding Items}"
                          SelectedItem="{Binding Selected, Mode=TwoWay}"
                          AutoGenerateColumns="True"
                          IsReadOnly="True"
                          CanUserAddRows="False"
                          AutoGeneratingColumn="OnAutoGeneratingColumn" />
            </DockPanel>
        </DataTemplate>
"""
            : $$$"""
        <DataTemplate x:Key="TransactionTemplate">
            <DockPanel Margin="16">
                <Grid DockPanel.Dock="Top" Margin="0,0,0,10">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="{Binding Title}" FontSize="15" FontWeight="SemiBold" />
                        <TextBlock Text="{Binding Status}" Foreground="{{{muted}}}"
                                   VerticalAlignment="Center" Margin="12,0,0,0" />
                    </StackPanel>
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                        <TextBox Text="{Binding Search, UpdateSourceTrigger=PropertyChanged}"
                                 Width="150" Margin="0,0,8,0" />
                        <Button Content="New" Command="{Binding NewCommand}" Width="70" Margin="4,0" />
                        <Button Content="Open" Command="{Binding OpenCommand}" Width="70" Margin="4,0" />
                        <Button Content="Delete" Command="{Binding DeleteCommand}" Width="70" Margin="4,0" />
                    </StackPanel>
                </Grid>
                <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
                    <TextBlock Text="From" VerticalAlignment="Center" Foreground="{{{muted}}}" />
                    <DatePicker SelectedDate="{Binding From}" Width="120" Margin="6,0,12,0" />
                    <TextBlock Text="To" VerticalAlignment="Center" Foreground="{{{muted}}}" />
                    <DatePicker SelectedDate="{Binding To}" Width="120" Margin="6,0" />
                </StackPanel>
                <ItemsControl DockPanel.Dock="Bottom" ItemsSource="{Binding Totals}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal" Margin="16,8,0,0">
                                <TextBlock Text="{Binding Column}" Foreground="{{{muted}}}" Margin="0,0,6,0" />
                                <TextBlock Text="{Binding Value}" FontWeight="Bold" Foreground="{{{text}}}" />
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <DataGrid ItemsSource="{Binding Items}"
                          SelectedItem="{Binding Selected, Mode=TwoWay}"
                          AutoGenerateColumns="True"
                          IsReadOnly="True"
                          CanUserAddRows="False"
                          MouseDoubleClick="OnRowActivated"
                          AutoGeneratingColumn="OnAutoGeneratingColumn" />
            </DockPanel>
        </DataTemplate>
""";
```

Then, in the main literal:
1. Replace the existing hardcoded `<DataTemplate x:Key="TransactionTemplate">...</DataTemplate>` block (lines 194-239) with `{{{transactionTemplate}}}`.
2. Wrap the existing `<DataTemplate x:Key="EditorTemplate">...</DataTemplate>` block (lines 244-358) and the `Editor="{StaticResource EditorTemplate}"` line inside `ScreenTemplateSelector` (line 362) so both are omitted when `isSplit` — extract that whole block into an `editorTemplate` local (`isSplit ? string.Empty : $$$"""...the existing block..."""`) the same way `transactionTemplate` was just built, and an `editorAssignment` local (`isSplit ? string.Empty : "Editor=\"{StaticResource EditorTemplate}\""`), splicing `{{{editorTemplate}}}` and `{{{editorAssignment}}}` in their place.
3. `ScreenTemplateSelector`'s `Editor` property assignment line in the `<views:ScreenTemplateSelector ...>` element must become conditional too — when `isSplit`, omit the `Editor="..."` attribute entirely (its C# `DataTemplate? Editor { get; set; }` stays null, which is fine: the `ScreenTemplateSelector.cs` code, unchanged, only reaches the `Editor` template for an `IRecordEditor`, and nothing constructs one when `EditsInline` is true — Task 12 guarantees that).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~WpfBusinessLayoutTests`
Expected: PASS, including every pre-existing test in the file — in particular, `All_four_screens_have_a_template`, `The_editor_template_holds_the_lines_in_tabs_with_their_totals`, `The_lines_grid_hides_the_foreign_key` and every other `EditorTemplate`/`TransactionTemplate`-scoped test must still pass for the DEFAULT (`Grouped`) context, since they call the parameterless `Xaml()`/`TemplateSlice(key)` helpers untouched.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Layouts/Wpf/WpfBusinessLayout.cs CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs
git commit -m "feat(wpf): Split gives the transaction screen an embedded editor and list"
```

---

## Task 14: Full-suite regression proof and codebehind compile check

**Files:**
- Test: `CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-13.

- [ ] **Step 1: Write the regression tests**

Append to `CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs`:

```csharp
    [TestMethod]
    public void A_build_naming_no_entry_template_matches_todays_output_byte_for_byte()
    {
        // The regression proof for the whole feature: EffectiveEntryTemplate defaults to Grouped,
        // so Context() (EntryTemplate: null) and Context() with an EXPLICIT Grouped must be
        // byte-identical, and both must equal what this file's pre-existing tests already pin.
        var unset = Xaml();
        var explicitGrouped = XamlFor(AppScaffolding.Screens.EntryTemplate.Grouped);

        Assert.AreEqual(unset, explicitGrouped);
    }

    [TestMethod]
    public void Every_entry_templates_window_and_codebehind_compile_together()
    {
        foreach (var template in Enum.GetValues<AppScaffolding.Screens.EntryTemplate>())
        {
            var context = ContextFor(template);
            var files = new WpfBusinessLayout().ViewFiles(context);

            EmittedDesktopSource.AssertCompilesTogether(
                [
                    .. files.Where(f => f.Name.EndsWith(".cs", StringComparison.Ordinal))
                            .Select(f => (f.Name, f.Source)),
                    ("Views/MainWindow.g.cs", XamlPartialStub),
                    ("Screens/EntityRole.cs", RoleStub),
                    ("Screens/FieldGroups.cs", FieldGroupsTemplate.Source("Payroll")),
                    ("Screens/RequiredFields.cs", RequiredFieldsTemplate.Source("Payroll", null)),
                    ("ViewModels/Shell.cs", ShellStub),
                    ("Auth/Session.cs", SessionStub),
                ]);
        }
    }

    [TestMethod]
    public void Every_entry_templates_xaml_is_well_formed()
    {
        foreach (var template in Enum.GetValues<AppScaffolding.Screens.EntryTemplate>())
            Assert.IsNotNull(System.Xml.Linq.XDocument.Parse(XamlFor(template)).Root);
    }
```

- [ ] **Step 2: Run the whole file's tests**

Run: `dotnet test --filter FullyQualifiedName~WpfBusinessLayoutTests`
Expected: PASS — every test in the file, old and new. If `A_build_naming_no_entry_template_matches_todays_output_byte_for_byte` fails, a template branch's `_ =>` fallback (Task 10/11/13) has diverged from Grouped's original markup; diff `unset` against `explicitGrouped` to find the stray change and fix it before proceeding.

- [ ] **Step 3: Run the full solution build and test suite**

Run: `dotnet build SmartCodeGenerator.slnx -c Release` then `dotnet test SmartCodeGenerator.slnx -c Release --no-build`
Expected: Solution builds clean under Release with `-warnaserror` (per [[ci-command-is-not-the-local-one]] in project memory — this is the command CI actually runs), and every test project passes, not just `CodeGenerator.Tests`.

- [ ] **Step 4: Commit**

```bash
git add CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs
git commit -m "test(wpf): regression proof that an unset entry template still matches Grouped"
```

---

## Task 15: End-to-end verification — generate, build and run

**Files:** none (manual verification against real generated output)

- [ ] **Step 1: Generate one app per template**

For each of the six template keys, run `build app` for a small model (e.g. a Customer master and a matching Order transaction with one line type, enough to exercise both `MasterTemplate` and `TransactionTemplate`/the Split composition) against the `wpf` target, under a short output path (per [[generated-app-probes-need-short-paths]] — the scratchpad exceeds `MAX_PATH`), e.g.:

```
build app: a shop with Customer and Order, Order has OrderLine company "Codis Ltd" layout business entry-template classic wpf
build app: a shop with Customer and Order, Order has OrderLine layout business entry-template grouped wpf
build app: a shop with Customer and Order, Order has OrderLine layout business entry-template tabbed wpf
build app: a shop with Customer and Order, Order has OrderLine layout business entry-template two-column wpf
build app: a shop with Customer and Order, Order has OrderLine layout business entry-template split wpf
build app: a shop with Customer and Order, Order has OrderLine layout business entry-template ribbon wpf
```

- [ ] **Step 2: Build and launch each generated app**

For each: `dotnet build`, then run the executable, sign in (default `admin`/`admin`), and open both a Customer (master) and an Order (transaction) screen. Confirm:
- **Classic:** one flat column of fields, no panel borders or titles.
- **Grouped:** unchanged from today — 3-column grouped panels (compare visually against a `layout business` build from before this branch, e.g. `git stash` and a quick generate on `main`, if in doubt).
- **Tabbed:** field groups appear as tabs (Basic/Address/Contact/Other, plus Additional Information).
- **TwoColumn:** 2-column grouped panels.
- **Split:** the Order screen shows the entry form AND the record list together, with the lines grid and totals visible beneath the form when a row with lines is loaded; there is no separate "editor replaces the list" navigation, and New/Save/Cancel act on the embedded form.
- **Ribbon:** the action bar renders as the icon-and-label strip, and every command (New/Edit/Delete/Save/Clear/Cancel) still works.

- [ ] **Step 3: Confirm the login screen and Users pane are unaffected**

Since login (per [[professional-app-layouts]] design) is orthogonal to this feature, spot-check on at least the Split and Ribbon builds (the two that touch chrome most) that sign-in, sign-out and the Users admin pane still work exactly as before.

- [ ] **Step 4: Record findings**

If any defect is found that a compile-tested/Roslyn-tested/XML-well-formed-tested suite did not catch (expected, per this project's own established pattern — see [[entry-screens-branch]], [[review-edit-properties-branch]] in project memory), fix it, add a regression test that would have caught it if one reasonably could, and re-run Steps 1-2 for the affected template(s) before considering this task done.

- [ ] **Step 5: No commit for this task**

This task produces no code changes of its own beyond whatever fixes Step 4 required (those get their own commit, following the same TDD steps as the task whose code they are fixing).
