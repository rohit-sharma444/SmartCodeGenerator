# Entity Classification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Decide, from the discovered model, which entities are masters, which are transactions and which are the lines of a transaction — overridably, and reported to the user.

**Architecture:** A pure function in `AppScaffolding/Screens/` maps `ClassDefinition`s plus the user's overrides to a role per entity name. Nothing renders anything: this branch produces the decision and the seam that carries it, and branch 3 is the first consumer. The overrides ride on `AppCustomization` as comma-joined strings so the record keeps value equality.

**Tech Stack:** .NET 10, MSTest, existing `ForeignKeys` shared FK definition, `InlineCustomization` two-token value keys.

**Spec:** `docs/superpowers/specs/2026-08-20-professional-app-layouts-design.md` — the **Classification** section, plus the `Delivery` item 2.

## Global Constraints

- **No new NuGet packages.**
- **`AppCustomization` is compared by value** — `CustomizationWizardTests` and `InlineCustomizationTests` both do `Assert.AreEqual(AppCustomization.Default, customization)`. A `List<string>` or array member would break that **silently**, because records use reference equality for those. Every new member on that record must be a `string`, a `bool`, an enum or a nullable of one.
- **Nothing in this branch renders a screen.** No layout, no view, no `IEntityViewModel` change. If a task starts wanting one, it belongs in branch 3.
- **`User` is emitted, not discovered**, and never reaches the classifier: the classifier is handed the discovered classes only.
- **Emitted code is compile-tested with Roslyn** where any is emitted — but this branch emits almost none, which is why its tests are ordinary unit tests over a pure function.
- **CI builds `SmartCodeGenerator.slnx` in Release with `-warnaserror`.** Run `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental` before claiming a task is done; the Debug build hides analyzer errors.
- **Run `GeneratedProjectBuildTests` before every commit that changes emitted output or the scaffolder.** Branch 1 shipped five defects that passed every string assertion and were caught only by building a real app.
- **MSTest conventions:** `[TestClass]` / `[TestMethod]`, sentence-shaped method names with underscores, `Assert.HasCount(n, x)` not `Assert.AreEqual(n, x.Count)` (MSTEST0037), `Assert.IsGreaterThan(bound, actual)` argument order.
- **Heredoc warning for the implementer:** if you edit files with a `python - <<'PY'` heredoc in this harness, backslashes are eaten — `'\\n'` arrives as a real newline and produces CS1010 while the script still prints success. Use raw strings, `chr(92)`, and assert match counts.

---

## File Structure

**New production files** (namespace `AppScaffolding.Screens`):

| File | Responsibility |
| --- | --- |
| `AppScaffolding/Screens/EntityRole.cs` | The three-value enum |
| `AppScaffolding/Screens/EntityRoleMap.cs` | The classifier's result: role per name, and the lines belonging to each transaction |
| `AppScaffolding/Screens/TransactionWords.cs` | The gazetteer, alone, so it can be read and argued with without reading the heuristic |
| `AppScaffolding/Screens/EntityRoles.cs` | The heuristic itself: `Classify(classes, overrides)` |
| `AppScaffolding/Screens/ScreenOverrides.cs` | Parses the two comma-joined option values into name sets |
| `AppScaffolding/FrontEndContext.cs` | What a target is handed once it needs roles as well as names |

**Modified production files:**

| File | Change |
| --- | --- |
| `AppScaffolding/AppCustomization.cs` | `Transactions` and `Masters`, both `string`, both empty by default |
| `AppScaffolding/ScaffoldResult.cs` | `ScreenSummary` string |
| `AppScaffolding/AppScaffolder.cs` | Classify, and put the summary in the result |
| `AppScaffolding/ITargetScaffolder.cs` | `FrontEndFiles(FrontEndContext)` default interface method |
| `CodeGenerator/App/InlineCustomization.cs` | Two new value keys |
| `CodeGenerator/App/BuildAppCommand.cs` | The options help line; print the screen summary |
| `CodeGenerator/App/CustomizationSummary.cs` | Report the overrides |
| `CodeGenerator/App/CustomizationWizard.cs`, `CustomizationMenus.cs`, `CustomizationDefaults.cs` | Ask and remember |
| `CodeGenerator/Web/Workflow/BuildCommandEcho.cs` | Echo the overrides |
| `CodeGenerationEngine/Core/CommandDictionary.cs` | The catalogue entry |
| `docs/user-manual-source.html` + rebuild the `.docx` | The manual |

**New test files** (`CodeGenerator.Tests/App/`): `EntityRolesTests.cs`, `DocumentLineTests.cs`, `ScreenOverridesTests.cs`, `ScreenSummaryTests.cs`, `FrontEndContextTests.cs`.

---

### Task 1: The role and the map it lands in

**Files:**
- Create: `AppScaffolding/Screens/EntityRole.cs`, `AppScaffolding/Screens/EntityRoleMap.cs`
- Test: `CodeGenerator.Tests/App/EntityRolesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `enum AppScaffolding.Screens.EntityRole { Master, Transaction, DocumentLine }`. `sealed class EntityRoleMap` with `EntityRole RoleOf(string name)` (returns `Master` for an unknown name), `IReadOnlyList<string> Names(EntityRole role)`, `IReadOnlyList<string> LinesOf(string transactionName)`, and a constructor `EntityRoleMap(IReadOnlyDictionary<string, EntityRole> roles, IReadOnlyDictionary<string, IReadOnlyList<string>> lines)`. All name lookups are `OrdinalIgnoreCase`.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding.Screens;

namespace CodeGenerator.Tests.App;

/// <summary>The classifier's result type. Master is the answer for anything unknown, because a
/// misclassified master renders as the ordinary grid this generator already produced.</summary>
[TestClass]
public class EntityRoleMapTests
{
    private static EntityRoleMap Map()
        => new(
            new Dictionary<string, EntityRole>(StringComparer.OrdinalIgnoreCase)
            {
                ["Customer"] = EntityRole.Master,
                ["Order"] = EntityRole.Transaction,
                ["OrderLine"] = EntityRole.DocumentLine,
            },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Order"] = new[] { "OrderLine" },
            });

    [TestMethod]
    public void A_name_reports_the_role_it_was_given()
    {
        Assert.AreEqual(EntityRole.Master, Map().RoleOf("Customer"));
        Assert.AreEqual(EntityRole.Transaction, Map().RoleOf("Order"));
        Assert.AreEqual(EntityRole.DocumentLine, Map().RoleOf("OrderLine"));
    }

    /// <summary>Casing is not a classification. A caller asking about "order" means Order.</summary>
    [TestMethod]
    public void Names_are_matched_without_regard_to_case()
        => Assert.AreEqual(EntityRole.Transaction, Map().RoleOf("ORDER"));

    /// <summary>An unknown name is a Master, not an exception: the safe default is the screen
    /// this generator has always produced.</summary>
    [TestMethod]
    public void An_unknown_name_is_a_master()
        => Assert.AreEqual(EntityRole.Master, Map().RoleOf("Nonexistent"));

    [TestMethod]
    public void Names_can_be_listed_by_role()
    {
        CollectionAssert.AreEquivalent(new[] { "Customer" }, Map().Names(EntityRole.Master).ToList());
        CollectionAssert.AreEquivalent(new[] { "Order" }, Map().Names(EntityRole.Transaction).ToList());
    }

    [TestMethod]
    public void A_transaction_reports_its_lines()
    {
        Assert.HasCount(1, Map().LinesOf("Order"));
        Assert.AreEqual("OrderLine", Map().LinesOf("Order")[0]);
    }

    /// <summary>A transaction with no line entity is the common case, not an error.</summary>
    [TestMethod]
    public void A_transaction_with_no_lines_reports_an_empty_list()
        => Assert.IsEmpty(Map().LinesOf("Customer"));
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter EntityRoleMapTests`
Expected: FAIL — `AppScaffolding.Screens` does not exist (CS0234).

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Screens/EntityRole.cs`:

```csharp
namespace AppScaffolding.Screens;

/// <summary>What kind of screen an entity gets.</summary>
/// <remarks>
/// <see cref="Master"/> is the safe default and the value anything unrecognised takes: a
/// misclassified master renders as the ordinary grid this generator has always produced, whereas a
/// misclassified <see cref="DocumentLine"/> hides a screen entirely.
/// </remarks>
public enum EntityRole
{
    /// <summary>Reference data — a list and an editor.</summary>
    Master,

    /// <summary>A record that references reference data — a filtered list, totals, and an editor
    /// that may hold lines.</summary>
    Transaction,

    /// <summary>A line belonging to a transaction. Gets no screen of its own; it is edited inside
    /// its parent's editor.</summary>
    DocumentLine
}
```

Create `AppScaffolding/Screens/EntityRoleMap.cs`:

```csharp
namespace AppScaffolding.Screens;

/// <summary>What the classifier decided, for one discovered model.</summary>
public sealed class EntityRoleMap
{
    private readonly IReadOnlyDictionary<string, EntityRole> _roles;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _lines;

    public EntityRoleMap(
        IReadOnlyDictionary<string, EntityRole> roles,
        IReadOnlyDictionary<string, IReadOnlyList<string>> lines)
    {
        _roles = roles;
        _lines = lines;
    }

    /// <summary>An empty model, so a caller with nothing discovered needs no null check.</summary>
    public static EntityRoleMap Empty { get; } = new(
        new Dictionary<string, EntityRole>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

    /// <summary>The role of <paramref name="name"/>, or <see cref="EntityRole.Master"/> when it
    /// was never classified.</summary>
    public EntityRole RoleOf(string name)
        => _roles.TryGetValue(name, out var role) ? role : EntityRole.Master;

    /// <summary>Every name with <paramref name="role"/>, in the order they were discovered.</summary>
    public IReadOnlyList<string> Names(EntityRole role)
        => _roles.Where(pair => pair.Value == role).Select(pair => pair.Key).ToList();

    /// <summary>The line entities belonging to <paramref name="transactionName"/>, or empty.</summary>
    public IReadOnlyList<string> LinesOf(string transactionName)
        => _lines.TryGetValue(transactionName, out var lines) ? lines : [];
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter EntityRoleMapTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Screens/ CodeGenerator.Tests/App/EntityRolesTests.cs
git commit -m "feat(screens): an entity has a role, and Master is the safe default"
```

---

### Task 2: The transaction heuristic

**Files:**
- Create: `AppScaffolding/Screens/TransactionWords.cs`, `AppScaffolding/Screens/EntityRoles.cs`
- Test: append to `CodeGenerator.Tests/App/EntityRolesTests.cs`

**Interfaces:**
- Consumes: Task 1's `EntityRole`, `EntityRoleMap`; the existing `AppScaffolding.ForeignKeys.TargetsOf(ClassDefinition, HashSet<string>)` and `ForeignKeys.ClassNameSet(IReadOnlyList<ClassDefinition>)`.
- Produces: `AppScaffolding.Screens.EntityRoles.Classify(IReadOnlyList<ClassDefinition> classes) -> EntityRoleMap`. `TransactionWords.Contains(string entityName) -> bool`, and `TransactionWords.All` as `IReadOnlyList<string>`. `EntityRoles.AuditFields` as `IReadOnlyList<string>` — `["CreatedOn", "ModifiedOn"]`.

- [ ] **Step 1: Write the failing test**

Append to `CodeGenerator.Tests/App/EntityRolesTests.cs`:

```csharp
using AppScaffolding.Screens;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The heuristic. A transaction needs BOTH an outgoing foreign key AND a reason to think it is
/// dated or transactional by name — either half alone misfires on an ordinary model.
/// </summary>
[TestClass]
public class EntityRolesTests
{
    private static ClassDefinition Entity(string name, params (string Name, string Type)[] fields)
    {
        var definition = new ClassDefinition { Name = name };

        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        foreach (var (fieldName, type) in fields)
            definition.Properties.Add(new PropertyDefinition { Name = fieldName, Type = type });

        // Every discovered class gets these from DomainModelMapper.AddAuditFields. They are here
        // in the fixture precisely because the heuristic has to ignore them.
        definition.Properties.Add(new PropertyDefinition { Name = "CreatedOn", Type = "DateTime" });
        definition.Properties.Add(new PropertyDefinition { Name = "ModifiedOn", Type = "DateTime" });

        return definition;
    }

    /// <summary>A foreign key as ForeignKeys.TargetsOf defines one: a &lt;Target&gt;Id int AND a
    /// navigation property typed as the target.</summary>
    private static (string Name, string Type)[] ForeignKeyTo(string target)
        => [(target + "Id", "int"), (target, target)];

    [TestMethod]
    public void An_entity_with_no_foreign_keys_is_a_master()
    {
        var map = EntityRoles.Classify([Entity("Customer", ("Name", "string"))]);

        Assert.AreEqual(EntityRole.Master, map.RoleOf("Customer"));
    }

    /// <summary>The audit-field trap. DomainModelMapper gives EVERY class a CreatedOn and a
    /// ModifiedOn, so "has a DateTime" is true of everything and means nothing. Without the
    /// exclusion this entity classifies as a transaction and so does the whole model.</summary>
    [TestMethod]
    public void A_foreign_key_plus_only_audit_dates_is_not_a_transaction()
    {
        var classes = new List<ClassDefinition>
        {
            Entity("Customer", ("Name", "string")),
            Entity("Address", [.. ForeignKeyTo("Customer"), ("Street", "string")]),
        };

        Assert.AreEqual(EntityRole.Master, EntityRoles.Classify(classes).RoleOf("Address"));
    }

    [TestMethod]
    public void A_foreign_key_plus_a_real_date_is_a_transaction()
    {
        var classes = new List<ClassDefinition>
        {
            Entity("Customer", ("Name", "string")),
            Entity("Booking", [.. ForeignKeyTo("Customer"), ("BookedOn", "DateTime")]),
        };

        Assert.AreEqual(EntityRole.Transaction, EntityRoles.Classify(classes).RoleOf("Booking"));
    }

    /// <summary>The gazetteer is the other half: a domain whose vocabulary the lexicon never
    /// learned may produce an Order with no date field at all.</summary>
    [TestMethod]
    public void A_foreign_key_plus_a_transactional_name_is_a_transaction()
    {
        var classes = new List<ClassDefinition>
        {
            Entity("Customer", ("Name", "string")),
            Entity("Order", [.. ForeignKeyTo("Customer"), ("Amount", "decimal")]),
        };

        Assert.AreEqual(EntityRole.Transaction, EntityRoles.Classify(classes).RoleOf("Order"));
    }

    /// <summary>Both halves are required. A dated entity that references nothing is reference data
    /// that happens to record a date — an Employee with a HireDate is not a transaction.</summary>
    [TestMethod]
    public void A_date_without_a_foreign_key_is_not_a_transaction()
    {
        var map = EntityRoles.Classify([Entity("Employee", ("HireDate", "DateTime"))]);

        Assert.AreEqual(EntityRole.Master, map.RoleOf("Employee"));
    }

    /// <summary>Likewise a transactional NAME with nothing to reference.</summary>
    [TestMethod]
    public void A_transactional_name_without_a_foreign_key_is_not_a_transaction()
    {
        var map = EntityRoles.Classify([Entity("Order", ("Amount", "decimal"))]);

        Assert.AreEqual(EntityRole.Master, map.RoleOf("Order"));
    }

    /// <summary>The gazetteer matches a word inside a name, or SalesOrder and PurchaseOrder are
    /// both missed.</summary>
    [TestMethod]
    public void A_compound_name_matches_the_word_inside_it()
    {
        var classes = new List<ClassDefinition>
        {
            Entity("Customer", ("Name", "string")),
            Entity("SalesOrder", [.. ForeignKeyTo("Customer"), ("Amount", "decimal")]),
        };

        Assert.AreEqual(EntityRole.Transaction, EntityRoles.Classify(classes).RoleOf("SalesOrder"));
    }

    [TestMethod]
    public void An_empty_model_classifies_to_an_empty_map()
        => Assert.IsEmpty(EntityRoles.Classify([]).Names(EntityRole.Master));

    /// <summary>The audit field names are stated once and shared, so the heuristic and any future
    /// caller cannot disagree about which dates do not count.</summary>
    [TestMethod]
    public void The_audit_fields_are_the_two_DomainModelMapper_adds()
        => CollectionAssert.AreEquivalent(
            new[] { "CreatedOn", "ModifiedOn" }, EntityRoles.AuditFields.ToList());
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter EntityRolesTests`
Expected: FAIL — `EntityRoles` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Screens/TransactionWords.cs`:

```csharp
namespace AppScaffolding.Screens;

/// <summary>
/// The words that make an entity name sound like a transaction.
/// </summary>
/// <remarks>
/// Its own file so it can be read, argued with and extended without reading the heuristic. The
/// list is deliberately short: every word here is one that could misclassify a master, and the
/// gazetteer is only ever half of the test — an entity also needs an outgoing foreign key.
/// </remarks>
public static class TransactionWords
{
    /// <summary>Lower-case, matched as a substring of the entity name.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "order", "invoice", "payment", "receipt", "booking", "reservation",
        "sale", "purchase", "shipment", "delivery", "transfer", "transaction",
        "refund", "claim", "dispatch", "movement", "adjustment",
    ];

    /// <summary>Whether <paramref name="entityName"/> contains one of the words.</summary>
    /// <remarks>
    /// Substring rather than whole-word, so SalesOrder and PurchaseInvoice match. That is also why
    /// the list holds no word that is a common fragment of an unrelated one.
    /// </remarks>
    public static bool Contains(string entityName)
        => All.Any(word => entityName.Contains(word, StringComparison.OrdinalIgnoreCase));
}
```

Create `AppScaffolding/Screens/EntityRoles.cs`:

```csharp
using CodeGenerationEngine.Models;

namespace AppScaffolding.Screens;

/// <summary>
/// Decides which discovered entities are masters and which are transactions.
/// </summary>
/// <remarks>
/// A pure function: no I/O, no UI, no dependency on a target. Everything it needs is the class
/// definitions and, later, the user's overrides.
/// </remarks>
public static class EntityRoles
{
    /// <summary>
    /// The date fields <c>DomainModelMapper.AddAuditFields</c> puts on EVERY discovered class.
    /// </summary>
    /// <remarks>
    /// The single most important line in this file. Because every class has these, "has a
    /// DateTime field" is true of everything and carries no information — leave them in and the
    /// entire model classifies as transactions, silently, with no sign of it in the emitted diff.
    /// </remarks>
    public static IReadOnlyList<string> AuditFields { get; } = ["CreatedOn", "ModifiedOn"];

    /// <summary>The role of every class in <paramref name="classes"/>.</summary>
    public static EntityRoleMap Classify(IReadOnlyList<ClassDefinition> classes)
    {
        var roles = new Dictionary<string, EntityRole>(StringComparer.OrdinalIgnoreCase);
        var names = ForeignKeys.ClassNameSet(classes);

        foreach (var definition in classes)
        {
            roles[definition.Name] = IsTransaction(definition, names)
                ? EntityRole.Transaction
                : EntityRole.Master;
        }

        return new EntityRoleMap(
            roles, new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Both halves are required: an outgoing foreign key, AND either a date that is not an audit
    /// field or a name the gazetteer recognises.
    /// </summary>
    /// <remarks>
    /// The foreign key alone would make every child entity a transaction — an Address belonging to
    /// a Customer is not one. The other half alone would make an Employee with a HireDate one.
    /// </remarks>
    private static bool IsTransaction(ClassDefinition definition, HashSet<string> allNames)
    {
        if (ForeignKeys.TargetsOf(definition, allNames).Count == 0)
            return false;

        return HasNonAuditDate(definition) || TransactionWords.Contains(definition.Name);
    }

    private static bool HasNonAuditDate(ClassDefinition definition)
        => definition.Properties.Any(p =>
            string.Equals(p.Type, "DateTime", StringComparison.Ordinal)
            && !AuditFields.Contains(p.Name, StringComparer.OrdinalIgnoreCase));
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "EntityRolesTests|EntityRoleMapTests"`
Expected: PASS, 15 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Screens/ CodeGenerator.Tests/App/EntityRolesTests.cs
git commit -m "feat(screens): a transaction is a dated or transactionally-named entity with a foreign key"
```

---

### Task 3: Document lines

**Files:**
- Modify: `AppScaffolding/Screens/EntityRoles.cs`
- Test: `CodeGenerator.Tests/App/DocumentLineTests.cs`

**Interfaces:**
- Consumes: Task 2's `EntityRoles.Classify`.
- Produces: `EntityRoles.LineSuffixes` as `IReadOnlyList<string>` — `["Line", "Item", "Detail", "Row"]`. `Classify` now also fills the map's line lists.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding.Screens;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

/// <summary>
/// A line entity gets no screen of its own — it is edited inside its parent's editor. A false
/// positive therefore HIDES a screen, which is why the name test is strict rather than shape-based.
/// </summary>
[TestClass]
public class DocumentLineTests
{
    private static ClassDefinition Entity(string name, params (string Name, string Type)[] fields)
    {
        var definition = new ClassDefinition { Name = name };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        foreach (var (fieldName, type) in fields)
            definition.Properties.Add(new PropertyDefinition { Name = fieldName, Type = type });

        definition.Properties.Add(new PropertyDefinition { Name = "CreatedOn", Type = "DateTime" });
        definition.Properties.Add(new PropertyDefinition { Name = "ModifiedOn", Type = "DateTime" });
        return definition;
    }

    private static (string Name, string Type)[] ForeignKeyTo(string target)
        => [(target + "Id", "int"), (target, target)];

    /// <summary>Customer, Order, OrderLine — the shape the whole design is aimed at.</summary>
    private static List<ClassDefinition> OrderModel(string lineName)
        =>
        [
            Entity("Customer", ("Name", "string")),
            Entity("Order", [.. ForeignKeyTo("Customer"), ("OrderDate", "DateTime")]),
            Entity(lineName, [.. ForeignKeyTo("Order"), ("Quantity", "int"), ("UnitPrice", "decimal")]),
        ];

    [TestMethod]
    public void A_child_named_after_its_transaction_is_a_line()
    {
        var map = EntityRoles.Classify(OrderModel("OrderLine"));

        Assert.AreEqual(EntityRole.DocumentLine, map.RoleOf("OrderLine"));
        Assert.AreEqual(EntityRole.Transaction, map.RoleOf("Order"));
    }

    [TestMethod]
    public void The_transaction_reports_the_line_as_its_own()
    {
        var map = EntityRoles.Classify(OrderModel("OrderLine"));

        Assert.HasCount(1, map.LinesOf("Order"));
        Assert.AreEqual("OrderLine", map.LinesOf("Order")[0]);
    }

    [TestMethod]
    public void Every_line_suffix_is_recognised()
    {
        foreach (var suffix in new[] { "Line", "Item", "Detail", "Row" })
        {
            var map = EntityRoles.Classify(OrderModel("Order" + suffix));

            Assert.AreEqual(
                EntityRole.DocumentLine, map.RoleOf("Order" + suffix), $"suffix {suffix}");
        }
    }

    /// <summary>A bare suffix counts too: a child of Order called simply "LineItem" is still its
    /// lines.</summary>
    [TestMethod]
    public void A_name_that_merely_ends_in_a_suffix_is_a_line()
        => Assert.AreEqual(EntityRole.DocumentLine, EntityRoles.Classify(OrderModel("LineItem")).RoleOf("LineItem"));

    /// <summary>The strictness that matters. A child of a transaction whose name says nothing
    /// about lines keeps its own screen — hiding it would lose a screen silently.</summary>
    [TestMethod]
    public void A_child_of_a_transaction_with_an_unrelated_name_is_not_a_line()
    {
        var map = EntityRoles.Classify(OrderModel("Shipment"));

        Assert.AreNotEqual(EntityRole.DocumentLine, map.RoleOf("Shipment"));
        Assert.IsEmpty(map.LinesOf("Order"));
    }

    /// <summary>A line-sounding child of a MASTER is not a line: there is no document to put it
    /// inside, so it keeps a screen.</summary>
    [TestMethod]
    public void A_line_named_child_of_a_master_is_not_a_line()
    {
        var classes = new List<ClassDefinition>
        {
            Entity("Customer", ("Name", "string")),
            Entity("CustomerLine", [.. ForeignKeyTo("Customer"), ("Note", "string")]),
        };

        Assert.AreNotEqual(
            EntityRole.DocumentLine, EntityRoles.Classify(classes).RoleOf("CustomerLine"));
    }

    /// <summary>A line never appears in the navigable entities, which is the whole point of the
    /// role.</summary>
    [TestMethod]
    public void Lines_are_not_listed_as_masters_or_transactions()
    {
        var map = EntityRoles.Classify(OrderModel("OrderLine"));

        CollectionAssert.DoesNotContain(map.Names(EntityRole.Master).ToList(), "OrderLine");
        CollectionAssert.DoesNotContain(map.Names(EntityRole.Transaction).ToList(), "OrderLine");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter DocumentLineTests`
Expected: FAIL — lines are classified as transactions or masters, and `LinesOf` is empty.

- [ ] **Step 3: Write the implementation**

Replace `Classify` in `AppScaffolding/Screens/EntityRoles.cs` and add the suffix list:

```csharp
    /// <summary>What a line entity's name ends in, or is named after its parent plus.</summary>
    /// <remarks>
    /// A strict NAME test, deliberately, not a shape test. A line gets no screen of its own, so a
    /// false positive hides one — and a hidden screen is far harder to notice than an extra one.
    /// </remarks>
    public static IReadOnlyList<string> LineSuffixes { get; } = ["Line", "Item", "Detail", "Row"];

    /// <summary>The role of every class in <paramref name="classes"/>.</summary>
    /// <remarks>
    /// Two passes, because lines are defined in terms of transactions: nothing can be recognised
    /// as a line until its parent is known to be one.
    /// </remarks>
    public static EntityRoleMap Classify(IReadOnlyList<ClassDefinition> classes)
    {
        var roles = new Dictionary<string, EntityRole>(StringComparer.OrdinalIgnoreCase);
        var names = ForeignKeys.ClassNameSet(classes);

        foreach (var definition in classes)
        {
            roles[definition.Name] = IsTransaction(definition, names)
                ? EntityRole.Transaction
                : EntityRole.Master;
        }

        var lines = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in classes)
        {
            var parent = LineParentOf(definition, names, roles);

            if (parent is null)
                continue;

            roles[definition.Name] = EntityRole.DocumentLine;

            var existing = lines.TryGetValue(parent, out var already)
                ? already.ToList()
                : [];

            existing.Add(definition.Name);
            lines[parent] = existing;
        }

        return new EntityRoleMap(roles, lines);
    }

    /// <summary>
    /// The transaction <paramref name="definition"/> is the lines of, or null when it is not lines.
    /// </summary>
    /// <remarks>
    /// Both conditions are required: a foreign key to something already classed as a Transaction,
    /// and a name that says "lines". A child of a MASTER called CustomerLine is not lines, because
    /// there is no document to edit it inside.
    /// </remarks>
    private static string? LineParentOf(
        ClassDefinition definition,
        HashSet<string> allNames,
        IReadOnlyDictionary<string, EntityRole> roles)
    {
        if (!LooksLikeLines(definition.Name))
            return null;

        foreach (var target in ForeignKeys.TargetsOf(definition, allNames))
        {
            if (roles.TryGetValue(target, out var role) && role == EntityRole.Transaction)
                return target;
        }

        return null;
    }

    private static bool LooksLikeLines(string name)
        => LineSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "DocumentLineTests|EntityRolesTests|EntityRoleMapTests"`
Expected: PASS, 23 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Screens/EntityRoles.cs CodeGenerator.Tests/App/DocumentLineTests.cs
git commit -m "feat(screens): a transaction's lines are found by name, strictly"
```

---

### Task 4: The overrides

**Files:**
- Create: `AppScaffolding/Screens/ScreenOverrides.cs`
- Modify: `AppScaffolding/AppCustomization.cs`, `AppScaffolding/Screens/EntityRoles.cs`, `CodeGenerator/App/InlineCustomization.cs`
- Test: `CodeGenerator.Tests/App/ScreenOverridesTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces: `AppCustomization.Transactions` and `AppCustomization.Masters`, both `string`, both `string.Empty` by default, positioned after `Login`. `ScreenOverrides.Parse(string transactions, string masters) -> ScreenOverrides` with `bool IsTransaction(string name)`, `bool IsMaster(string name)`, `bool Any`. `EntityRoles.Classify(classes, ScreenOverrides overrides)` — the single-argument overload stays and passes `ScreenOverrides.None`.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using AppScaffolding.Screens;
using CodeGenerationEngine.Models;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>The heuristic will be wrong sometimes; this is how a user says so. Two value keys,
/// comma-joined without spaces, because the parser splits on whitespace.</summary>
[TestClass]
public class ScreenOverridesTests
{
    private static ClassDefinition Entity(string name, params (string Name, string Type)[] fields)
    {
        var definition = new ClassDefinition { Name = name };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        foreach (var (fieldName, type) in fields)
            definition.Properties.Add(new PropertyDefinition { Name = fieldName, Type = type });

        definition.Properties.Add(new PropertyDefinition { Name = "CreatedOn", Type = "DateTime" });
        definition.Properties.Add(new PropertyDefinition { Name = "ModifiedOn", Type = "DateTime" });
        return definition;
    }

    private static (string Name, string Type)[] ForeignKeyTo(string target)
        => [(target + "Id", "int"), (target, target)];

    private static List<ClassDefinition> Model() =>
    [
        Entity("Customer", ("Name", "string")),
        Entity("Region", ("Name", "string")),
        Entity("Order", [.. ForeignKeyTo("Customer"), ("OrderDate", "DateTime")]),
    ];

    [TestMethod]
    public void The_defaults_override_nothing()
    {
        Assert.AreEqual(string.Empty, AppCustomization.Default.Transactions);
        Assert.AreEqual(string.Empty, AppCustomization.Default.Masters);
        Assert.IsFalse(ScreenOverrides.None.Any);
    }

    [TestMethod]
    public void A_named_transaction_becomes_one_even_with_no_foreign_key()
    {
        var overrides = ScreenOverrides.Parse("Region", string.Empty);

        Assert.AreEqual(EntityRole.Transaction, EntityRoles.Classify(Model(), overrides).RoleOf("Region"));
    }

    [TestMethod]
    public void A_named_master_stays_a_master_even_when_the_heuristic_disagrees()
    {
        var overrides = ScreenOverrides.Parse(string.Empty, "Order");

        Assert.AreEqual(EntityRole.Master, EntityRoles.Classify(Model(), overrides).RoleOf("Order"));
    }

    /// <summary>Naming a line entity as a master promotes it to a screen of its own — the spec
    /// says this is the only correction anyone would want to make to line detection.</summary>
    [TestMethod]
    public void Naming_a_line_as_a_master_gives_it_its_own_screen()
    {
        var classes = new List<ClassDefinition>
        {
            Entity("Customer", ("Name", "string")),
            Entity("Order", [.. ForeignKeyTo("Customer"), ("OrderDate", "DateTime")]),
            Entity("OrderLine", [.. ForeignKeyTo("Order"), ("Quantity", "int")]),
        };

        var map = EntityRoles.Classify(classes, ScreenOverrides.Parse(string.Empty, "OrderLine"));

        Assert.AreEqual(EntityRole.Master, map.RoleOf("OrderLine"));
        Assert.IsEmpty(map.LinesOf("Order"), "a promoted line is no longer its parent's lines");
    }

    [TestMethod]
    public void Several_names_are_comma_separated()
    {
        var overrides = ScreenOverrides.Parse("Region,Customer", string.Empty);

        Assert.IsTrue(overrides.IsTransaction("Region"));
        Assert.IsTrue(overrides.IsTransaction("Customer"));
    }

    /// <summary>Typed by a person, so spaces after commas and odd casing both have to work even
    /// though the parser's own tokenizer will not have allowed spaces through unquoted.</summary>
    [TestMethod]
    public void Names_are_trimmed_and_matched_without_regard_to_case()
    {
        var overrides = ScreenOverrides.Parse("region, CUSTOMER", string.Empty);

        Assert.IsTrue(overrides.IsTransaction("Region"));
        Assert.IsTrue(overrides.IsTransaction("Customer"));
    }

    /// <summary>Naming the same entity both ways is a contradiction. Master wins, because it is
    /// the safe answer: an entity wrongly made a master is an ordinary grid.</summary>
    [TestMethod]
    public void Master_wins_when_a_name_is_given_to_both()
    {
        var overrides = ScreenOverrides.Parse("Order", "Order");

        Assert.AreEqual(EntityRole.Master, EntityRoles.Classify(Model(), overrides).RoleOf("Order"));
    }

    /// <summary>A name that matches nothing in the model is ignored rather than fatal: the user
    /// typed it before discovery ran and may simply have misremembered.</summary>
    [TestMethod]
    public void A_name_that_matches_no_entity_changes_nothing()
    {
        var map = EntityRoles.Classify(Model(), ScreenOverrides.Parse("Nonexistent", string.Empty));

        Assert.AreEqual(EntityRole.Master, map.RoleOf("Customer"));
        Assert.AreEqual(EntityRole.Transaction, map.RoleOf("Order"));
    }

    // ---- the inline parser ----

    [TestMethod]
    public void The_inline_options_are_read_off_the_line()
    {
        var (remaining, customization) = InlineCustomization.Split(
            "a shop transactions Order,Invoice masters Region,Category", AppCustomization.Default);

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual("Order,Invoice", customization.Transactions);
        Assert.AreEqual("Region,Category", customization.Masters);
    }

    /// <summary>Options are stripped right to left, so these have to sit happily beside the
    /// flags and the other value keys.</summary>
    [TestMethod]
    public void They_read_beside_the_other_options()
    {
        var (remaining, customization) = InlineCustomization.Split(
            "a shop company Codis transactions Order nologin", AppCustomization.Default);

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual("Order", customization.Transactions);
        Assert.AreEqual("Codis", customization.Company);
        Assert.IsFalse(customization.Login);
    }

    /// <summary>An empty value is not usable, and an unusable value leaves the line alone rather
    /// than silently eating two tokens — the rule ApplyValue already follows for `theme mauve`.</summary>
    [TestMethod]
    public void An_empty_value_is_not_taken()
    {
        var (remaining, customization) =
            InlineCustomization.Split("a shop transactions \"\"", AppCustomization.Default);

        Assert.AreEqual("a shop transactions \"\"", remaining);
        Assert.AreEqual(string.Empty, customization.Transactions);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter ScreenOverridesTests`
Expected: FAIL — `AppCustomization` has no `Transactions`, and `ScreenOverrides` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Screens/ScreenOverrides.cs`:

```csharp
namespace AppScaffolding.Screens;

/// <summary>
/// The entities the user named, overruling the heuristic.
/// </summary>
/// <remarks>
/// Parsed from two comma-joined strings rather than carried as collections, because the strings
/// live on <c>AppCustomization</c> — a record compared by value in the existing tests, where a
/// list member would break equality silently.
/// </remarks>
public sealed class ScreenOverrides
{
    private readonly HashSet<string> _transactions;
    private readonly HashSet<string> _masters;

    private ScreenOverrides(HashSet<string> transactions, HashSet<string> masters)
    {
        _transactions = transactions;
        _masters = masters;
    }

    /// <summary>Nothing overridden — the heuristic decides everything.</summary>
    public static ScreenOverrides None { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Whether the user named anything at all.</summary>
    public bool Any => _transactions.Count > 0 || _masters.Count > 0;

    public static ScreenOverrides Parse(string transactions, string masters)
        => new(Split(transactions), Split(masters));

    /// <summary>Whether the user asked for <paramref name="name"/> to be a transaction.</summary>
    /// <remarks>
    /// False when the same name was also given as a master. Naming an entity both ways is a
    /// contradiction, and master is the safe half to honour: an entity wrongly made a master
    /// renders as the ordinary grid this generator has always produced.
    /// </remarks>
    public bool IsTransaction(string name)
        => _transactions.Contains(name) && !_masters.Contains(name);

    public bool IsMaster(string name) => _masters.Contains(name);

    private static HashSet<string> Split(string value)
        => new(
            (value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
}
```

In `AppScaffolding/AppCustomization.cs`, add after `Login`:

```csharp
    bool Login,
    string Transactions,
    string Masters,
    string NamespacePrefix,
```

and in `Default`:

```csharp
        Login: true,
        Transactions: string.Empty,
        Masters: string.Empty,
        NamespacePrefix: string.Empty,
```

with the parameter documented on the record:

```csharp
/// <param name="Transactions">
/// Entity names the user declared to be transactions, comma-joined, overruling the heuristic. A
/// string rather than a collection because this record is compared by value and a list member
/// would break that silently.
/// </param>
/// <param name="Masters">As <paramref name="Transactions"/>, in the other direction. A name given
/// in both wins as a master, which is the safe answer.</param>
```

In `AppScaffolding/Screens/EntityRoles.cs`, add the overload and honour the overrides:

```csharp
    /// <summary>The role of every class, with the heuristic deciding everything.</summary>
    public static EntityRoleMap Classify(IReadOnlyList<ClassDefinition> classes)
        => Classify(classes, ScreenOverrides.None);

    /// <summary>The role of every class, with <paramref name="overrides"/> overruling the
    /// heuristic.</summary>
    public static EntityRoleMap Classify(
        IReadOnlyList<ClassDefinition> classes, ScreenOverrides overrides)
    {
        var roles = new Dictionary<string, EntityRole>(StringComparer.OrdinalIgnoreCase);
        var names = ForeignKeys.ClassNameSet(classes);

        foreach (var definition in classes)
        {
            roles[definition.Name] = Role(definition, names, overrides);
        }

        var lines = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in classes)
        {
            // A name the user pulled out to a master keeps its own screen, and so is not lines.
            // This is the only correction anyone would want to make to line detection.
            if (overrides.IsMaster(definition.Name))
                continue;

            var parent = LineParentOf(definition, names, roles);

            if (parent is null)
                continue;

            roles[definition.Name] = EntityRole.DocumentLine;

            var existing = lines.TryGetValue(parent, out var already) ? already.ToList() : [];
            existing.Add(definition.Name);
            lines[parent] = existing;
        }

        return new EntityRoleMap(roles, lines);
    }

    private static EntityRole Role(
        ClassDefinition definition, HashSet<string> names, ScreenOverrides overrides)
    {
        if (overrides.IsMaster(definition.Name))
            return EntityRole.Master;

        if (overrides.IsTransaction(definition.Name))
            return EntityRole.Transaction;

        return IsTransaction(definition, names) ? EntityRole.Transaction : EntityRole.Master;
    }
```

In `CodeGenerator/App/InlineCustomization.cs`, extend the value keys:

```csharp
    private static readonly string[] ValueKeys =
        ["company", "theme", "accent", "icon", "title", "namespace", "layout",
         "transactions", "masters"];
```

and add to `ApplyValue`'s switch, before the closing default:

```csharp
            case "transactions":
                return customization with { Transactions = value };

            case "masters":
                return customization with { Masters = value };
```

Fix the one positional construction of `AppCustomization` this breaks — `CustomizationDefaults.Load` — by adding `Transactions` and `Masters` to its `Profile`, its `Load` and its `Save`, exactly as `Login` was added.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "ScreenOverridesTests|InlineCustomizationTests|CustomizationDefaultsTests|CustomizationMergeTests|LoginFlagTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/ CodeGenerator/App/ CodeGenerator.Tests/App/ScreenOverridesTests.cs
git commit -m "feat(build app): transactions and masters name what the heuristic got wrong"
```

---

### Task 5: The remaining override surfaces

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs` (`InlineOptionsHelp`), `CodeGenerator/App/CustomizationSummary.cs`, `CodeGenerator/App/CustomizationWizard.cs`, `CodeGenerator/App/CustomizationMenus.cs`, `CodeGenerator/Web/Workflow/BuildCommandEcho.cs`, `CodeGenerationEngine/Core/CommandDictionary.cs`
- Modify: `docs/commands.md` — **regenerated, never hand-edited**
- Test: append to `CodeGenerator.Tests/App/ScreenOverridesTests.cs`; `CodeGenerator.Tests/Core/CommandDictionaryDataTests.cs`

**Interfaces:**
- Consumes: Task 4's `AppCustomization.Transactions` / `.Masters`.
- Produces: no new API.

- [ ] **Step 1: Write the failing test**

Append to `ScreenOverridesTests.cs`:

```csharp
    [TestMethod]
    public void The_inline_options_help_line_lists_both_keys()
    {
        StringAssert.Contains(BuildAppCommand.InlineOptionsHelp, "transactions");
        StringAssert.Contains(BuildAppCommand.InlineOptionsHelp, "masters");
    }

    [TestMethod]
    public void The_summary_reports_the_overrides()
    {
        var lines = CustomizationSummary.Lines(
            AppCustomization.Default with { Transactions = "Order", Masters = "Region" });

        Assert.IsTrue(lines.Any(l => l.Contains("Order", StringComparison.Ordinal)));
        Assert.IsTrue(lines.Any(l => l.Contains("Region", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void The_summary_stays_silent_when_nothing_is_overridden()
        => Assert.IsFalse(
            CustomizationSummary.Lines(AppCustomization.Default)
                .Any(l => l.Contains("ransaction", StringComparison.Ordinal)));
```

In `CodeGenerator.Tests/Web/BuildCommandEchoTests.cs`, extend `Every_customization_the_page_can_set_comes_back_as_itself` — add to the `asked` initialiser:

```csharp
            Transactions = "Order",
            Masters = "Region",
```

and to its assertions:

```csharp
        Assert.AreEqual(asked.Transactions, parsed.Customization.Transactions);
        Assert.AreEqual(asked.Masters, parsed.Customization.Masters);
```

In `CodeGenerator.Tests/Core/CommandDictionaryDataTests.cs`, add `"transactions"` and `"masters"` to the expected-token list.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "ScreenOverridesTests|BuildCommandEchoTests|CommandDictionaryDataTests"`
Expected: FAIL — none of these surfaces mentions the keys.

- [ ] **Step 3: Write the implementation**

`BuildAppCommand.InlineOptionsHelp` — extend the constant:

```csharp
    internal const string InlineOptionsHelp =
        "Options (last on the line): company \"<name>\"  theme light|dark  "
        + "accent #rrggbb  icon <path>  title \"<pattern>\"  "
        + "transactions <A,B>  masters <A,B>  "
        + "novalidation  logging  noseed  nologin";
```

`CustomizationSummary.Lines` — beside the layout line, before the features list:

```csharp
        // Named, not counted: the point of typing them is to check the generator agreed.
        if (customization.Transactions.Length > 0)
            lines.Add($"  Transactions: {customization.Transactions}");

        if (customization.Masters.Length > 0)
            lines.Add($"  Masters: {customization.Masters}");
```

`BuildCommandEcho.Options` — beside the layout echo:

```csharp
        if (customization.Transactions.Length > 0)
            options += $" transactions {customization.Transactions}";

        if (customization.Masters.Length > 0)
            options += $" masters {customization.Masters}";
```

`CustomizationWizard` and `CustomizationMenus` — ask for both, next to the layout question, as free text with blank meaning automatic. Follow the shape the neighbouring value questions use in each file; do not invent a new prompt mechanism. Wording:

> `Transactions (comma-separated, blank to decide automatically)`
> `Masters (comma-separated, blank to decide automatically)`

Guard each answer for null the way the neighbouring questions do — a cancelled prompt returns null and must return null from the whole wizard, not `.Value` into a `NullReferenceException`. **Release with `-warnaserror` is what catches a missing guard here (CS8629); Debug will not.**

`CommandDictionary` — a new entry in the Build App category:

```csharp
        new()
        {
            Category = "Build App",
            Command = "build app: <description> [transactions <A,B>] [masters <A,B>]",
            Description = "Overrules how the generator split your model into screens. It decides "
                        + "by itself which classes are transactions (records with a foreign key "
                        + "and a date or a transactional name) and which are masters (everything "
                        + "else); these name the ones it got wrong. Comma-joined with no spaces, "
                        + "or quoted. Naming a line entity as a master gives it a screen of its "
                        + "own instead of editing it inside its parent.",
            Example = "build app: a shop transactions Order,Invoice masters Region"
        },
```

Then regenerate the manual table rather than editing it:

```bash
UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter CommandsDoc_MatchesTheCatalog
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "ScreenOverridesTests|BuildCommandEchoTests|CommandDictionaryDataTests|CustomizationWizardTests|CustomizationMenuTests|CommandReferenceDocTests"`
Expected: PASS. The wizard's scripted-answer fixtures each gain two keystrokes, and `CustomizationMenuTests.Questions` gains two entries; `Every_prompt_puts_its_caret_in_the_same_column` asserts a question count that must go up by two.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/ CodeGenerationEngine/Core/CommandDictionary.cs docs/commands.md CodeGenerator.Tests/
git commit -m "docs(build app): transactions and masters appear on every surface"
```

---

### Task 6: Tell the user what was decided

**Files:**
- Create: `AppScaffolding/Screens/ScreenSummary.cs`
- Modify: `AppScaffolding/ScaffoldResult.cs`, `AppScaffolding/AppScaffolder.cs`, `CodeGenerator/App/BuildAppCommand.cs`
- Test: `CodeGenerator.Tests/App/ScreenSummaryTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces: `ScreenSummary.Describe(EntityRoleMap map) -> string` — e.g. `"3 masters, 2 transactions (Order has 2 lines)"`, or `string.Empty` for an empty model. `ScaffoldResult.ScreenSummary` (`string`, defaulted to `""`, added as the last positional parameter so existing construction sites are untouched).

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using AppScaffolding.Screens;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.App;

/// <summary>A silent misclassification is discoverable only by launching the app, so the build
/// says what it decided.</summary>
[TestClass]
public class ScreenSummaryTests
{
    private static ClassDefinition Entity(string name, params (string Name, string Type)[] fields)
    {
        var definition = new ClassDefinition { Name = name };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        foreach (var (fieldName, type) in fields)
            definition.Properties.Add(new PropertyDefinition { Name = fieldName, Type = type });

        definition.Properties.Add(new PropertyDefinition { Name = "CreatedOn", Type = "DateTime" });
        definition.Properties.Add(new PropertyDefinition { Name = "ModifiedOn", Type = "DateTime" });
        return definition;
    }

    private static (string Name, string Type)[] ForeignKeyTo(string target)
        => [(target + "Id", "int"), (target, target)];

    [TestMethod]
    public void It_counts_both_kinds()
    {
        var map = EntityRoles.Classify(
        [
            Entity("Customer", ("Name", "string")),
            Entity("Product", ("Name", "string")),
            Entity("Order", [.. ForeignKeyTo("Customer"), ("OrderDate", "DateTime")]),
        ]);

        var summary = ScreenSummary.Describe(map);

        StringAssert.Contains(summary, "2 masters");
        StringAssert.Contains(summary, "1 transaction");
    }

    /// <summary>Singular and plural both read properly; "1 masters" is the kind of detail that
    /// makes generated output look machine-made.</summary>
    [TestMethod]
    public void One_of_something_is_singular()
    {
        var map = EntityRoles.Classify([Entity("Customer", ("Name", "string"))]);

        StringAssert.Contains(ScreenSummary.Describe(map), "1 master");
        Assert.IsFalse(ScreenSummary.Describe(map).Contains("1 masters", StringComparison.Ordinal));
    }

    /// <summary>Lines are the detail worth naming: they are the entities that got NO screen, and
    /// the user cannot otherwise tell that happened.</summary>
    [TestMethod]
    public void A_transaction_with_lines_says_so_by_name()
    {
        var map = EntityRoles.Classify(
        [
            Entity("Customer", ("Name", "string")),
            Entity("Order", [.. ForeignKeyTo("Customer"), ("OrderDate", "DateTime")]),
            Entity("OrderLine", [.. ForeignKeyTo("Order"), ("Quantity", "int")]),
        ]);

        StringAssert.Contains(ScreenSummary.Describe(map), "Order has 1 line");
    }

    [TestMethod]
    public void An_empty_model_says_nothing()
        => Assert.AreEqual(string.Empty, ScreenSummary.Describe(EntityRoleMap.Empty));

    /// <summary>It reaches the caller, not just the pure function.</summary>
    [TestMethod]
    public void The_scaffold_result_carries_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "sum-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        var result = new AppScaffolder().Scaffold(
            ScaffoldRequest.For(TestModels.OneEntity(), "Shop", root, new WpfTarget()));

        StringAssert.Contains(result.ScreenSummary, "1 master");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter ScreenSummaryTests`
Expected: FAIL — `ScreenSummary` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Screens/ScreenSummary.cs`:

```csharp
namespace AppScaffolding.Screens;

/// <summary>One line saying how the model was split into screens.</summary>
/// <remarks>
/// A misclassification is otherwise discoverable only by launching the application and noticing a
/// screen is missing or shaped wrongly. Line entities are named individually for exactly that
/// reason: they are the ones that got no screen at all.
/// </remarks>
public static class ScreenSummary
{
    public static string Describe(EntityRoleMap map)
    {
        var masters = map.Names(EntityRole.Master).Count;
        var transactions = map.Names(EntityRole.Transaction).Count;

        if (masters == 0 && transactions == 0)
            return string.Empty;

        var summary = $"{Count(masters, "master")}, {Count(transactions, "transaction")}";

        var withLines = map.Names(EntityRole.Transaction)
            .Where(name => map.LinesOf(name).Count > 0)
            .Select(name => $"{name} has {Count(map.LinesOf(name).Count, "line")}")
            .ToList();

        return withLines.Count == 0
            ? summary
            : $"{summary} ({string.Join(", ", withLines)})";
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
```

In `AppScaffolding/ScaffoldResult.cs`, add the last parameter:

```csharp
public sealed record ScaffoldResult(
    bool Success,
    string ProjectPath,
    IReadOnlyList<string> FilesWritten,
    string Message,
    string? TestProjectPath = null,
    string ScreenSummary = "");
```

In `AppScaffolder`, classify beside the existing `DomainModelMapper.ToClassDefinitions` call and put the summary on every result it returns:

```csharp
        var roles = EntityRoles.Classify(
            classes, ScreenOverrides.Parse(customization.Transactions, customization.Masters));

        var screenSummary = ScreenSummary.Describe(roles);
```

In `BuildAppCommand`, print it after the generation message, when it is not empty:

```csharp
        if (scaffold.ScreenSummary.Length > 0)
            Console.WriteLine("  Screens: " + scaffold.ScreenSummary);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "ScreenSummaryTests|AppScaffolderWriteTests|ScaffoldRequestTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/ CodeGenerator/App/BuildAppCommand.cs CodeGenerator.Tests/App/ScreenSummaryTests.cs
git commit -m "feat(build app): the build says how it split the model into screens"
```

---

### Task 7: The seam that carries roles to a target

**Files:**
- Create: `AppScaffolding/FrontEndContext.cs`
- Modify: `AppScaffolding/ITargetScaffolder.cs`, `AppScaffolding/AppScaffolder.cs`
- Test: `CodeGenerator.Tests/App/FrontEndContextTests.cs`

**Interfaces:**
- Consumes: Tasks 1–6.
- Produces: `sealed record FrontEndContext(string ProjectName, IReadOnlyList<string> EntityNames, AppCustomization Customization, EntityRoleMap Roles)`. `ITargetScaffolder.FrontEndFiles(FrontEndContext context)` as a default interface method delegating to `FrontEndFiles(context.ProjectName, context.EntityNames, context.Customization)`. `AppScaffolder` calls the new overload.

**Why this is here and not in branch 3:** nothing consumes `Roles` yet. It lands now because branch 3 would otherwise open by changing an interface all seven targets implement, and doing that while it is a pure addition with no behaviour attached keeps that change out of a branch already large. This is the one task in this plan that could be deferred without loss; if a reviewer objects, moving it costs nothing.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using AppScaffolding.Screens;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The seam branch 3 needs: a target that wants roles as well as names has somewhere to read them.
/// </summary>
/// <remarks>
/// The default implementation delegates, so the four targets with no screens — and the three that
/// have them but do not yet use roles — are untouched by its arrival.
/// </remarks>
[TestClass]
public class FrontEndContextTests
{
    private static FrontEndContext Context(AppCustomization? customization = null)
        => new("Shop", ["Order"], customization ?? AppCustomization.Default, EntityRoleMap.Empty);

    /// <summary>Every target answers the new overload, whether or not it knows about it.</summary>
    [TestMethod]
    public void Every_target_produces_the_same_files_through_either_overload()
    {
        foreach (ITargetScaffolder target in new ITargetScaffolder[]
                 {
                     new WpfTarget(), new WinFormsTarget(), new BlazorTarget(),
                     new ConsoleTarget(), new WebApiTarget(), new WorkerTarget(),
                     new ClassLibraryTarget(),
                 })
        {
            var viaContext = target.FrontEndFiles(Context()).Select(f => f.Name).ToList();

            var viaNames = target
                .FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
                .Select(f => f.Name).ToList();

            CollectionAssert.AreEqual(viaNames, viaContext, target.Key);
        }
    }

    /// <summary>The customization travels, or a target reading the context would silently lose
    /// every option the user chose.</summary>
    [TestMethod]
    public void The_customization_reaches_the_target_through_the_context()
    {
        var noLogin = AppCustomization.Default with { Login = false };

        var names = new WpfTarget().FrontEndFiles(Context(noLogin)).Select(f => f.Name).ToList();

        CollectionAssert.DoesNotContain(names, "LoginWindow.xaml");
    }

    [TestMethod]
    public void The_roles_are_carried_rather_than_recomputed()
    {
        var roles = EntityRoles.Classify([]);

        Assert.AreSame(roles, new FrontEndContext("Shop", ["Order"], AppCustomization.Default, roles).Roles);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FrontEndContextTests`
Expected: FAIL — `FrontEndContext` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/FrontEndContext.cs`:

```csharp
using AppScaffolding.Screens;

namespace AppScaffolding;

/// <summary>
/// Everything a target needs to emit its front end, once names alone are not enough.
/// </summary>
/// <remarks>
/// <see cref="ITargetScaffolder.FrontEndFiles(string, IReadOnlyList{string}, AppCustomization)"/>
/// is handed entity <i>names</i>, which cannot say whether an entity is a master or a transaction.
/// Rather than widen that signature for the fourth time, the arguments become a record — the same
/// move <see cref="ScaffoldRequest"/> made when its overload chain reached four.
/// </remarks>
public sealed record FrontEndContext(
    string ProjectName,
    IReadOnlyList<string> EntityNames,
    AppCustomization Customization,
    EntityRoleMap Roles);
```

In `ITargetScaffolder`, add beside the existing customization overload:

```csharp
    /// <summary>
    /// As the overloads above, for a target whose front end varies with the entities' roles.
    /// </summary>
    /// <remarks>
    /// A default interface method like its neighbours: a target that does not care about roles
    /// needs no change, and the four screenless ones never will.
    /// </remarks>
    IReadOnlyList<GeneratedFile> FrontEndFiles(FrontEndContext context)
        => FrontEndFiles(context.ProjectName, context.EntityNames, context.Customization);
```

In `AppScaffolder`, call the new overload where the old one is called:

```csharp
        foreach (var file in target.FrontEndFiles(
                     new FrontEndContext(projectName, entityNames, customization, roles)))
            files.Add((Path.Combine(outputFolder, file.RelativeFolder), file.Name, file.Source));
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FrontEndContextTests|BlazorTargetTests|ConsoleTargetTests|ClassLibraryTargetTests"`
Expected: PASS.

- [ ] **Step 5: Verify nothing in the emitted output moved**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter GeneratedProjectBuildTests`
Expected: PASS, 38 rows. This task changes how every target is *called*; the build tests are what prove none of them now emits something different.

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/ CodeGenerator.Tests/App/FrontEndContextTests.cs
git commit -m "feat(scaffolding): a target can be handed roles as well as names"
```

---

### Task 8: The manual

**Files:**
- Modify: `docs/user-manual-source.html`, then rebuild `docs/SmartCodeGenerator-User-Manual.docx`

**Interfaces:**
- Consumes: everything above.
- Produces: documentation only.

- [ ] **Step 1: Add the section**

Add a `<h3>` under the `build app` chapter explaining, in the manual's voice: that the generator splits the discovered model into masters and transactions; what it decides on (a foreign key plus a date or a transactional name); that line entities are edited inside their parent and get no screen; that the build prints what it decided; and that `transactions A,B` and `masters A,B` correct it. Include the worked example:

```
build app: a shop that sells books transactions Order masters Region
```

- [ ] **Step 2: Rebuild the .docx**

`$PSScriptRoot` comes out **empty** when these scripts are launched from the Bash tool, so pass absolute paths:

```bash
powershell -NoProfile -ExecutionPolicy Bypass \
  -File "D:\SmartCodeGenerator-Repository\tools\build-user-manual.ps1" \
  -Source "D:\SmartCodeGenerator-Repository\docs\user-manual-source.html" \
  -Output "D:\SmartCodeGenerator-Repository\docs\SmartCodeGenerator-User-Manual.docx"
```

- [ ] **Step 3: Verify it**

```bash
powershell -NoProfile -ExecutionPolicy Bypass \
  -File "D:\SmartCodeGenerator-Repository\tools\verify-user-manual.ps1" \
  -Document "D:\SmartCodeGenerator-Repository\docs\SmartCodeGenerator-User-Manual.docx"
```

Expected: "All checks passed", with the chapter count one higher than before if a new `<h2>` was added, and TOC links matching heading bookmarks.

- [ ] **Step 4: Full verification in the CI configuration**

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```

Expected: build clean, every test passing.

- [ ] **Step 5: Commit**

```bash
git add docs/
git commit -m "docs: the manual explains how the model is split into screens"
```

---

## Self-Review

**Spec coverage.** Every claim in the spec's Classification section maps to a task: three roles (1), the audit-field trap and both halves of the transaction rule (2), the strict line-name test (3), the two override keys and the master-promotes-a-line rule (4), the help catalogue and manual (5, 8), the build summary (6), and the `FrontEndContext` seam (7).

**Deliberately not here.** No screen renders in this branch — no `IEntityViewModel.Role`, no `LayoutContext` change, no templates. Those are branch 3, and a task that starts wanting one has wandered.

**One judgement recorded rather than hidden.** Task 7 builds a seam with no consumer. It is here to keep an interface change affecting seven targets out of an already-large branch 3, and the task says so, so a reviewer can move it instead of discovering the motive by archaeology.

**Two things the implementer must look up rather than trust.** The exact shape of the neighbouring value-question in `CustomizationWizard` and `CustomizationMenus` (Task 5) — the wording is specified, the mechanism is not, because guessing it in branch 1 produced a null-guard bug that only Release `-warnaserror` caught. And the current question count asserted by `Every_prompt_puts_its_caret_in_the_same_column`, which goes up by two.

---

**Plan complete and saved to `docs/superpowers/plans/2026-08-20-professional-app-layouts-classification.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
