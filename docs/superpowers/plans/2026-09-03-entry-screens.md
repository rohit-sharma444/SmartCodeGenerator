# Master and Transaction Entry Screens Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the `business` layout's WPF and WinForms screens match the two supplied mockups — a header band, fields grouped into titled panels across three columns, a tab strip holding the record list or the document lines, an action bar and a status bar.

**Architecture:** The grouping rules ship as a new emitted file, `Screens/FieldGroups.cs`, which reflects over a model type at runtime and is consumed by both desktop targets — so the rules exist exactly once in a generated app. `MasterScreenViewModel<T>` gains an inline `RecordEditorViewModel<T>` so a master's form and list share one screen; a transaction keeps its existing list screen in front of the new entry screen. Neither desktop layout emits per-entity markup: both stay the same length for a model of two entities or twenty.

**Tech Stack:** C# / .NET 10, WPF, Windows Forms, MSTest, Roslyn (`CodeGenerationEngine.Core.Compiler`).

**Spec:** `docs/superpowers/specs/2026-09-03-entry-screens-design.md` — read it before Task 1. It records SEVEN deliberate departures from the mockups, each of which will otherwise look like a bug, and a Known gaps section for what running the apps found and left.

## Global Constraints

- Branch: `entry-screens`, already created off `main` at `6c077c4`. The spec commit `5fd3a5f` is its first commit.
- Build and test exactly as CI does before claiming green:
  `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-restore -warnaserror`
  then `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build`.
  A Debug build of `CodeGenerator` alone hides analyzer errors and runs one of the five test projects.
- **Emitted C# is proved by compiling it**, never by `StringAssert.Contains` alone. Use
  `EmittedSource.AssertCompiles` / `Assembly.Load(Compiler.Compile(source).AssemblyBytes!)` for
  target-neutral code, and `EmittedDesktopSource.AssertCompiles` /
  `EmittedDesktopSource.AssertCompilesTogether` for anything naming a `Window`, `Form` or
  `Control`. Substring assertions have shipped CS0023 and literal-injection defects green here before.
- **Every new assertion is run red before it is run green.** Record the failure message in the commit
  or the step output. An inverted `Assert.DoesNotContain` and an assertion on a name the fixture
  itself declares have both passed vacuously in this repository.
- MSTest analyzer rule MSTEST0037 rejects `Assert.AreEqual(n, collection.Count)`. Use
  `Assert.HasCount(n, collection)`.
- Files written by the Write tool land LF-only in this CRLF checkout. Raw string literals carry the
  source file's line endings, which is how byte-neutrality breaks invisibly — check `git diff` for
  whole-file rewrites before committing.
- Do not touch `WpfLayoutNeutralityTests` or `WinFormsLayoutNeutralityTests`. They pin the four
  pre-business layouts byte for byte and must stay green untouched. If one goes red, the change
  leaked out of the business layout.
- Do not touch the Blazor target, `EntityRole`, `EntityRoles.Classify`, `ScreenPlan`,
  `ScreenOverrides`, `TransactionScreenViewModel`, `PropertyEditorTemplate`, or `WinFormsPaneCode`.
- Generated apps are written to `D:\SampleProjects\<AppName>`, not the shell's working directory.
- Commit after each task. Never `git add -u` — it sweeps in unrelated work.

---

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `AppScaffolding/Screens/FieldGroupsTemplate.cs` | **new** — emits `Screens/FieldGroups.cs`: the displayable pool, the buckets, the key fields, the three-column packing | 1–3 |
| `AppScaffolding/Screens/RequiredFieldsTemplate.cs` | **new** — emits `Screens/RequiredFields.cs`: a class → required-property-names map | 4 |
| `AppScaffolding/Targets/DesktopViewModelFiles.cs` | registers both new emitted files, inside the existing `roles is not null` block | 1, 4 |
| `AppScaffolding/Screens/RecordEditorTemplate.cs` | `IDirtyScreen`; `LineTotal`; `Totals` | 5, 7 |
| `AppScaffolding/Screens/ShellTemplate.cs` | the discard check tests `IDirtyScreen`, not `IRecordEditor` | 5 |
| `AppScaffolding/Screens/MasterScreenTemplate.cs` | inline `Editor`, the six commands, `IDirtyScreen`, no `EditorRequested` | 6 |
| `AppScaffolding/Screens/LineSetTemplate.cs` | `ILineSet.Clear()` | 7 |
| `AppScaffolding/Layouts/Wpf/WpfEntryControls.cs` | **new** — emits `Views/EntryHeader.cs` and `Views/EntryForm.cs` | 8 |
| `AppScaffolding/Layouts/Wpf/WpfBusinessLayout.cs` | master + editor `DataTemplate`s; registers the two new view files; drops `PropertyEditor.cs` | 9 |
| `AppScaffolding/Layouts/WinForms/WinFormsBusinessLayout.cs` | `BuildEntryHeader`, `BuildEntryForm`, rewritten `BuildList` and `BuildEditor` | 10 |

New test files: `CodeGenerator.Tests/App/FieldGroupsTests.cs`, `RequiredFieldsTests.cs`,
`EntryScreenViewModelTests.cs`. Rewritten: `WpfBusinessLayoutTests.cs`,
`WinFormsBusinessLayoutTests.cs`, `MasterScreenTests.cs`, `BusinessViewModelTests.cs`.

---

### Task 1: `FieldGroups` — the displayable pool and the buckets

**Files:**
- Create: `AppScaffolding/Screens/FieldGroupsTemplate.cs`
- Modify: `AppScaffolding/Targets/DesktopViewModelFiles.cs` (inside `if (roles is not null)`)
- Test: `CodeGenerator.Tests/App/FieldGroupsTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `FieldGroupsTemplate.Source(string rootNamespace) → string`, emitting
  `namespace <ns>.Screens;` containing
  `public sealed record FieldGroup(string Title, IReadOnlyList<PropertyInfo> Fields)`,
  `public sealed record KeyFields(PropertyInfo? Code, PropertyInfo? Date, PropertyInfo? Status)`
  and `public static class FieldGroups` with
  `public static PropertyInfo[] Displayable(Type type)`,
  `public static IReadOnlyList<FieldGroup> For(Type type)`,
  `public static IReadOnlyList<PropertyInfo> Additional(Type type)`,
  `public static string LabelOf(PropertyInfo property)` and
  `public static PropertyInfo? NavigationFor(PropertyInfo property)`.
  Tasks 2, 3, 8 and 10 all call these exact names.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/FieldGroupsTests.cs`:

```csharp
using System.Reflection;
using AppScaffolding.Screens;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The bucket rules, compiled and run rather than matched as text.
/// </summary>
/// <remarks>
/// The order of the buckets is the substance, not an implementation detail: ContactAddress is an
/// address and ContactPerson is a contact, so `address` has to be tested before `contact`, and
/// `pincode` before `pin`. A substring assertion over the emitted source could not tell those apart.
/// </remarks>
[TestClass]
public class FieldGroupsTests
{
    /// <summary>A model wide enough to reach every bucket, plus the two shapes that have to be
    /// kept — an enum and a Guid, which the desktop form shows and the web list does not.</summary>
    private const string ModelStub = """
using System;

public class Customer
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal CreditLimit { get; set; }
    public Guid Reference { get; set; }
    public DayOfWeek DeliveryDay { get; set; }

    public string AddressLine1 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string PinCode { get; set; } = string.Empty;
    public string ContactAddress { get; set; } = string.Empty;

    public string ContactPerson { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public string Remarks { get; set; } = string.Empty;
    public bool TermsAccepted { get; set; }

    public List<string> Tags { get; set; } = new();
}
""";

    private static Assembly Emit()
    {
        var source = EmittedSource.Flatten(
            ModelStub,
            FieldGroupsTemplate.Source("Acme").Replace("namespace Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    /// <summary>Title → the field names in it, from the compiled emitted code.</summary>
    private static Dictionary<string, string[]> Groups()
    {
        var assembly = Emit();
        var model = assembly.GetType("Customer")!;
        var groups = (System.Collections.IEnumerable)assembly.GetType("FieldGroups")!
            .GetMethod("For")!.Invoke(null, [model])!;

        var result = new Dictionary<string, string[]>();

        foreach (var group in groups)
        {
            var type = group.GetType();
            var title = (string)type.GetProperty("Title")!.GetValue(group)!;
            var fields = ((IEnumerable<PropertyInfo>)type.GetProperty("Fields")!.GetValue(group)!)
                .Select(p => p.Name)
                .ToArray();

            result[title] = fields;
        }

        return result;
    }

    [TestMethod]
    public void Address_names_land_in_address_information()
        => CollectionAssert.AreEquivalent(
            new[] { "AddressLine1", "City", "PinCode", "ContactAddress" },
            Groups()["Address Information"]);

    /// <summary>The ordering assertion. ContactAddress is an address; ContactPerson is not.</summary>
    [TestMethod]
    public void Contact_is_tested_after_address()
        => CollectionAssert.AreEquivalent(
            new[] { "ContactPerson", "Email" },
            Groups()["Contact Information"]);

    [TestMethod]
    public void Everything_unmatched_stays_in_basic_information()
        => CollectionAssert.AreEquivalent(
            new[] { "Code", "Name", "CreditLimit", "Reference", "DeliveryDay" },
            Groups()["Basic Information"]);

    /// <summary>The desktop form shows enums and Guids; the web list's IsScalar does not. Reusing
    /// the web predicate here would silently drop both.</summary>
    [TestMethod]
    public void An_enum_and_a_guid_reach_a_panel()
    {
        var basic = Groups()["Basic Information"];

        CollectionAssert.Contains(basic, "DeliveryDay");
        CollectionAssert.Contains(basic, "Reference");
    }

    [TestMethod]
    public void Id_and_collections_are_not_shown()
    {
        var all = Groups().Values.SelectMany(f => f).ToList();

        CollectionAssert.DoesNotContain(all, "Id");
        CollectionAssert.DoesNotContain(all, "Tags");
    }

    /// <summary>Only strings, so a TermsAccepted bool stays a checkbox in a panel rather than
    /// becoming a multi-line box in a tab.</summary>
    [TestMethod]
    public void Only_long_text_strings_reach_the_additional_tab()
    {
        var assembly = Emit();
        var additional = ((IEnumerable<PropertyInfo>)assembly.GetType("FieldGroups")!
            .GetMethod("Additional")!.Invoke(null, [assembly.GetType("Customer")!])!)
            .Select(p => p.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { "Remarks" }, additional);
        CollectionAssert.DoesNotContain(
            Groups().Values.SelectMany(f => f).ToList(), "Remarks");
        CollectionAssert.Contains(Groups()["Basic Information"], "TermsAccepted");
    }

    /// <summary>A heading with nothing under it is noise — the rule DashboardViewModel.Group
    /// already follows.</summary>
    [TestMethod]
    public void An_empty_group_is_not_emitted()
    {
        var source = EmittedSource.Flatten(
            "public class Note { public int Id { get; set; } public string Text { get; set; } = string.Empty; }",
            FieldGroupsTemplate.Source("Acme").Replace("namespace Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
        var groups = (System.Collections.IEnumerable)assembly.GetType("FieldGroups")!
            .GetMethod("For")!.Invoke(null, [assembly.GetType("Note")!])!;

        Assert.HasCount(1, groups.Cast<object>().ToList());
    }

    [TestMethod]
    public void Both_desktop_targets_emit_the_file()
    {
        foreach (var target in new AppScaffolding.Targets.ITargetScaffolder[]
                 { new AppScaffolding.Targets.WpfTarget(), new AppScaffolding.Targets.WinFormsTarget() })
        {
            var names = target.FrontEndFiles(
                    "Shop", ["Order"],
                    AppCustomization.Default with { Layout = AppLayout.Business })
                .Select(f => f.Name)
                .ToList();

            CollectionAssert.Contains(names, "FieldGroups.cs", target.Key);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter FieldGroupsTests`
Expected: FAIL — `FieldGroupsTemplate` does not exist (CS0103 / CS0246 at build).

- [ ] **Step 3: Write `FieldGroupsTemplate`**

Create `AppScaffolding/Screens/FieldGroupsTemplate.cs`:

```csharp
namespace AppScaffolding.Screens;

/// <summary>
/// Emits <c>Screens/FieldGroups.cs</c>: which titled panel each of a record's fields belongs in.
/// </summary>
/// <remarks>
/// The rules live once, in the generated application, and both desktop targets reflect over them
/// at run time. Putting them in the generator instead would mean emitting them twice — and
/// <see cref="PropertyEditorTemplate"/> already documents an <c>IsDisplayable</c> convention that
/// exists in three emitted files and has to be hand-kept in sync. Nothing in the generator needs
/// to know the groups at build time, so one copy is reachable here.
/// <para>
/// <c>Displayable</c> is nonetheless a FOURTH copy of that convention, and deliberately not the
/// web target's <c>ScreenFields.Editable</c>: that one's <c>IsScalar</c> admits neither an enum
/// nor a Guid, and reusing it would silently drop both from every desktop form.
/// </para>
/// </remarks>
public static class FieldGroupsTemplate
{
    /// <summary>Source of <c>Screens/FieldGroups.cs</c>.</summary>
    public static string Source(string rootNamespace) => $$"""
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace {{rootNamespace}}.Screens;

/// <summary>One titled panel on an entry screen, and the fields in it.</summary>
public sealed record FieldGroup(string Title, IReadOnlyList<PropertyInfo> Fields);

/// <summary>Which panel each of a record's fields belongs in.</summary>
public static class FieldGroups
{
    public const string Basic = "Basic Information";
    public const string More = "More Information";
    public const string Address = "Address Information";
    public const string Contact = "Contact Information";
    public const string Other = "Other Information";
    public const string AdditionalTitle = "Additional Information";

    /// <summary>The point at which Basic Information stops fitting the panel height and splits.
    /// A layout constant, not a claim about meaning.</summary>
    private const int PanelLimit = 12;

    // Tested in order, first match wins. Address BEFORE contact, so ContactAddress is an address
    // and ContactPerson is not; pincode before pin, so PinCode is not matched by the shorter word
    // inside it.
    private static readonly string[] AddressWords =
    [
        "address", "street", "city", "province", "district", "postal", "pincode", "pin", "zip",
        "country", "region", "state",
    ];

    private static readonly string[] ContactWords =
    [
        "contact", "phone", "mobile", "telephone", "fax", "email", "website", "url", "designation",
    ];

    private static readonly string[] OtherWords = ["status", "active", "since"];

    private static readonly string[] LongTextWords =
    [
        "remark", "note", "terms", "condition", "description",
    ];

    /// <summary>
    /// The fields an entry form shows.
    /// </summary>
    /// <remarks>
    /// Enums and Guids are included, which is where this parts company with the web target's
    /// ScreenFields.Editable. Id is excluded because the store assigns it; navigation properties
    /// and collections because they are not something a user types.
    /// </remarks>
    public static PropertyInfo[] Displayable(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => p.CanRead && p.CanWrite)
            .Where(p => !string.Equals(p.Name, "Id", StringComparison.Ordinal))
            .Where(p => p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false)
            .Where(p => IsDisplayable(p.PropertyType))
            .ToArray();

    /// <summary>The titled panels, in display order. Never contains an empty group.</summary>
    public static IReadOnlyList<FieldGroup> For(Type type)
    {
        var key = Key(type);

        var pool = Displayable(type)
            .Where(p => p != key.Code && p != key.Date && p != key.Status)
            .Where(p => !IsLongText(p))
            .ToList();

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

    /// <summary>The long-text fields, which the view shows as a tab rather than a panel.</summary>
    public static IReadOnlyList<PropertyInfo> Additional(Type type)
        => Displayable(type).Where(IsLongText).ToList();

    /// <summary>
    /// The label a field is shown under.
    /// </summary>
    /// <remarks>
    /// A foreign key is labelled by the entity it points at: SupplierId reads "Supplier", which is
    /// also what decides its bucket -- ShipToLocationId is an address, and the raw key name is not.
    /// </remarks>
    public static string LabelOf(PropertyInfo property)
        => NavigationFor(property)?.Name ?? property.Name;

    /// <summary>
    /// The navigation property a foreign key points at, or null.
    /// </summary>
    /// <remarks>
    /// The same convention MainWindowTemplate's column rule uses: an int &lt;Nav&gt;Id whose
    /// sibling &lt;Nav&gt; is an entity. A form that dropped these would leave no way to set a
    /// document's supplier at all.
    /// </remarks>
    public static PropertyInfo? NavigationFor(PropertyInfo property)
    {
        if (property.PropertyType != typeof(int) || !property.Name.EndsWith("Id", StringComparison.Ordinal))
            return null;

        var nav = property.DeclaringType?
            .GetProperty(property.Name.Substring(0, property.Name.Length - 2));

        return nav is not null && IsEntity(nav.PropertyType) ? nav : null;
    }

    private static bool IsLongText(PropertyInfo property)
        => property.PropertyType == typeof(string)
           && LongTextWords.Any(word => property.Name.ToLowerInvariant().Contains(word));

    private static bool IsDate(PropertyInfo property)
        => (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType)
           == typeof(DateTime);

    private static bool IsDisplayable(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsPrimitive
               || underlying.IsEnum
               || underlying == typeof(string)
               || underlying == typeof(decimal)
               || underlying == typeof(DateTime)
               || underlying == typeof(Guid);
    }

    /// <summary>A generated model: a class in this assembly that is neither a string nor a
    /// collection.</summary>
    private static bool IsEntity(Type type)
        => type.IsClass
           && type != typeof(string)
           && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
}
""";
}
```

Note: `Key` is called here but is written in Task 2. To keep this task's tests green on their own,
add this temporary stub inside the emitted class now, and replace it in Task 2:

```csharp
    /// <summary>Replaced in full by the key-field resolution. Present so For() compiles.</summary>
    public static KeyFields Key(Type type) => new(null, null, null);
```

together with `public sealed record KeyFields(PropertyInfo? Code, PropertyInfo? Date, PropertyInfo? Status);`
declared beside `FieldGroup`.

- [ ] **Step 4: Register the emitted file**

In `AppScaffolding/Targets/DesktopViewModelFiles.cs`, inside the existing `if (roles is not null)`
block, immediately after the `LineSet.cs` line:

```csharp
            // Beside LineSet for the same reason: what the model is, rather than how a window
            // shows it. Both desktop layouts reflect over it; nothing else does.
            files.Add(new("Screens", "FieldGroups.cs", FieldGroupsTemplate.Source(rootNamespace)));
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter FieldGroupsTests`
Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/Screens/FieldGroupsTemplate.cs AppScaffolding/Targets/DesktopViewModelFiles.cs CodeGenerator.Tests/App/FieldGroupsTests.cs
git commit -m "Group a record's fields into the entry form's titled panels"
```

---

### Task 2: `FieldGroups.Key` — the header's fields, held out of the panels

**Files:**
- Modify: `AppScaffolding/Screens/FieldGroupsTemplate.cs` (replace the Task 1 `Key` stub)
- Test: `CodeGenerator.Tests/App/FieldGroupsTests.cs` (append)

**Interfaces:**
- Consumes: `FieldGroups.Displayable`, `FieldGroups.For` from Task 1.
- Produces: `public sealed record KeyFields(PropertyInfo? Code, PropertyInfo? Date, PropertyInfo? Status)`
  and `public static KeyFields Key(Type type)`. Tasks 8 and 10 build the header band from these.

**Why this exists:** `Status` matches the Other Information bucket and a transaction's date matches
it by type, so without this the same property is drawn twice on one screen — once in the header
strip and once in a panel. Two live inputs bound to one property is a defect. The mockups do show
Customer Code and Status in both places; this departs from them deliberately (spec, departure 6).

- [ ] **Step 1: Write the failing tests**

Append to `FieldGroupsTests`:

```csharp
    private static (string? Code, string? Date, string? Status) Key(string modelStub, string typeName)
    {
        var source = EmittedSource.Flatten(
            modelStub,
            FieldGroupsTemplate.Source("Acme").Replace("namespace Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
        var key = assembly.GetType("FieldGroups")!
            .GetMethod("Key")!.Invoke(null, [assembly.GetType(typeName)!])!;

        string? Name(string part)
            => (key.GetType().GetProperty(part)!.GetValue(key) as PropertyInfo)?.Name;

        return (Name("Code"), Name("Date"), Name("Status"));
    }

    private const string OrderStub = """
using System;

public class Order
{
    public int Id { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}
""";

    [TestMethod]
    public void The_header_takes_the_code_the_date_and_the_status()
    {
        var key = Key(OrderStub, "Order");

        Assert.AreEqual("OrderNo", key.Code);
        Assert.AreEqual("OrderDate", key.Date);
        Assert.AreEqual("Status", key.Status);
    }

    /// <summary>The audit fields every model carries are never what a user means by "the date" —
    /// the exclusion TransactionScreenViewModel and ScreenFields both already make.</summary>
    [TestMethod]
    public void The_audit_dates_are_never_the_header_date()
        => Assert.AreNotEqual("CreatedOn", Key(OrderStub, "Order").Date);

    /// <summary>The assertion this whole member exists for.</summary>
    [TestMethod]
    public void A_header_field_appears_in_no_panel()
    {
        var source = EmittedSource.Flatten(
            OrderStub,
            FieldGroupsTemplate.Source("Acme").Replace("namespace Acme.Screens;", string.Empty));

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
        var groups = (System.Collections.IEnumerable)assembly.GetType("FieldGroups")!
            .GetMethod("For")!.Invoke(null, [assembly.GetType("Order")!])!;

        var shown = groups.Cast<object>()
            .SelectMany(g => (IEnumerable<PropertyInfo>)g.GetType().GetProperty("Fields")!.GetValue(g)!)
            .Select(p => p.Name)
            .ToList();

        CollectionAssert.DoesNotContain(shown, "OrderNo");
        CollectionAssert.DoesNotContain(shown, "OrderDate");
        CollectionAssert.DoesNotContain(shown, "Status");
        CollectionAssert.Contains(shown, "Total");
    }

    [TestMethod]
    public void A_model_with_no_code_date_or_status_yields_none()
    {
        var key = Key(
            "public class Note { public int Id { get; set; } public string Text { get; set; } = string.Empty; }",
            "Note");

        Assert.IsNull(key.Code);
        Assert.IsNull(key.Date);
        Assert.IsNull(key.Status);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter FieldGroupsTests`
Expected: the four new tests FAIL — the stub returns `(null, null, null)`, so
`The_header_takes_the_code_the_date_and_the_status` reports `Assert.AreEqual failed. Expected:<OrderNo>. Actual:<(null)>`,
and `A_header_field_appears_in_no_panel` reports `Status` still present.

- [ ] **Step 3: Replace the stub**

In the emitted `FieldGroups` class, replace the Task 1 `Key` stub with:

```csharp
    private static readonly string[] AuditFields = ["CreatedOn", "ModifiedOn"];

    private static readonly string[] CodeSuffixes = ["Code", "No", "Number"];

    /// <summary>
    /// What the header band shows. Excluded from every panel.
    /// </summary>
    /// <remarks>
    /// Without the exclusion the same property is drawn twice on one screen: Status matches the
    /// Other Information bucket, and a transaction's date matches it by type. Two live inputs
    /// bound to one property is a defect rather than a style.
    /// </remarks>
    public static KeyFields Key(Type type)
    {
        var pool = Displayable(type);

        var code = pool.FirstOrDefault(p =>
            p.PropertyType == typeof(string)
            && CodeSuffixes.Any(s => p.Name.EndsWith(s, StringComparison.Ordinal)));

        // The same date the transaction list filters on, and excluded for the same reason: every
        // model carries CreatedOn and ModifiedOn, which are not what a user means by "the date".
        var date = pool.FirstOrDefault(p =>
            IsDate(p) && !AuditFields.Contains(p.Name, StringComparer.OrdinalIgnoreCase));

        var status = pool.FirstOrDefault(p =>
            string.Equals(p.Name, "Status", StringComparison.Ordinal)
            || string.Equals(p.Name, "State", StringComparison.Ordinal));

        return new KeyFields(code, date, status);
    }
```

`Displayable` already excludes the audit dates? It does not — add nothing there; the filter above
is the only place they are excluded, because a panel showing `CreatedOn` is existing behaviour and
is not in this branch's scope to change.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter FieldGroupsTests`
Expected: PASS, 12 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Screens/FieldGroupsTemplate.cs CodeGenerator.Tests/App/FieldGroupsTests.cs
git commit -m "Hold the header's code, date and status out of the panels"
```

---

### Task 3: `FieldGroups.Columns` — the mockups' three columns

**Files:**
- Modify: `AppScaffolding/Screens/FieldGroupsTemplate.cs`
- Test: `CodeGenerator.Tests/App/FieldGroupsTests.cs` (append)

**Interfaces:**
- Consumes: `FieldGroups.For` from Task 1.
- Produces: `public static IReadOnlyList<IReadOnlyList<FieldGroup>> Columns(Type type)` — always
  exactly three lists, any of which may be empty. Tasks 8 and 10 lay the form out from this.

- [ ] **Step 1: Write the failing tests**

Append to `FieldGroupsTests`:

```csharp
    private static List<List<string>> Columns(string modelStub, string typeName)
    {
        var source = EmittedSource.Flatten(
            modelStub,
            FieldGroupsTemplate.Source("Acme").Replace("namespace Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
        var columns = (System.Collections.IEnumerable)assembly.GetType("FieldGroups")!
            .GetMethod("Columns")!.Invoke(null, [assembly.GetType(typeName)!])!;

        return columns.Cast<System.Collections.IEnumerable>()
            .Select(c => c.Cast<object>()
                .Select(g => (string)g.GetType().GetProperty("Title")!.GetValue(g)!)
                .ToList())
            .ToList();
    }

    [TestMethod]
    public void There_are_always_three_columns()
        => Assert.HasCount(
            3,
            Columns("public class Note { public int Id { get; set; } public string Text { get; set; } = string.Empty; }", "Note"));

    /// <summary>The master mockup's shape: Basic alone, Address alone, Contact and Other stacked
    /// in the third column.</summary>
    [TestMethod]
    public void Four_groups_stack_the_smallest_two_together()
    {
        var columns = Columns(ModelStub, "Customer");
        var stacked = columns.Where(c => c.Count > 1).ToList();

        Assert.HasCount(3, columns);
        Assert.HasCount(1, stacked);
        Assert.HasCount(4, columns.SelectMany(c => c).ToList());
    }

    [TestMethod]
    public void One_group_leaves_two_columns_empty()
    {
        var columns = Columns(
            "public class Note { public int Id { get; set; } public string Text { get; set; } = string.Empty; }",
            "Note");

        Assert.HasCount(1, columns.Where(c => c.Count > 0).ToList());
        Assert.HasCount(2, columns.Where(c => c.Count == 0).ToList());
    }

    /// <summary>Past the panel limit Basic splits, and the overflow is titled rather than
    /// silently appended to a panel that no longer fits.</summary>
    [TestMethod]
    public void A_long_basic_group_splits_into_more_information()
    {
        var fields = string.Join(
            "\n", Enumerable.Range(1, 15).Select(i => $"    public string Field{i} {{ get; set; }} = string.Empty;"));

        var titles = Columns($"public class Wide\n{{\n    public int Id {{ get; set; }}\n{fields}\n}}", "Wide")
            .SelectMany(c => c)
            .ToList();

        CollectionAssert.Contains(titles, "Basic Information");
        CollectionAssert.Contains(titles, "More Information");
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter FieldGroupsTests`
Expected: FAIL — `Columns` does not exist, so `GetMethod("Columns")!` throws
`NullReferenceException`.

- [ ] **Step 3: Add `Columns`**

Add to the emitted `FieldGroups` class:

```csharp
    /// <summary>
    /// The panels packed into the three columns the mockups use.
    /// </summary>
    /// <remarks>
    /// Always three lists, so a view can lay out a fixed grid; any of them may be empty, and the
    /// view collapses an empty one so a small model is not two-thirds whitespace. Each group goes
    /// to whichever column holds the fewest fields, ties going left — which reproduces the master
    /// mockup for the four-group case: Basic alone, Address alone, Contact and Other stacked.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<FieldGroup>> Columns(Type type)
    {
        var columns = new List<FieldGroup>[] { new(), new(), new() };
        var weights = new int[3];

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

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter FieldGroupsTests`
Expected: PASS, 16 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Screens/FieldGroupsTemplate.cs CodeGenerator.Tests/App/FieldGroupsTests.cs
git commit -m "Pack the entry form's panels into three columns"
```

---

### Task 4: `RequiredFields` — the asterisk's source of truth

**Files:**
- Create: `AppScaffolding/Screens/RequiredFieldsTemplate.cs`
- Modify: `AppScaffolding/FrontEndContext.cs` (add `Classes`)
- Modify: `AppScaffolding/AppScaffolder.cs:481` (pass the `classes` already in scope)
- Modify: `AppScaffolding/Targets/WpfTarget.cs:26,49` and `Targets/WinFormsTarget.cs:51`
- Modify: `AppScaffolding/Targets/DesktopViewModelFiles.cs`
- Test: `CodeGenerator.Tests/App/RequiredFieldsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `RequiredFieldsTemplate.Source(string rootNamespace, IReadOnlyList<ClassDefinition> classes) → string`,
  emitting `public static class RequiredFields` with
  `public static bool IsRequired(Type type, string property)`. Tasks 8 and 10 call it for the asterisk.

**Why a map and not an attribute:** requiredness lives in `PropertyDefinition.Rules` at generation
time and is compiled into the validator's body — it is not on the model, so a running application
cannot see it by reflection. Putting `[Required]` on generated model properties would work but
changes every generated model file, which is beyond this branch.

**The classes are not currently in reach and have to be threaded.** `FrontEndContext` carries
`ProjectName`, `EntityNames`, `Customization` and `Roles` — entity *names* cannot say which
properties are required. `AppScaffolder` does hold the `ClassDefinition`s: the local `classes` is
in scope eight lines above where it builds the context (`AppScaffolder.cs:473` and `:481`). Add a
member to the record, which is the extension point its own remarks describe — "rather than widen
that signature for the fourth time, the arguments become a record".

Emit an empty map when the list is null or the build has no validation, so the file always exists.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/RequiredFieldsTests.cs`:

```csharp
using System.Reflection;
using AppScaffolding.Screens;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The red asterisk and the generated validator must never disagree, so both read the same
/// PropertyDefinition.Rules scan.
/// </summary>
[TestClass]
public class RequiredFieldsTests
{
    private static ClassDefinition Customer()
    {
        var required = new PropertyDefinition { Name = "Name", Type = "string" };
        required.Rules.Add(new PropertyRule { Name = "required" });

        var optional = new PropertyDefinition { Name = "Remarks", Type = "string" };

        return new ClassDefinition
        {
            Name = "Customer",
            Properties = { required, optional },
        };
    }

    private static Assembly Emit(params ClassDefinition[] classes)
    {
        var source = EmittedSource.Flatten(
            "public class Customer { public string Name { get; set; } = string.Empty; public string Remarks { get; set; } = string.Empty; }",
            RequiredFieldsTemplate.Source("Acme", classes)
                .Replace("namespace Acme.Screens;", string.Empty));

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    private static bool IsRequired(Assembly assembly, string property)
        => (bool)assembly.GetType("RequiredFields")!
            .GetMethod("IsRequired")!
            .Invoke(null, [assembly.GetType("Customer")!, property])!;

    [TestMethod]
    public void A_required_property_is_reported_required()
        => Assert.IsTrue(IsRequired(Emit(Customer()), "Name"));

    [TestMethod]
    public void An_optional_property_is_not()
        => Assert.IsFalse(IsRequired(Emit(Customer()), "Remarks"));

    [TestMethod]
    public void An_unknown_property_is_not()
        => Assert.IsFalse(IsRequired(Emit(Customer()), "Nonexistent"));

    /// <summary>Always emitted, empty when the build has no validation — so no consumer has to
    /// strike the conditional-fragment bargain RecordEditorTemplate does.</summary>
    [TestMethod]
    public void An_empty_map_compiles_and_answers_false()
        => Assert.IsFalse(IsRequired(Emit(), "Name"));

    [TestMethod]
    public void Both_desktop_targets_emit_the_file()
    {
        foreach (var target in new AppScaffolding.Targets.ITargetScaffolder[]
                 { new AppScaffolding.Targets.WpfTarget(), new AppScaffolding.Targets.WinFormsTarget() })
        {
            var names = target.FrontEndFiles(
                    "Shop", ["Order"],
                    AppCustomization.Default with { Layout = AppLayout.Business })
                .Select(f => f.Name)
                .ToList();

            CollectionAssert.Contains(names, "RequiredFields.cs", target.Key);
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter RequiredFieldsTests`
Expected: FAIL — `RequiredFieldsTemplate` does not exist.

- [ ] **Step 3: Write `RequiredFieldsTemplate`**

Create `AppScaffolding/Screens/RequiredFieldsTemplate.cs`:

```csharp
using CodeGenerationEngine.Models;

namespace AppScaffolding.Screens;

/// <summary>
/// Emits <c>Screens/RequiredFields.cs</c>: which properties an entry form marks with an asterisk.
/// </summary>
/// <remarks>
/// Requiredness lives in <see cref="PropertyDefinition.Rules"/> and is compiled into the generated
/// validator's body, not onto the model — so a running application cannot see it by reflection and
/// the form needs its own map. Written from the same rule scan <see cref="ValidatorTemplate"/>
/// performs, so the asterisk and the validator cannot disagree.
/// <para>
/// Always emitted, empty when the build has no validation. Emitting it conditionally would push
/// every consumer into the conditional-fragment bargain <see cref="RecordEditorTemplate"/> already
/// strikes, in two more places, for no gain.
/// </para>
/// </remarks>
public static class RequiredFieldsTemplate
{
    private const string RequiredRule = "required";

    /// <summary>Source of <c>Screens/RequiredFields.cs</c>.</summary>
    public static string Source(string rootNamespace, IReadOnlyList<ClassDefinition>? classes)
    {
        var entries = (classes ?? [])
            .Select(c => (c.Name, Required: c.Properties
                .Where(p => p.Rules.Any(r =>
                    string.Equals(r.Name, RequiredRule, StringComparison.OrdinalIgnoreCase)))
                .Select(p => p.Name)
                .ToList()))
            .Where(x => x.Required.Count > 0)
            .Select(x =>
                $"        [\"{x.Name}\"] = [{string.Join(", ", x.Required.Select(n => $"\"{n}\""))}],")
            .ToList();

        var map = entries.Count == 0
            ? string.Empty
            : "\r\n" + string.Join("\r\n", entries) + "\r\n    ";

        return $$"""
using System;
using System.Collections.Generic;
using System.Linq;

namespace {{rootNamespace}}.Screens;

/// <summary>Which properties an entry form marks required.</summary>
public static class RequiredFields
{
    private static readonly Dictionary<string, string[]> Map = new(StringComparer.Ordinal)
    {{{map}}};

    /// <summary>Whether <paramref name="property"/> on <paramref name="type"/> is required.
    /// False for a type the map does not hold, which is every type in a build with no
    /// validation.</summary>
    public static bool IsRequired(Type type, string property)
        => Map.TryGetValue(type.Name, out var names)
           && names.Contains(property, StringComparer.Ordinal);
}
""";
    }
}
```

The `{{{map}}}` renders as `{` + entries + `}`: the outer braces come from the `{{{`/`}}}`
escaping in a `$$` raw literal, so the emitted initializer is `new(...) { ... };` with the entries
inside. Verify by eye in the first test failure message if the compile fails.

- [ ] **Step 4: Thread the classes through and register the file**

**4a.** In `AppScaffolding/FrontEndContext.cs`, add the member and a doc line:

```csharp
public sealed record FrontEndContext(
    string ProjectName,
    IReadOnlyList<string> EntityNames,
    AppCustomization Customization,
    EntityRoleMap Roles,
    /// <summary>The mapped classes, for the front end that has to know which properties are
    /// required. Empty for a caller that has only names.</summary>
    IReadOnlyList<CodeGenerationEngine.Models.ClassDefinition>? Classes = null);
```

**4b.** In `AppScaffolding/AppScaffolder.cs`, at the `new FrontEndContext(...)` on line 481, pass
the `classes` local already in scope:

```csharp
        foreach (var file in target.FrontEndFiles(
                     new FrontEndContext(projectName, entityNames, customization, roles, classes)))
```

**4c.** In `AppScaffolding/Targets/WpfTarget.cs`, widen the private overload and forward:

```csharp
    public IReadOnlyList<GeneratedFile> FrontEndFiles(FrontEndContext context)
        => FrontEndFiles(
            context.ProjectName, context.EntityNames, context.Customization,
            context.Roles, context.Classes);

    public IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization)
        => FrontEndFiles(projectName, entityNames, customization, roles: null, classes: null);

    private IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization,
        Screens.EntityRoleMap? roles, IReadOnlyList<ClassDefinition>? classes)
```

and pass `classes` to `DesktopViewModelFiles.Build`. Make the identical change in
`Targets/WinFormsTarget.cs`, whose `Build` call is at line 51.

**4d.** In `AppScaffolding/Targets/DesktopViewModelFiles.cs`, add the parameter:

```csharp
    public static List<GeneratedFile> Build(
        string rootNamespace, IReadOnlyList<string> entityNames, string relayCommandSource,
        bool validation = true, bool seed = true, bool login = false,
        EntityRoleMap? roles = null, IReadOnlyList<ClassDefinition>? classes = null)
```

and inside `if (roles is not null)`, after the `FieldGroups.cs` line:

```csharp
            files.Add(new("Screens", "RequiredFields.cs",
                RequiredFieldsTemplate.Source(rootNamespace, validation ? classes : null)));
```

`DesktopViewModelFilesTests` calls `Build` in four places without the new argument; the default
keeps them compiling and they must stay green.

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter RequiredFieldsTests`
Expected: PASS, 5 tests.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/Screens/RequiredFieldsTemplate.cs AppScaffolding/Targets/ CodeGenerator.Tests/App/RequiredFieldsTests.cs
git commit -m "Emit which properties an entry form marks required"
```

---

### Task 5: `IDirtyScreen` — so a dirty inline editor cannot be navigated away from silently

**Files:**
- Modify: `AppScaffolding/Screens/RecordEditorTemplate.cs`
- Modify: `AppScaffolding/Screens/ShellTemplate.cs`
- Test: `CodeGenerator.Tests/App/EntryScreenViewModelTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces: `public interface IDirtyScreen { bool IsDirty { get; } }` in the emitted
  `ViewModels/RecordEditorViewModel.cs`, with `IRecordEditor : IDirtyScreen`. Task 6 makes
  `MasterScreenViewModel<T>` implement it.

**Do not** make `MasterScreenViewModel<T>` implement `IRecordEditor` instead. It would compile and
would pass the dirty check — and would silently break rendering, because `ScreenTemplateSelector`
tests `IRecordEditor` **before** `IEntityViewModel`, so every master screen in the application
would draw as the editor template. `Screens/ScreenTemplates.cs` therefore needs no change in this
plan; if it turns out to, the interface split went wrong.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/EntryScreenViewModelTests.cs`:

```csharp
using AppScaffolding.Screens;

namespace CodeGenerator.Tests.App;

/// <summary>The view-model half of the entry screens.</summary>
[TestClass]
public class EntryScreenViewModelTests
{
    [TestMethod]
    public void The_editor_interface_extends_a_dirty_screen()
    {
        var source = RecordEditorTemplate.Source("Acme", validation: false);

        StringAssert.Contains(source, "public interface IDirtyScreen");
        StringAssert.Contains(source, "public interface IRecordEditor : IDirtyScreen");
    }

    /// <summary>
    /// The shell must not test IRecordEditor for dirtiness.
    /// </summary>
    /// <remarks>
    /// A master screen holds its editor inline and is deliberately not an IRecordEditor, so a
    /// check against that interface would let a dirty master be navigated away from without the
    /// discard prompt firing — in all three places the shell asks.
    /// </remarks>
    [TestMethod]
    public void The_shell_asks_a_dirty_screen_not_a_record_editor()
    {
        var source = ShellTemplate.Shell("Acme");

        Assert.DoesNotContain("_current is IRecordEditor { IsDirty: true }", source);
        Assert.AreEqual(
            2,
            source.Split("_current is IDirtyScreen { IsDirty: true }").Length - 1,
            "TryShow and SignOut both ask");
    }

    /// <summary>Leaving an editor still drops the history, and that test is on IRecordEditor —
    /// a master screen is not an editor and Back must not be disabled over it.</summary>
    [TestMethod]
    public void Leaving_an_editor_still_drops_the_history()
        => StringAssert.Contains(
            ShellTemplate.Shell("Acme"), "_previous = _current is IRecordEditor ? null : _current;");
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter EntryScreenViewModelTests`
Expected: FAIL — `IDirtyScreen` is not in the emitted source.

- [ ] **Step 3: Add the interface and change the shell**

In `RecordEditorTemplate.Source`, above the existing `IRecordEditor` declaration:

```csharp
/// <summary>
/// A screen that can be left with unsaved changes.
/// </summary>
/// <remarks>
/// Separate from IRecordEditor because a master screen holds its editor INLINE: it is dirty in
/// exactly the same way, and it is deliberately not an IRecordEditor — ScreenTemplateSelector
/// tests that interface before IEntityViewModel, so a master implementing it would draw as the
/// editor template on every screen in the application.
/// </remarks>
public interface IDirtyScreen
{
    /// <summary>Whether anything has been typed since the screen was opened or last saved.</summary>
    bool IsDirty { get; }
}
```

Change `public interface IRecordEditor` to `public interface IRecordEditor : IDirtyScreen`, and
delete the now-inherited `bool IsDirty { get; }` member from it (keep its doc comment on
`IDirtyScreen`).

In `ShellTemplate.Shell`, replace both occurrences of
`_current is IRecordEditor { IsDirty: true }` with `_current is IDirtyScreen { IsDirty: true }`
(in `TryShow` and in `SignOut`). Leave the `_previous = _current is IRecordEditor ? null : _current;`
line alone — history is about editors, not about dirtiness.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter EntryScreenViewModelTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Screens/RecordEditorTemplate.cs AppScaffolding/Screens/ShellTemplate.cs CodeGenerator.Tests/App/EntryScreenViewModelTests.cs
git commit -m "Ask any dirty screen before leaving it, not only a record editor"
```

---

### Task 6: `MasterScreenViewModel<T>` — the inline editor

**Files:**
- Modify: `AppScaffolding/Screens/MasterScreenTemplate.cs`
- Test: `CodeGenerator.Tests/App/MasterScreenTests.cs` (rewrite the edit-path tests)

**Interfaces:**
- Consumes: `IDirtyScreen` (Task 5), `RecordEditorViewModel<T>` (existing).
- Produces: on `MasterScreenViewModel<T>` —
  `RecordEditorViewModel<T> Editor { get; }` (never null),
  `ICommand NewCommand`, `EditCommand`, `ClearCommand`, `CancelCommand`, `SaveCommand`,
  `DeleteCommand`; `bool IsDirty`; `bool HasAdditional`. `OpenCommand` and the `EditorRequested`
  event are **removed**. Tasks 9 and 10 bind these names.

`TransactionScreenViewModel<T>` derives from this class and is not otherwise changed; it inherits
the inline editor but its screen is still reached through the transaction list, so the transaction
templates bind `Items`/`From`/`To`/`Totals` as they do today.

- [ ] **Step 1: Write the failing tests**

`MasterScreenTests.Emit()` already flattens and compiles the emitted screen. Append:

```csharp
    private static object EditorOf(object screen)
        => screen.GetType().GetProperty("Editor")!.GetValue(screen)!;

    private static void Run(object owner, string command)
    {
        var value = owner.GetType().GetProperty(command)!.GetValue(owner)!;
        value.GetType().GetMethod("Execute")!.Invoke(value, [null]);
    }

    [TestMethod]
    public void The_screen_opens_on_a_blank_editor()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel", "Dune");
        var editor = EditorOf(screen);

        Assert.IsTrue((bool)editor.GetType().GetProperty("IsNew")!.GetValue(editor)!);
    }

    [TestMethod]
    public void Selecting_a_row_loads_it_into_the_editor()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "MasterScreenViewModel", "Dune");
        var row = ((IEnumerable)screen.GetType().GetProperty("Items")!.GetValue(screen)!)
            .Cast<object>().Single();

        screen.GetType().GetProperty("Selected")!.SetValue(screen, row);

        var draft = EditorOf(screen).GetType().GetProperty("Draft")!.GetValue(EditorOf(screen))!;

        Assert.AreEqual("Dune", draft.GetType().GetProperty("Title")!.GetValue(draft));
        Assert.IsFalse((bool)EditorOf(screen).GetType().GetProperty("IsNew")!.GetValue(EditorOf(screen))!);
    }

    /// <summary>A fresh instance, not a mutated one: the draft is a clone, and reusing the
    /// instance would leave the previous record's snapshot in place and make IsDirty lie.</summary>
    [TestMethod]
    public void Selecting_a_second_row_replaces_the_editor()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "MasterScreenViewModel", "Dune", "Emma");
        var rows = ((IEnumerable)screen.GetType().GetProperty("Items")!.GetValue(screen)!)
            .Cast<object>().ToList();

        screen.GetType().GetProperty("Selected")!.SetValue(screen, rows[0]);
        var first = EditorOf(screen);

        screen.GetType().GetProperty("Selected")!.SetValue(screen, rows[1]);

        Assert.AreNotSame(first, EditorOf(screen));
    }

    [TestMethod]
    public void New_blanks_the_editor()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "MasterScreenViewModel", "Dune");
        var row = ((IEnumerable)screen.GetType().GetProperty("Items")!.GetValue(screen)!)
            .Cast<object>().Single();

        screen.GetType().GetProperty("Selected")!.SetValue(screen, row);
        Run(screen, "NewCommand");

        Assert.IsTrue((bool)EditorOf(screen).GetType().GetProperty("IsNew")!.GetValue(EditorOf(screen))!);
    }

    [TestMethod]
    public void Saving_writes_the_editor_and_reloads_the_list()
    {
        var assembly = Emit();
        var screen = Screen(assembly, "MasterScreenViewModel", "Dune");

        var draft = EditorOf(screen).GetType().GetProperty("Draft")!.GetValue(EditorOf(screen))!;
        draft.GetType().GetProperty("Title")!.SetValue(draft, "New Book");

        Run(screen, "SaveCommand");

        var titles = ((IEnumerable)screen.GetType().GetProperty("Items")!.GetValue(screen)!)
            .Cast<object>()
            .Select(r => (string)r.GetType().GetProperty("Title")!.GetValue(r)!)
            .ToList();

        CollectionAssert.Contains(titles, "New Book");
    }

    [TestMethod]
    public void The_screen_is_dirty_when_its_inline_editor_is()
    {
        var screen = Screen(Emit(), "MasterScreenViewModel", "Dune");
        var isDirty = screen.GetType().GetProperty("IsDirty")!;

        Assert.IsFalse((bool)isDirty.GetValue(screen)!);

        var draft = EditorOf(screen).GetType().GetProperty("Draft")!.GetValue(EditorOf(screen))!;
        draft.GetType().GetProperty("Title")!.SetValue(draft, "typed");

        Assert.IsTrue((bool)isDirty.GetValue(screen)!);
    }

    /// <summary>
    /// The master no longer asks the shell for an editor.
    /// </summary>
    /// <remarks>
    /// ShellViewModel.Route looks the event up by reflection and returns early when a screen does
    /// not have it, so its absence needs no shell change — and the transaction path that still
    /// raises it is untouched.
    /// </remarks>
    [TestMethod]
    public void The_master_screen_no_longer_requests_an_editor()
    {
        var source = MasterScreenTemplate.Source("Acme");

        Assert.DoesNotContain("EditorRequested", source);
        Assert.DoesNotContain("OpenCommand", source);
    }
```

Delete the existing tests in `MasterScreenTests` that assert `EditorRequested` fires and that
`OpenCommand` exists — they pin behaviour this task removes.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter MasterScreenTests`
Expected: FAIL — `GetProperty("Editor")` returns null, `NullReferenceException`.

- [ ] **Step 3: Rewrite the emitted screen**

In `MasterScreenTemplate.Source`, change the class declaration to:

```csharp
public class MasterScreenViewModel<T> : ObservableObject, IEntityViewModel, IDirtyScreen where T : class, new()
```

Replace the constructor's command wiring and remove the event:

```csharp
    public MasterScreenViewModel(string title)
    {
        Title = title;
        Records = new ObservableCollection<T>(_store.GetAll());

        NewCommand = new RelayCommand(New);
        EditCommand = new RelayCommand(Edit, () => _selected is T);
        ClearCommand = new RelayCommand(New);
        CancelCommand = new RelayCommand(Cancel);
        DeleteCommand = new RelayCommand(Delete, () => _selected is T);
        SaveCommand = new RelayCommand(Save);

        _editor = NewEditor(null, isNew: true);
    }
```

Add the editor and its members:

```csharp
    private RecordEditorViewModel<T> _editor;

    /// <summary>
    /// The form at the top of the screen. Never null: the form is usable the moment the screen
    /// opens, without anything having been selected.
    /// </summary>
    public RecordEditorViewModel<T> Editor
    {
        get => _editor;
        private set => SetField(ref _editor, value);
    }

    /// <inheritdoc />
    public bool IsDirty => Editor.IsDirty;

    /// <summary>Whether this record has long-text fields, and so an Additional Information tab.
    /// A view binds it so a model with none does not show an empty tab — the same rule that
    /// drops an empty panel.</summary>
    public bool HasAdditional => FieldGroups.Additional(typeof(T)).Count > 0;

    /// <summary>A FRESH editor, never a mutated one: the draft is a clone, and reusing the
    /// instance would leave the previous record's snapshot in place and make IsDirty lie.</summary>
    private RecordEditorViewModel<T> NewEditor(T? record, bool isNew)
        => new(record, isNew, LineSets());

    private void New() => Editor = NewEditor(null, isNew: true);

    private void Edit()
    {
        if (Selected is T record)
            Editor = NewEditor(record, isNew: false);
    }

    private void Cancel()
    {
        Selected = null;
        New();
    }

    /// <summary>Saves the form and re-reads the list under it. Save on this screen means the
    /// record, not the list — the list has nothing of its own to write.</summary>
    private void Save()
    {
        var save = Editor.SaveCommand;

        if (save.CanExecute(null))
            save.Execute(null);

        Status = Editor.Status;
        Reload();
    }
```

Change `Selected`'s setter so selecting a row loads it:

```csharp
    public object? Selected
    {
        get => _selected;
        set
        {
            if (!SetField(ref _selected, value))
                return;

            // Selecting a row IS opening it: the form and the list are one screen.
            if (value is T record)
                Editor = NewEditor(record, isNew: false);
        }
    }
```

Declare the commands, replacing `OpenCommand`:

```csharp
    public ICommand NewCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SaveCommand { get; }
```

Delete the `EditorRequested` event, the `Open` method, and the `New`/`Open` bodies that raised it.
Update the `SaveCommand` doc comment — it now saves the inline editor rather than reloading. Keep
`AddCommand => NewCommand`, `Reload`, `Matching`, `LineSets`, `Delete` and `IdOf` as they are.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "MasterScreenTests|TransactionScreenTests|EntryScreenViewModelTests"`
Expected: PASS. `TransactionScreenTests` must stay green untouched — it derives from this class.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Screens/MasterScreenTemplate.cs CodeGenerator.Tests/App/MasterScreenTests.cs
git commit -m "Put a master's form and list on one screen"
```

---

### Task 7: `Totals` on the editor and `Clear` on a line set

**Files:**
- Modify: `AppScaffolding/Screens/RecordEditorTemplate.cs`
- Modify: `AppScaffolding/Screens/LineSetTemplate.cs`
- Test: `CodeGenerator.Tests/App/EntryScreenViewModelTests.cs` (append),
  `CodeGenerator.Tests/App/ScreensLineTests.cs` (append)

**Interfaces:**
- Consumes: `ILineSet` (existing).
- Produces: `public sealed record LineTotal(string Column, decimal Value, bool IsGrand)`,
  `public IReadOnlyList<LineTotal> Totals { get; }` and
  `public bool HasAdditional => FieldGroups.Additional(typeof(T)).Count > 0;` on
  `RecordEditorViewModel<T>`; `void Clear()` on `ILineSet` and `LineSet<TLine>`.
  Tasks 9 and 10 bind all four.

**Why not the mockup's rows:** the mockup shows Sub Total / Discount / Tax Amount / Freight /
Grand Total. The generator cannot know which column is which, and
`TransactionScreenViewModel.MoneyColumns` already states the reasoning it follows — several honest
numbers beat one confidently wrong one. `IsGrand` is true only when a set has exactly one money
column, in which case that row takes the emphasised treatment.

- [ ] **Step 1: Write the failing tests**

Append to `EntryScreenViewModelTests` (the fixture types come from `RecordEditorTests.Emit`, which
already flattens `LineSetTemplate` alongside the editor):

```csharp
    [TestMethod]
    public void An_editor_totals_each_money_column_of_its_lines()
    {
        var source = RecordEditorTemplate.Source("Acme", validation: false);

        StringAssert.Contains(source, "public sealed record LineTotal(string Column, decimal Value, bool IsGrand)");
        StringAssert.Contains(source, "public IReadOnlyList<LineTotal> Totals");
    }

    /// <summary>The same exclusion the transaction list makes: an Id is not money.</summary>
    [TestMethod]
    public void The_totals_exclude_the_id_and_the_foreign_key()
    {
        var source = RecordEditorTemplate.Source("Acme", validation: false);

        StringAssert.Contains(source, "!p.Name.EndsWith(\"Id\", StringComparison.Ordinal)");
    }
```

Append to `ScreensLineTests`:

```csharp
    [TestMethod]
    public void Clearing_a_line_set_empties_it_and_marks_it_dirty()
    {
        var source = LineSetTemplate.Source("Acme");

        StringAssert.Contains(source, "void Clear();");
        StringAssert.Contains(source, "public void Clear()");
    }
```

Then the behavioural test, which is the one that matters. `ScreensLineTests` already has a
compiled fixture; reuse its helpers rather than writing a second one. Read the file first and
adjust these two names to whatever it actually calls them:

```csharp
    /// <summary>Clearing empties the set and leaves it dirty, so Cancel can still put the lines
    /// back — nothing has reached the store.</summary>
    [TestMethod]
    public void Clearing_empties_a_loaded_set_and_marks_it_dirty()
    {
        var assembly = Emit();
        var set = Set(assembly, parentId: 1);      // a set with lines already loaded

        set.GetType().GetMethod("Clear")!.Invoke(set, null);

        var items = ((IEnumerable)set.GetType().GetProperty("Items")!.GetValue(set)!).Cast<object>();

        Assert.IsEmpty(items.ToList());
        Assert.IsTrue((bool)set.GetType().GetProperty("IsDirty")!.GetValue(set)!);
    }

    /// <summary>Nothing is written until the editor saves — the same bargain Add and Remove
    /// already make.</summary>
    [TestMethod]
    public void Clearing_writes_nothing_to_the_store()
    {
        var assembly = Emit();
        var set = Set(assembly, parentId: 1);
        var before = Rows(assembly).Count;

        set.GetType().GetMethod("Clear")!.Invoke(set, null);

        Assert.HasCount(before, Rows(assembly));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "EntryScreenViewModelTests|ScreensLineTests"`
Expected: FAIL — neither `LineTotal` nor `Clear` exists.

- [ ] **Step 3: Add `Clear` to the line set**

In `LineSetTemplate.Source`, on the `ILineSet` interface, after `Remove`:

```csharp
    /// <summary>Removes every line. Nothing reaches the store until the editor saves, so this is
    /// undone by Cancel like any other line edit.</summary>
    void Clear();
```

and on `LineSet<TLine>`:

```csharp
    public void Clear()
    {
        _lines.Clear();
        Selected = null;
    }
```

`IsDirty` already compares a snapshot against `_openedAs`, so clearing a loaded set makes it dirty
with no further change.

- [ ] **Step 4: Add `Totals` to the editor**

In `RecordEditorTemplate.Source`, beside the `IRecordEditor` declaration:

```csharp
/// <summary>One money column of a document's lines, summed over them.</summary>
public sealed record LineTotal(string Column, decimal Value, bool IsGrand);
```

and on `RecordEditorViewModel<T>`:

```csharp
    /// <summary>
    /// One row per money column of the lines, summed across them.
    /// </summary>
    /// <remarks>
    /// Every decimal and double that is neither an Id nor a foreign key, rather than guessing
    /// which single column is the document's total — the rule TransactionScreenViewModel already
    /// applies to a list, for the reason written there: a generated application showing one
    /// confidently wrong number is worse than one showing several honest ones.
    /// <para>
    /// IsGrand only when a set has exactly one money column, which is the only case where naming
    /// a grand total is not a guess.
    /// </para>
    /// </remarks>
    public IReadOnlyList<LineTotal> Totals
    {
        get
        {
            var totals = new List<LineTotal>();

            foreach (var set in Lines)
            {
                var columns = set.LineType.GetProperties()
                    .Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(double))
                    .Where(p => !string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase)
                                && !p.Name.EndsWith("Id", StringComparison.Ordinal))
                    .ToList();

                foreach (var column in columns)
                {
                    var sum = set.Items.Cast<object>().Sum(line => ToDecimal(column.GetValue(line)));

                    totals.Add(new LineTotal(column.Name, sum, columns.Count == 1));
                }
            }

            return totals;
        }
    }

    private static decimal ToDecimal(object? value)
        => value switch
        {
            decimal d => d,
            double d => (decimal)d,
            _ => 0m,
        };

    /// <summary>Whether this record has long-text fields, and so an Additional Information tab.
    /// A view binds it so a model with none does not show an empty tab.</summary>
    public bool HasAdditional => FieldGroups.Additional(typeof(T)).Count > 0;
```

Add `using System.Collections;` to the emitted file's using block if `Cast<object>()` needs it —
`System.Linq` is already there. `{{rootNamespace}}.Screens` is already imported, which is where
`FieldGroups` lives.

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "EntryScreenViewModelTests|ScreensLineTests|RecordEditorTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/Screens/RecordEditorTemplate.cs AppScaffolding/Screens/LineSetTemplate.cs CodeGenerator.Tests/App/
git commit -m "Total a document's lines and let a line set be cleared"
```

---

### Task 8: WPF — `EntryHeader` and `EntryForm`

**Files:**
- Create: `AppScaffolding/Layouts/Wpf/WpfEntryControls.cs`
- Test: `CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs` (append)

**Interfaces:**
- Consumes: `FieldGroups.Columns`, `FieldGroups.Key`, `FieldGroups.LabelOf`,
  `FieldGroups.NavigationFor` (Tasks 1–3); `RequiredFields.IsRequired` (Task 4).
- Produces: `WpfEntryControls.Header(LayoutContext) → string` emitting `Views/EntryHeader.cs`,
  `WpfEntryControls.Form(LayoutContext) → string` emitting `Views/EntryForm.cs`, and
  `WpfEntryControls.Notes(LayoutContext) → string` emitting `Views/EntryNotes.cs`. All three are
  `Grid` subclasses taking a `Record` dependency property. Task 9 registers and binds them.

Both derive from `Grid` and take the record through a dependency property, the same pattern and for
the same documented reason as `PropertyEditor`: a `ContentControl` cannot work, because its single
`Content` slot would hold the record instead of the generated form, so the form would never reach
the visual tree.

- [ ] **Step 1: Write the failing tests**

Append to `WpfBusinessLayoutTests`:

```csharp
    [TestMethod]
    public void The_layout_emits_the_two_entry_controls()
    {
        var names = new WpfBusinessLayout().ViewFiles(Context()).Select(f => f.Name).ToList();

        CollectionAssert.Contains(names, "EntryHeader.cs");
        CollectionAssert.Contains(names, "EntryForm.cs");
    }

    /// <summary>The business shell no longer needs the master-detail layout's flat form. That
    /// layout still emits it; this one must stop, or the app carries a file nothing binds.</summary>
    [TestMethod]
    public void The_business_layout_no_longer_emits_the_flat_property_editor()
        => CollectionAssert.DoesNotContain(
            new WpfBusinessLayout().ViewFiles(Context()).Select(f => f.Name).ToList(),
            PropertyEditorTemplate.FileName);

    [TestMethod]
    public void The_entry_controls_compile()
    {
        var context = Context();

        EmittedDesktopSource.AssertCompilesTogether(
            ("Screens/FieldGroups.cs", FieldGroupsTemplate.Source("Payroll")),
            ("Screens/RequiredFields.cs", RequiredFieldsTemplate.Source("Payroll", null)),
            ("Views/EntryHeader.cs", WpfEntryControls.Header(context)),
            ("Views/EntryForm.cs", WpfEntryControls.Form(context)));
    }

    /// <summary>A foreign key is a lookup, not a text box — dropping it would leave no way to set
    /// a document's supplier at all.</summary>
    [TestMethod]
    public void The_form_renders_a_foreign_key_as_a_lookup()
    {
        var source = WpfEntryControls.Form(Context());

        StringAssert.Contains(source, "FieldGroups.NavigationFor(property)");
        StringAssert.Contains(source, "new ComboBox");
    }

    [TestMethod]
    public void The_form_marks_required_fields()
        => StringAssert.Contains(
            WpfEntryControls.Form(Context()), "RequiredFields.IsRequired(");

    [TestMethod]
    public void No_entity_name_appears_in_either_control()
    {
        foreach (var source in new[] { WpfEntryControls.Header(Context()), WpfEntryControls.Form(Context()) })
        {
            Assert.DoesNotContain("Customer", source);
            Assert.DoesNotContain("Order", source);
        }
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter WpfBusinessLayoutTests`
Expected: FAIL — `WpfEntryControls` does not exist.

- [ ] **Step 3: Write `WpfEntryControls`**

Create `AppScaffolding/Layouts/Wpf/WpfEntryControls.cs`. Both emitted controls build their visual
tree in code rather than XAML, because they are used from inside a `DataTemplate` where named
elements are per-instance and unreachable from the window's code-behind — the constraint
`PropertyEditorTemplate` documents.

```csharp
namespace AppScaffolding.Layouts.Wpf;

/// <summary>
/// The two controls an entry screen is built from: the accent header band, and the three columns
/// of grouped panels under it.
/// </summary>
/// <remarks>
/// Built in code rather than XAML for the reason <see cref="PropertyEditorTemplate"/> records: the
/// entry screen lives inside a DataTemplate, where named elements are per-instance and cannot be
/// reached from the window's code-behind at all. Each derives from Grid and takes the record
/// through its own dependency property, because a ContentControl's single Content slot would hold
/// the record instead of the generated form.
/// <para>
/// Neither names an entity. The whole point of this layout is that it is the same length for a
/// model of two entities and a model of twenty.
/// </para>
/// </remarks>
public static class WpfEntryControls
{
    /// <summary>Source of <c>Views/EntryHeader.cs</c>.</summary>
    public static string Header(LayoutContext context)
    {
        var accent = context.Themed ? "{DynamicResource AppAccentBrush}" : "#2563EB";

        return $$"""
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using {{context.RootNamespace}}.Screens;

namespace {{context.RootNamespace}}.Views;

/// <summary>The accent band at the top of an entry screen: a title, a subtitle, and the record's
/// key fields held out of the panels below.</summary>
public sealed class EntryHeader : Grid
{
    public static readonly DependencyProperty RecordProperty = DependencyProperty.Register(
        nameof(Record), typeof(object), typeof(EntryHeader), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(EntryHeader), new PropertyMetadata(string.Empty, OnChanged));

    public object? Record
    {
        get => GetValue(RecordProperty);
        set => SetValue(RecordProperty, value);
    }

    public string Heading
    {
        get => (string)GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EntryHeader)d).Build();

    private void Build()
    {
        Children.Clear();
        ColumnDefinitions.Clear();

        Background = (Brush)new BrushConverter().ConvertFromString("{{accent}}")!;
        Height = 74;

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };

        titles.Children.Add(new TextBlock
        {
            Text = Heading,
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
        });

        titles.Children.Add(new TextBlock
        {
            Text = $"Create and manage {Heading.ToLowerInvariant()} records",
            FontSize = 12,
            Foreground = Brushes.White,
            Opacity = 0.85,
        });

        SetColumn(titles, 1);
        Children.Add(titles);

        if (Record is not null)
        {
            var strip = KeyStrip(Record);

            SetColumn(strip, 2);
            Children.Add(strip);
        }
    }

    /// <summary>The code, date and status, which FieldGroups holds out of every panel so they are
    /// not drawn twice on one screen.</summary>
    private UIElement KeyStrip(object record)
    {
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 0),
        };

        var key = FieldGroups.Key(record.GetType());

        foreach (var property in new[] { key.Code, key.Date, key.Status })
        {
            if (property is null)
                continue;

            var cell = new StackPanel { Margin = new Thickness(14, 0, 0, 0), MinWidth = 110 };

            cell.Children.Add(new TextBlock
            {
                Text = FieldGroups.LabelOf(property),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 3),
            });

            var box = new TextBox { MinWidth = 110 };

            box.SetBinding(
                TextBox.TextProperty,
                new Binding(property.Name) { Source = record, Mode = BindingMode.TwoWay });

            cell.Children.Add(box);
            strip.Children.Add(cell);
        }

        return strip;
    }
}
""";
    }

    /// <summary>Source of <c>Views/EntryForm.cs</c>.</summary>
    public static string Form(LayoutContext context)
    {
        var accent = context.Themed ? "{DynamicResource AppAccentBrush}" : "#2563EB";
        var rule = context.Themed ? "{DynamicResource AppGridLineBrush}" : "#E5E7EB";
        var surface = context.Themed ? "{DynamicResource AppSurfaceBrush}" : "#FFFFFF";

        return $$"""
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using {{context.RootNamespace}}.Screens;

namespace {{context.RootNamespace}}.Views;

/// <summary>The grouped panels of an entry screen, laid out in the three columns FieldGroups
/// packs them into.</summary>
public sealed class EntryForm : Grid
{
    public static readonly DependencyProperty RecordProperty = DependencyProperty.Register(
        nameof(Record), typeof(object), typeof(EntryForm), new PropertyMetadata(null, OnRecordChanged));

    public object? Record
    {
        get => GetValue(RecordProperty);
        set => SetValue(RecordProperty, value);
    }

    private static Brush Brush(string colour)
        => (Brush)new BrushConverter().ConvertFromString(colour)!;

    private static void OnRecordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EntryForm)d).Build();

    private void Build()
    {
        Children.Clear();
        ColumnDefinitions.Clear();

        if (Record is null)
            return;

        var columns = FieldGroups.Columns(Record.GetType());

        for (var i = 0; i < columns.Count; i++)
        {
            // An empty column takes no width, so a small model is not two-thirds whitespace.
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

    private UIElement Panel(FieldGroup group)
    {
        var body = new StackPanel();

        body.Children.Add(new TextBlock
        {
            Text = group.Title,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("{{accent}}"),
            Margin = new Thickness(0, 0, 0, 8),
        });

        foreach (var field in group.Fields)
            body.Children.Add(Row(field));

        return new Border
        {
            BorderBrush = Brush("{{rule}}"),
            BorderThickness = new Thickness(1),
            Background = Brush("{{surface}}"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = body,
        };
    }

    private UIElement Row(PropertyInfo field)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 108 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };

        label.Inlines.Add(new System.Windows.Documents.Run(FieldGroups.LabelOf(field)));

        if (RequiredFields.IsRequired(Record!.GetType(), field.Name))
        {
            label.Inlines.Add(new System.Windows.Documents.Run(" *")
            {
                Foreground = Brushes.Red,
                FontWeight = FontWeights.Bold,
            });
        }

        SetColumn(label, 0);
        row.Children.Add(label);

        var editor = Editor(field);

        SetColumn(editor, 1);
        row.Children.Add(editor);

        return row;
    }

    /// <summary>
    /// The control a field is typed into.
    /// </summary>
    /// <remarks>
    /// A foreign key becomes a picker over the entity it points at — the same convention the grids'
    /// OnAutoGeneratingColumn applies. Rendering it as a text box, or dropping it, would leave no
    /// way to set a document's supplier at all.
    /// </remarks>
    private FrameworkElement Editor(PropertyInfo field)
    {
        var underlying = Nullable.GetUnderlyingType(field.PropertyType) ?? field.PropertyType;
        var binding = new Binding(field.Name) { Source = Record, Mode = BindingMode.TwoWay };

        if (FieldGroups.NavigationFor(field) is { } nav)
        {
            var lookup = new ComboBox { DisplayMemberPath = "Id" };

            lookup.SetBinding(Selector.SelectedValueProperty, binding);
            lookup.SelectedValuePath = "Id";
            lookup.ItemsSource = Rows(nav.PropertyType);

            return lookup;
        }

        if (underlying == typeof(bool))
        {
            var check = new CheckBox { VerticalAlignment = VerticalAlignment.Center };

            check.SetBinding(ToggleButton.IsCheckedProperty, binding);

            return check;
        }

        if (underlying == typeof(DateTime))
        {
            var picker = new DatePicker();

            picker.SetBinding(DatePicker.SelectedDateProperty, binding);

            return picker;
        }

        if (underlying.IsEnum)
        {
            var combo = new ComboBox { ItemsSource = Enum.GetValues(underlying) };

            combo.SetBinding(Selector.SelectedItemProperty, binding);

            return combo;
        }

        var box = new TextBox();

        box.SetBinding(TextBox.TextProperty, binding);

        return box;
    }

    /// <summary>The rows a lookup offers, read from the pane that holds them. Empty when the
    /// application has no pane for that entity, which is what a line type gets.</summary>
    private static IEnumerable Rows(Type entityType)
    {
        var main = Application.Current?.MainWindow?.DataContext;
        var groups = main?.GetType().GetProperty("Groups")?.GetValue(main) as IEnumerable;

        if (groups is null)
            return Array.Empty<object>();

        foreach (var group in groups)
        {
            var entities = group.GetType().GetProperty("Entities")?.GetValue(group) as IEnumerable;

            if (entities is null)
                continue;

            foreach (var entity in entities)
            {
                var title = entity.GetType().GetProperty("Title")?.GetValue(entity) as string;

                if (string.Equals(title, entityType.Name, StringComparison.Ordinal))
                    return entity.GetType().GetProperty("Items")?.GetValue(entity) as IEnumerable
                           ?? Array.Empty<object>();
            }
        }

        return Array.Empty<object>();
    }
}
""";
    }
}
```

Add `using System.Windows.Controls.Primitives;` to the emitted `EntryForm` for `ToggleButton` and
`Selector`. `AppAccentBrush`, `AppSurfaceBrush` and `AppGridLineBrush` are all declared by
`ThemeXamlTemplate` (lines 19–22), so the themed branches resolve.

Then add the third control, `Notes`, to the same class:

```csharp
    /// <summary>Source of <c>Views/EntryNotes.cs</c>.</summary>
    public static string Notes(LayoutContext context) => $$"""
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using {{context.RootNamespace}}.Screens;

namespace {{context.RootNamespace}}.Views;

/// <summary>The Additional Information tab: the long-text fields, as multi-line boxes.</summary>
/// <remarks>
/// A tab rather than a panel, which is what gives that tab -- present in both mockups -- real
/// content, and what gives Terms and Conditions a home without printing the same field twice.
/// </remarks>
public sealed class EntryNotes : Grid
{
    public static readonly DependencyProperty RecordProperty = DependencyProperty.Register(
        nameof(Record), typeof(object), typeof(EntryNotes), new PropertyMetadata(null, OnRecordChanged));

    public object? Record
    {
        get => GetValue(RecordProperty);
        set => SetValue(RecordProperty, value);
    }

    private static void OnRecordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EntryNotes)d).Build();

    private void Build()
    {
        Children.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();

        if (Record is null)
            return;

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 120 });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;

        foreach (var field in FieldGroups.Additional(Record.GetType()))
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = FieldGroups.LabelOf(field),
                Margin = new Thickness(10, 10, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };

            SetRow(label, row);
            SetColumn(label, 0);
            Children.Add(label);

            var box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 90,
                Margin = new Thickness(0, 10, 10, 0),
            };

            box.SetBinding(
                TextBox.TextProperty,
                new Binding(field.Name) { Source = Record, Mode = BindingMode.TwoWay });

            SetRow(box, row);
            SetColumn(box, 1);
            Children.Add(box);

            row++;
        }
    }
}
""";
```

Extend the Step 1 tests: `The_layout_emits_the_two_entry_controls` becomes
`The_layout_emits_the_three_entry_controls` and also asserts `EntryNotes.cs`;
`The_entry_controls_compile` gains `("Views/EntryNotes.cs", WpfEntryControls.Notes(context))`;
`No_entity_name_appears_in_either_control` covers all three sources.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter WpfBusinessLayoutTests`
Expected: the six new tests PASS. Tests pinning the old master/editor templates still fail — Task 9
rewrites them.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Layouts/Wpf/WpfEntryControls.cs CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs
git commit -m "Emit the WPF entry header and grouped entry form"
```

---

### Task 9: WPF — the master and editor templates

**Files:**
- Modify: `AppScaffolding/Layouts/Wpf/WpfBusinessLayout.cs`
- Test: `CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs` (rewrite the master/editor tests)

**Interfaces:**
- Consumes: `WpfEntryControls.Header`/`Form` (Task 8); `Editor`, the six commands and `IsDirty`
  on the screen (Task 6); `Totals` and `ILineSet.Clear` (Task 7).
- Produces: no new API. `ViewFiles` gains `EntryHeader.cs` and `EntryForm.cs` and loses
  `PropertyEditor.cs`.

- [ ] **Step 1: Rewrite the failing tests**

In `WpfBusinessLayoutTests`, replace `The_editor_template_offers_save_and_cancel`,
`The_lists_are_read_only` and `The_editor_shows_the_lines_of_a_transaction` with:

```csharp
    /// <summary>The master mockup's shape: one screen holding the form and the list.</summary>
    [TestMethod]
    public void The_master_template_holds_a_header_a_form_and_a_list()
    {
        var xaml = Xaml();
        var master = Between(xaml, "x:Key=\"MasterTemplate\"", "</DataTemplate>");

        StringAssert.Contains(master, "<views:EntryHeader");
        StringAssert.Contains(master, "<views:EntryForm");
        StringAssert.Contains(master, "<TabControl");
        StringAssert.Contains(master, "<DataGrid");
    }

    [TestMethod]
    public void The_master_template_binds_the_inline_editor()
    {
        var master = Between(Xaml(), "x:Key=\"MasterTemplate\"", "</DataTemplate>");

        StringAssert.Contains(master, "Record=\"{Binding Editor.Draft}\"");
    }

    [TestMethod]
    public void The_action_bar_offers_only_commands_that_exist()
    {
        var master = Between(Xaml(), "x:Key=\"MasterTemplate\"", "</DataTemplate>");

        foreach (var command in new[]
                 { "NewCommand", "EditCommand", "DeleteCommand", "SaveCommand", "ClearCommand", "CancelCommand" })
        {
            StringAssert.Contains(master, command);
        }
    }

    /// <summary>Spec departure 1: a button that does nothing is output that misinforms.</summary>
    [TestMethod]
    public void No_button_without_behaviour_is_emitted()
    {
        var xaml = Xaml();

        foreach (var absent in new[] { "Print", "Export", "Save &amp; Print", "Import Items" })
            Assert.DoesNotContain($"Content=\"{absent}\"", xaml);
    }

    [TestMethod]
    public void The_editor_template_holds_the_lines_in_tabs_with_their_totals()
    {
        var editor = Between(Xaml(), "x:Key=\"EditorTemplate\"", "<views:ScreenTemplateSelector");

        StringAssert.Contains(editor, "<views:EntryHeader");
        StringAssert.Contains(editor, "<views:EntryForm");
        StringAssert.Contains(editor, "ItemsSource=\"{Binding Lines}\"");
        StringAssert.Contains(editor, "ItemsSource=\"{Binding Totals}\"");
        StringAssert.Contains(editor, "OnClearLines");
        StringAssert.Contains(editor, "<views:EntryNotes");
    }

    /// <summary>The long-text fields are reachable on both screens, and on neither of them are
    /// they also in a panel — FieldGroups.For excludes them, and this is the view half of that.</summary>
    [TestMethod]
    public void Both_screens_show_the_additional_fields_behind_HasAdditional()
    {
        var xaml = Xaml();

        StringAssert.Contains(
            Between(xaml, "x:Key=\"MasterTemplate\"", "</DataTemplate>"),
            "Header=\"Additional Information\"");
        StringAssert.Contains(
            Between(xaml, "x:Key=\"EditorTemplate\"", "<views:ScreenTemplateSelector"),
            "Text=\"Additional Information\"");
        StringAssert.Contains(xaml, "<BooleanToVisibilityConverter x:Key=\"BoolToVisibility\" />");
    }
```

Add the `Xaml()` and `Between(...)` helpers if the fixture has none:

```csharp
    private static string Xaml()
        => new WpfBusinessLayout().ViewFiles(Context()).Single(f => f.Name == "MainWindow.xaml").Source;

    /// <summary>The slice of the window between two markers, so a template's assertions cannot be
    /// satisfied by markup that belongs to a different template.</summary>
    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, from, $"'{start}' not found");

        var to = source.IndexOf(end, from, StringComparison.Ordinal);

        Assert.IsGreaterThan(from, to, $"'{end}' not found after '{start}'");

        return source.Substring(from, to - from);
    }
```

`Between` matters: moving markup from one template to another leaves a whole-file `Contains`
assertion passing while the element is in the wrong place, and an absence assertion passing
vacuously.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter WpfBusinessLayoutTests`
Expected: FAIL — `<views:EntryHeader` is not in `MasterTemplate`.

- [ ] **Step 3: Rewrite the two templates**

In `WpfBusinessLayout.ViewFiles`, replace the `PropertyEditorTemplate` entry with:

```csharp
        new("Views", "EntryHeader.cs", WpfEntryControls.Header(context)),
        new("Views", "EntryForm.cs", WpfEntryControls.Form(context)),
```

Replace the `MasterTemplate` `DataTemplate` body with the header band, the form, a `TabControl`
holding the record list (search box, `DataGrid` bound to `Items` with `SelectedItem` two-way to
`Selected`, and a `Total Records` count from `Count`) and — when the model has long-text fields —
an Additional Information tab, then a bottom action bar and a status bar. Keep the file free of
entity names.

```xml
        <DataTemplate x:Key="MasterTemplate">
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
                <TabControl Margin="10,0,10,10">
                    <TabItem Header="{Binding Title}">
                        <DockPanel>
                            <DockPanel DockPanel.Dock="Top" Margin="0,6,0,6">
                                <TextBlock DockPanel.Dock="Right"
                                           Text="{Binding Count, StringFormat='Total Records: {0}'}"
                                           Foreground="{{{muted}}}"
                                           VerticalAlignment="Center" />
                                <TextBox Text="{Binding Search, UpdateSourceTrigger=PropertyChanged}"
                                         Width="240"
                                         HorizontalAlignment="Left" />
                            </DockPanel>
                            <DataGrid ItemsSource="{Binding Items}"
                                      SelectedItem="{Binding Selected, Mode=TwoWay}"
                                      AutoGenerateColumns="True"
                                      IsReadOnly="True"
                                      CanUserAddRows="False"
                                      AutoGeneratingColumn="OnAutoGeneratingColumn" />
                        </DockPanel>
                    </TabItem>
                </TabControl>
            </DockPanel>
        </DataTemplate>
```

`MouseDoubleClick="OnRowActivated"` comes off the master grid: selecting a row already opens it.
Remove `OnRowActivated` from the code-behind **only if** the transaction template no longer uses
it — it does still use it, so leave the handler in place.

Add the Additional Information tab to `MasterTemplate`, after the list `TabItem`. Its visibility is
bound rather than conditional markup, because the layout emits no per-entity XAML:

```xml
                    <TabItem Header="Additional Information"
                             Visibility="{Binding HasAdditional, Converter={StaticResource BoolToVisibility}}">
                        <views:EntryNotes Record="{Binding Editor.Draft}" />
                    </TabItem>
```

and declare the converter once in `Window.Resources`, above the templates:

```xml
        <BooleanToVisibilityConverter x:Key="BoolToVisibility" />
```

Then replace `EditorTemplate` entirely:

```xml
        <DataTemplate x:Key="EditorTemplate">
            <DockPanel>
                <views:EntryHeader DockPanel.Dock="Top"
                                   Heading="{Binding Title}"
                                   Record="{Binding Draft}" />
                <Border DockPanel.Dock="Bottom" Background="{{{railFill}}}" Padding="10,6">
                    <DockPanel>
                        <TextBlock Text="{Binding Status}" Foreground="{{{muted}}}" VerticalAlignment="Center" />
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                            <Button Content="Save" Command="{Binding SaveCommand}" Width="88" Margin="4,0" />
                            <Button Content="Cancel" Command="{Binding CancelCommand}" Width="88" Margin="4,0" />
                        </StackPanel>
                    </DockPanel>
                </Border>
                <views:EntryForm DockPanel.Dock="Top" Record="{Binding Draft}" Margin="10" />

                <!-- The totals sit beside the lines, and both are hidden for a master: an editor
                     with no line sets is a form and nothing else, which is what HasLines says. -->
                <Grid Margin="10,0,10,10"
                      Visibility="{Binding HasLines, Converter={StaticResource BoolToVisibility}}">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TabControl Grid.Column="0" ItemsSource="{Binding Lines}">
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
                        <ItemsControl ItemsSource="{Binding Totals}">
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

                <!-- A titled section rather than a tab beside the lines. The lines tab strip is
                     driven by ItemsSource, and mixing a static TabItem into it needs a
                     CompositeCollection -- whose CollectionContainer is not in the visual tree, so
                     inside a DataTemplate it has no DataContext and binds nothing. That parses
                     cleanly and renders an empty strip, so it is not worth the fidelity. -->
                <Border DockPanel.Dock="Bottom"
                        BorderBrush="{{{rule}}}"
                        BorderThickness="1"
                        Margin="10,0,10,10"
                        Padding="12,8"
                        Visibility="{Binding HasAdditional, Converter={StaticResource BoolToVisibility}}">
                    <StackPanel>
                        <TextBlock Text="Additional Information"
                                   FontWeight="SemiBold"
                                   Foreground="{{{accentBrush}}}"
                                   Margin="0,0,0,6" />
                        <views:EntryNotes Record="{Binding Draft}" />
                    </StackPanel>
                </Border>
            </DockPanel>
        </DataTemplate>
```

`accentBrush` is a new local in `Xaml(LayoutContext)` beside the existing `railFill` / `rule` /
`text` / `muted`:

```csharp
        var accentBrush = context.Themed ? "{DynamicResource AppAccentBrush}" : "#2563EB";
```

Add the `OnClearLines` handler to the code-behind, beside the existing `OnAddLine` /
`OnRemoveLine` and following their exact shape — they resolve the set from the clicked button's
`DataContext`:

```csharp
    private void OnClearLines(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ILineSet lines })
            lines.Clear();
    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "WpfBusinessLayoutTests|WpfLayoutNeutralityTests|WpfLayoutAppearanceTests"`
Expected: PASS. Neutrality must be green without having been edited.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Layouts/Wpf/WpfBusinessLayout.cs CodeGenerator.Tests/App/WpfBusinessLayoutTests.cs
git commit -m "Draw the WPF master and transaction entry screens"
```

---

### Task 10: WinForms — the same screens through `BuildList` and `BuildEditor`

**Files:**
- Modify: `AppScaffolding/Layouts/WinForms/WinFormsBusinessLayout.cs`
- Test: `CodeGenerator.Tests/App/WinFormsBusinessLayoutTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–7.
- Produces: no new API. The emitted `MainForm` gains `BuildEntryHeader`, `BuildEntryForm`,
  `BuildPanel`, `BuildFieldRow` and `BuildTotals`; `BuildList` and `BuildEditor` are rewritten.

Panels are a bordered `Panel` with a coloured title `Label`, **not** a `GroupBox`: a `GroupBox`
caption cannot take the accent colour. The screen is still built by reflection over the view model
rather than typed against it — every screen is `MasterScreenViewModel<T>` or similar, and a form
cannot name an open generic, which is the constraint the file already documents.

- [ ] **Step 1: Rewrite the failing tests**

In `WinFormsBusinessLayoutTests`, replace the tests pinning the old flat editor with:

```csharp
    private static string Source()
        => new WinFormsBusinessLayout().ViewFiles(Context()).Single(f => f.Name == "MainForm.cs").Source;

    [TestMethod]
    public void The_form_builds_a_header_a_grouped_form_and_a_tab_strip()
    {
        var source = Source();

        StringAssert.Contains(source, "private Control BuildEntryHeader(");
        StringAssert.Contains(source, "private Control BuildEntryForm(");
        StringAssert.Contains(source, "new TabControl");
        StringAssert.Contains(source, "FieldGroups.Columns(");
    }

    /// <summary>A GroupBox caption cannot take the accent colour, so the panel is a bordered Panel
    /// with a Label of its own.</summary>
    [TestMethod]
    public void A_panel_is_not_a_group_box()
        => Assert.DoesNotContain("new GroupBox", Source());

    [TestMethod]
    public void The_form_marks_required_fields_and_renders_lookups()
    {
        var source = Source();

        StringAssert.Contains(source, "RequiredFields.IsRequired(");
        StringAssert.Contains(source, "FieldGroups.NavigationFor(");
    }

    [TestMethod]
    public void The_action_bar_offers_only_commands_that_exist()
    {
        var source = Source();

        foreach (var command in new[]
                 { "NewCommand", "EditCommand", "DeleteCommand", "SaveCommand", "ClearCommand", "CancelCommand" })
        {
            StringAssert.Contains(source, command);
        }

        foreach (var absent in new[] { "\"Print\"", "\"Export\"", "\"Import Items\"" })
            Assert.DoesNotContain(absent, source);
    }

    [TestMethod]
    public void The_editor_shows_the_line_totals_and_can_clear_a_set()
    {
        var source = Source();

        StringAssert.Contains(source, "Totals");
        StringAssert.Contains(source, "\"Clear All\"");
    }

    /// <summary>Behind HasAdditional on both screens, so a model with no long-text fields shows no
    /// empty tab — the same rule that drops an empty panel.</summary>
    [TestMethod]
    public void The_additional_fields_are_shown_only_when_there_are_some()
    {
        var source = Source();

        StringAssert.Contains(source, "HasAdditional");
        StringAssert.Contains(source, "FieldGroups.Additional(");
        StringAssert.Contains(source, "\"Additional Information\"");
    }

    [TestMethod]
    public void The_form_compiles()
        => EmittedDesktopSource.AssertCompilesTogether(
            ("Screens/FieldGroups.cs", FieldGroupsTemplate.Source("Payroll")),
            ("Screens/RequiredFields.cs", RequiredFieldsTemplate.Source("Payroll", null)),
            ("Views/MainForm.cs", Source()));
```

`The_form_compiles` will need the view models and the auth stub alongside it, exactly as the
fixture's existing compile test assembles them — reuse that test's file list and add the two new
`Screens/` entries rather than writing a new list.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter WinFormsBusinessLayoutTests`
Expected: FAIL — `BuildEntryHeader` is not in the emitted form.

- [ ] **Step 3: Rewrite `BuildList` and `BuildEditor`**

Both now return a `Panel` docked as: `BuildEntryHeader(screen)` at the top, `BuildEntryForm(record)`
below it, a `TabControl` filling the rest, and an action bar plus a status `Label` at the bottom.

`BuildEntryForm` mirrors the WPF control:

```csharp
    /// <summary>The grouped panels, in the three columns FieldGroups packs them into.</summary>
    private Control BuildEntryForm(object record)
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var columns = FieldGroups.Columns(record.GetType());

        for (var i = 0; i < columns.Count; i++)
        {
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns.Count));

            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
            };

            foreach (var group in columns[i])
                stack.Controls.Add(BuildPanel(record, group));

            host.Controls.Add(stack, i, 0);
        }

        return host;
    }
```

`BuildPanel` and `BuildFieldRow`:

```csharp
    /// <summary>
    /// One titled panel.
    /// </summary>
    /// <remarks>
    /// A bordered Panel with a Label of its own, not a GroupBox: a GroupBox caption cannot take
    /// the accent colour, which is the whole visual signature of these panels.
    /// </remarks>
    private Control BuildPanel(object record, FieldGroup group)
    {
        var body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(10),
        };

        body.Controls.Add(new Label
        {
            Text = group.Title,
            ForeColor = {{accent}},
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
        });

        foreach (var field in group.Fields)
            body.Controls.Add(BuildFieldRow(record, field));

        var panel = new Panel
        {
            BorderStyle = BorderStyle.FixedSingle,
            AutoSize = true,
            Margin = new Padding(6),
        };

        panel.Controls.Add(body);

        return panel;
    }

    /// <summary>A label, an asterisk when the field is required, and the control it is typed
    /// into.</summary>
    private Control BuildFieldRow(object record, PropertyInfo field)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 4),
        };

        var required = RequiredFields.IsRequired(record.GetType(), field.Name);

        row.Controls.Add(new Label
        {
            Text = FieldGroups.LabelOf(field),
            AutoSize = false,
            Width = 108,
            TextAlign = ContentAlignment.MiddleLeft,
        });

        row.Controls.Add(new Label
        {
            Text = required ? "*" : string.Empty,
            ForeColor = Color.Red,
            AutoSize = false,
            Width = 12,
            TextAlign = ContentAlignment.MiddleLeft,
        });

        row.Controls.Add(BuildFieldEditor(record, field));

        return row;
    }

    /// <summary>
    /// The control a field is typed into.
    /// </summary>
    /// <remarks>
    /// A foreign key becomes a picker over the entity it points at, the same convention the grids
    /// apply. Rendering it as a text box, or dropping it, would leave no way to set a document's
    /// supplier at all.
    /// </remarks>
    private Control BuildFieldEditor(object record, PropertyInfo field)
    {
        var underlying = Nullable.GetUnderlyingType(field.PropertyType) ?? field.PropertyType;

        if (FieldGroups.NavigationFor(field) is { } nav)
        {
            var lookup = new ComboBox
            {
                Width = 190,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Id",
                ValueMember = "Id",
                DataSource = LookupRows(nav.PropertyType),
            };

            lookup.DataBindings.Add(
                new Binding("SelectedValue", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged));

            return lookup;
        }

        if (underlying == typeof(bool))
        {
            var check = new CheckBox { Width = 190 };

            check.DataBindings.Add(
                new Binding("Checked", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged));

            return check;
        }

        if (underlying == typeof(DateTime))
        {
            var picker = new DateTimePicker { Width = 190, Format = DateTimePickerFormat.Short };

            picker.DataBindings.Add(
                new Binding("Value", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged));

            return picker;
        }

        if (underlying.IsEnum)
        {
            var combo = new ComboBox
            {
                Width = 190,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DataSource = Enum.GetValues(underlying),
            };

            combo.DataBindings.Add(
                new Binding("SelectedItem", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged));

            return combo;
        }

        var box = new TextBox { Width = 190 };

        box.DataBindings.Add(
            new Binding("Text", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged));

        return box;
    }

    /// <summary>The rows a lookup offers, from the pane that holds that entity. Empty when the
    /// application has no pane for it, which is what a line type gets.</summary>
    private IList LookupRows(Type entityType)
    {
        foreach (var group in _shell.Groups)
        {
            foreach (var entity in group.Entities)
            {
                if (string.Equals(entity.Title, entityType.Name, StringComparison.Ordinal))
                    return entity.Items.Cast<object>().ToList();
            }
        }

        return new List<object>();
    }
```

The Additional Information fields go in a `TabPage` on both screens — Windows Forms has no
equivalent of the WPF `CollectionContainer` limitation, because the tab pages are added in code
either way. Add the page only when there are fields for it, which is the same rule that drops an
empty panel:

```csharp
    /// <summary>The Additional Information tab: the long-text fields, as multi-line boxes.</summary>
    private void AddAdditionalTab(TabControl tabs, object record)
    {
        var fields = FieldGroups.Additional(record.GetType());

        if (fields.Count == 0)
            return;

        var body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(10),
        };

        foreach (var field in fields)
        {
            body.Controls.Add(new Label { Text = FieldGroups.LabelOf(field), AutoSize = true });

            var box = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Width = 520,
                Height = 90,
                Margin = new Padding(0, 0, 0, 10),
            };

            box.DataBindings.Add(
                new Binding("Text", record, field.Name, true, DataSourceUpdateMode.OnPropertyChanged));

            body.Controls.Add(box);
        }

        var page = new TabPage("Additional Information");

        page.Controls.Add(body);
        tabs.TabPages.Add(page);
    }
```

Call it from both `BuildList` and `BuildEditor`, after their own tab pages are added.

`BuildTotals` renders the editor's `Totals` as right-aligned label pairs, the `IsGrand` row in
bold:

```csharp
    private Control BuildTotals(IRecordEditor editor)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Right,
            Width = 240,
            WrapContents = false,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10),
        };

        foreach (var total in ((dynamic)editor).Totals)
        {
            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
            };

            var bold = (bool)total.IsGrand;

            row.Controls.Add(new Label { Text = (string)total.Column, Width = 120, AutoSize = false });
            row.Controls.Add(new Label
            {
                Text = ((decimal)total.Value).ToString("N2"),
                Width = 90,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Font = bold ? new Font(Font, FontStyle.Bold) : Font,
            });

            panel.Controls.Add(row);
        }

        return panel;
    }
```

`dynamic` is used here because `IRecordEditor` does not declare `Totals` — the editor is generic
over the record and the shell holds the interface. If `dynamic` is unwelcome, add
`IReadOnlyList<LineTotal> Totals { get; }` to `IRecordEditor` in Task 7 instead and bind it
directly; the emitted `MainForm` already reflects over view models elsewhere, so either is in
keeping.

`BuildLines` gains a third button:

```csharp
        var clear = new Button { Text = "Clear All", Width = 88 };

        clear.Click += (_, _) => { lines.Clear(); Bind(lines, grid); };

        buttons.Controls.Add(clear);
```

using the same local `lines` the existing Add/Remove buttons in that method already resolve.

Keep `BuildDashboard`, `BuildHeader`, `BuildRail`, `Show`, `BuildScreen`, `Refresh`, `Bind`,
`CommandButton`, `Invoke` and `IsAdminPane` as they are. `CommandButton(owner, commandName, text)`
already exists — use it for every action-bar button rather than writing new click handlers.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --filter "WinFormsBusinessLayoutTests|WinFormsLayoutNeutralityTests"`
Expected: PASS. Neutrality green without having been edited.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Layouts/WinForms/WinFormsBusinessLayout.cs CodeGenerator.Tests/App/WinFormsBusinessLayoutTests.cs
git commit -m "Draw the Windows Forms master and transaction entry screens"
```

---

### Task 11: The whole suite, and a generated app that builds

**Files:** none — this task changes code only where something is red.

- [ ] **Step 1: Build exactly as CI does**

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-restore -warnaserror
```

Expected: succeeds with no warnings. A Debug build of `CodeGenerator` alone would hide analyzer
errors — do not substitute it.

- [ ] **Step 2: Run the whole suite**

```bash
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

Expected: every test passes. `Failed!` in the summary is the string to search for — a per-test
`  Failed <name>` line does not match it. Fix anything red before continuing; in particular
`GeneratedProjectBuildTests` proves a generated project still compiles as a project, which no
subset compile can.

- [ ] **Step 3: Generate a WPF app and a WinForms app**

Rebuild the CLI first — `dotnet test` leaves its `bin` stale, and a green fix can look dead.

```bash
dotnet build CodeGenerator/CodeGenerator.csproj -c Release
```

Then generate both, into `D:\SampleProjects`:

```
build app: purchase ordering system for a wholesaler
```

answering `wpf` and then `winforms` for the target, taking the default `business` layout. The model
must have a transaction with lines and a master with enough properties to fill the panels — a
purchase order with order lines, a supplier and an item.

- [ ] **Step 4: Build both generated apps**

```bash
dotnet build "D:\SampleProjects\<AppName>\<AppName>.csproj" -c Debug
```

Expected: both succeed. Probe under a short path if MSB3030 reports a file not found that exists —
that is the MAX_PATH tell.

- [ ] **Step 5: Commit any fixes**

```bash
git add -- <the files you actually changed>
git commit -m "Fix <what was red>"
```

---

### Task 12: Look at both running applications

**Files:** none unless a defect is found — and one usually is.

Every previous round in this repository has produced defects that compiled clean with a green suite
and appeared only on a running screen: a missing render mode, a blank overlay, a caption escaping
bug, a field order nobody chose. A green suite is not the completion criterion for this branch.

- [ ] **Step 1: Run the WPF app and screenshot the master screen**

```bash
Start-Process "D:\SampleProjects\<AppName>\bin\Debug\net10.0-windows\<AppName>.exe"
```

Navigate to a master in the rail. Screenshot it.

- [ ] **Step 2: Check the master screen against the mockup**

Confirm by eye, and write down what is wrong:
- three columns of panels, each with an accent title, none empty, none overflowing its border
- the header band shows the code, date and status — and **none of those three appears again in a
  panel below**
- required fields carry a red asterisk, and it is the same set the validator rejects
- selecting a row in the list loads it into the form above
- New / Edit / Delete / Save / Clear / Cancel all do something; no other button is present
- the record count reads `Total Records: n` and matches the grid
- a foreign key is a picker showing the referenced records, not an integer box

- [ ] **Step 3: Screenshot the transaction entry screen**

Open a transaction from its list. Confirm:
- the lines are in a tab, editable in place, with Add Row / Delete Row / Clear All
- the totals panel sums each money column of the lines and changes as a line is edited
- the Additional Information tab holds the long-text fields, and they appear nowhere else
- Save writes the parent and then the lines; Cancel discards both
- leaving a dirty screen raises the discard prompt — check this on a **master**, which is the path
  `IDirtyScreen` exists for and the one no test can see

- [ ] **Step 4: Repeat both for the WinForms app**

The same checks against `bin\Debug\net10.0-windows\<AppName>.exe` for the WinForms build.

- [ ] **Step 5: Fix what looking found, with a test where a test is possible**

For each defect: write the failing test first if the defect is expressible as one, fix it, rerun
the suite. Where no test can see it — layout, colour, ordering on screen — say so in the commit
message so the gap is on the record rather than implied.

- [ ] **Step 6: Final CI-shaped run and commit**

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-restore -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
git add -- <files changed>
git commit -m "Fix what running the generated apps found"
```

---

## One departure this plan adds to the spec's five

**On the WPF transaction entry screen, Additional Information is a titled section under the lines,
not a tab beside them.** The lines tab strip is driven by `ItemsSource="{Binding Lines}"`, and
mixing a static `TabItem` into it needs a `CompositeCollection` — whose `CollectionContainer` is
not in the visual tree, so inside a `DataTemplate` it has no `DataContext` and binds nothing. That
parses cleanly and renders an empty strip: a defect that no test would catch and that only opening
the app would show. The master screen keeps a real tab, because both of its tabs are static, and
Windows Forms keeps a real `TabPage` on both screens, because it adds pages in code either way.

Update the spec's departures list to say so when this branch is finished.

## Done when

- Both mockup shapes are on screen in a generated WPF app and a generated WinForms app, and have
  been looked at.
- The full solution builds Release with `-warnaserror` and the whole suite is green.
- `WpfLayoutNeutralityTests` and `WinFormsLayoutNeutralityTests` are green **and unedited**.
- The departures in the spec are still true of the output, or the spec has been updated to say
  otherwise.
