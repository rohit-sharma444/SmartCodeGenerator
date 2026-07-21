# Generated-app validation & relationship surfacing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `build app:` output validate its data and let users pick related records instead of typing raw foreign-key ids.

**Architecture:** Inferred DataAnnotations are added to model classes in `DomainModelMapper` (via the generator's existing `Rules`/`Attributes`/`Usings` channels). The two interactive front-ends (console, WPF) enforce those annotations with `Validator.TryValidateObject` before persisting, and render a foreign-key picker (a numbered console list; a `DataGridComboBoxColumn` in WPF) in place of the raw id. All front-end code stays generic/reflection-driven — no per-entity code.

**Tech Stack:** .NET 10, C#, Roslyn (`RoslynClassGenerator`), MSTest, WPF, `System.ComponentModel.DataAnnotations`.

## Global Constraints

- Target framework: `net10.0`. SDK: .NET 10+.
- The build must be warning-free: CI runs with `-warnaserror`.
- `dotnet test` must be green before every commit.
- No new NuGet dependencies (`DataAnnotations` ships in the framework).
- Do not change `CandidateField`, the DomainDiscovery pipeline, or the NLE.
- The shared Models/Data core keeps the same file set and class shapes; only property attributes are added.
- Piped/redirected `build app:` … `approve` must be unchanged (no new prompts on that path).
- WPF automated tests are **structure-only** (`StringAssert` on template output); WPF cannot compile in Linux CI. WPF build is verified locally on Windows during execution.
- Tests use MSTest (`[TestClass]`/`[TestMethod]`, `Assert`/`CollectionAssert`/`StringAssert`); the test assembly's global usings already import the MSTest namespace.
- Run tests from the repo root `D:\SmartCodeGenerator-Repository`. Filter example: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~DomainModelMapperValidation"`.

---

### Task 1: Inferred validation attributes in `DomainModelMapper`

**Files:**
- Modify: `AppScaffolding/DomainModelMapper.cs`
- Test: `CodeGenerator.Tests/App/DomainModelMapperTests.cs` (add a new `[TestClass]`)

**Interfaces:**
- Consumes: `DomainModelMapper.ToClassDefinitions(CandidateDomainModel)` → `IReadOnlyList<ClassDefinition>` (existing). `PropertyDefinition.Rules` (`List<PropertyRule>`), `PropertyDefinition.Attributes` (`List<string>`), `ClassDefinition.Usings` (`List<string>`). `PropertyRule { string Name; int? MaxLength; }`. Generator renders `Name=="required"`→`[Required]`, `Name=="maxlength"`+`MaxLength`→`[StringLength(n)]`; bare `Attributes` entries → `[Name]`.
- Produces: model classes whose properties carry inferred DataAnnotations, and a `System.ComponentModel.DataAnnotations` using on any class that gained one.

- [ ] **Step 1: Write the failing tests**

Append to `CodeGenerator.Tests/App/DomainModelMapperTests.cs`:

```csharp
[TestClass]
public class DomainModelMapperValidationTests
{
    private static CandidateDomainModel EmployeeModel()
    {
        var model = new CandidateDomainModel();
        var employee = new CandidateEntity { Name = "Employee" };
        employee.Fields.Add(new CandidateField { Name = "Name", Type = "string" });
        employee.Fields.Add(new CandidateField { Name = "WorkEmail", Type = "string" });
        employee.Fields.Add(new CandidateField { Name = "Bio", Type = "string" });
        employee.Fields.Add(new CandidateField { Name = "Salary", Type = "decimal" });
        model.Entities.Add(employee);
        return model;
    }

    private static PropertyDefinition Prop(CandidateDomainModel model, string name) =>
        DomainModelMapper.ToClassDefinitions(model).Single()
            .Properties.Single(p => p.Name == name);

    [TestMethod]
    public void Id_GetsKeyAttribute()
    {
        CollectionAssert.Contains(Prop(EmployeeModel(), "Id").Attributes, "Key");
    }

    [TestMethod]
    public void NameField_IsRequiredAndLengthBounded()
    {
        var name = Prop(EmployeeModel(), "Name");
        Assert.IsTrue(name.Rules.Any(r => r.Name == "required"), "expected [Required]");
        Assert.IsTrue(name.Rules.Any(r => r.Name == "maxlength" && r.MaxLength == 200),
            "expected [StringLength(200)]");
    }

    [TestMethod]
    public void EmailField_GetsEmailAddressAttribute_AndLength()
    {
        var email = Prop(EmployeeModel(), "WorkEmail");
        CollectionAssert.Contains(email.Attributes, "EmailAddress");
        Assert.IsTrue(email.Rules.Any(r => r.Name == "maxlength" && r.MaxLength == 200));
        Assert.IsFalse(email.Rules.Any(r => r.Name == "required"),
            "a non-name string is not required");
    }

    [TestMethod]
    public void PlainString_IsOnlyLengthBounded()
    {
        var bio = Prop(EmployeeModel(), "Bio");
        Assert.IsTrue(bio.Rules.Any(r => r.Name == "maxlength" && r.MaxLength == 200));
        Assert.IsFalse(bio.Rules.Any(r => r.Name == "required"));
        Assert.IsEmpty(bio.Attributes);
    }

    [TestMethod]
    public void NonString_NonId_IsUntouched()
    {
        var salary = Prop(EmployeeModel(), "Salary");
        Assert.IsEmpty(salary.Rules);
        Assert.IsEmpty(salary.Attributes);
    }

    [TestMethod]
    public void ClassWithValidation_DeclaresDataAnnotationsUsing()
    {
        var employee = DomainModelMapper.ToClassDefinitions(EmployeeModel()).Single();
        CollectionAssert.Contains(employee.Usings, "System.ComponentModel.DataAnnotations");
    }

    [TestMethod]
    public void NavigationProperty_IsNotGivenValidation()
    {
        // Order -> Customer: the Customer nav property must keep only [JsonIgnore].
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Customer" });
        var order = new CandidateEntity { Name = "Order" };
        order.Relationships.Add(new CandidateRelationship { Target = "Customer", Role = "references" });
        model.Entities.Add(order);

        var nav = DomainModelMapper.ToClassDefinitions(model)
            .Single(c => c.Name == "Order").Properties.Single(p => p.Name == "Customer");

        CollectionAssert.AreEqual(new[] { "JsonIgnore" }, nav.Attributes);
        Assert.IsEmpty(nav.Rules);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~DomainModelMapperValidationTests"`
Expected: FAIL — no `Key` attribute / no rules on properties yet.

- [ ] **Step 3: Implement the inference in `DomainModelMapper`**

In `AppScaffolding/DomainModelMapper.cs`, add these constants beside the existing ones (after `JsonIgnoreAttribute`, around line 53):

```csharp
    /// <summary>The namespace DataAnnotations attributes live in. Declared only by a class that uses one.</summary>
    private const string DataAnnotationsNamespace = "System.ComponentModel.DataAnnotations";

    /// <summary>Default upper bound for a generated string column: generous, but not unbounded.</summary>
    private const int DefaultStringLength = 200;
```

In `ToClassDefinitions`, add a call after `AddAuditFields(classes);` (line 92) and before `return classes;`:

```csharp
        AddValidation(classes);
```

Add these two methods (place them after `AddAuditFields`):

```csharp
    /// <summary>
    /// Adds inferred DataAnnotations to each property from its name and type: a key on Id, a
    /// required, length-bounded name field, and length limits (plus an email/phone shape) on
    /// strings. Declarative only -- the interactive front-ends run Validator.TryValidateObject to
    /// enforce them. A class that gains any attribute also gains the DataAnnotations using.
    /// </summary>
    private static void AddValidation(List<ClassDefinition> classes)
    {
        foreach (var definition in classes)
        {
            var added = false;
            foreach (var property in definition.Properties)
                added |= InferValidation(property);

            if (added && !definition.Usings.Contains(DataAnnotationsNamespace))
                definition.Usings.Add(DataAnnotationsNamespace);
        }
    }

    /// <summary>
    /// Applies the inference rules to one property, mutating its Rules/Attributes in place, and
    /// returns whether it added a DataAnnotations attribute. A navigation property (typed as
    /// another class or a List&lt;&gt;) is not a string and carries no Id name, so it is left alone.
    /// </summary>
    private static bool InferValidation(PropertyDefinition property)
    {
        if (string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)
            && property.Type == "int")
        {
            property.Attributes.Add("Key");
            return true;
        }

        if (property.Type != "string")
            return false;

        var isNameField = string.Equals(property.Name, "Name", StringComparison.OrdinalIgnoreCase)
            || string.Equals(property.Name, "Title", StringComparison.OrdinalIgnoreCase);

        if (isNameField)
            property.Rules.Add(new PropertyRule { Name = "required" });

        property.Rules.Add(new PropertyRule { Name = "maxlength", MaxLength = DefaultStringLength });

        if (property.Name.Contains("email", StringComparison.OrdinalIgnoreCase))
            property.Attributes.Add("EmailAddress");
        else if (property.Name.Contains("phone", StringComparison.OrdinalIgnoreCase))
            property.Attributes.Add("Phone");

        return true;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~DomainModelMapperTests|FullyQualifiedName~DomainModelMapperValidationTests|FullyQualifiedName~DomainModelMapperNavigationTests"`
Expected: PASS (new validation tests plus the existing mapper/navigation tests all green).

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/DomainModelMapper.cs CodeGenerator.Tests/App/DomainModelMapperTests.cs
git commit -m "feat: infer DataAnnotations on generated model classes

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Console validation enforcement

**Files:**
- Modify: `AppScaffolding/ConsoleMenuTemplate.cs`
- Test: `CodeGenerator.Tests/App/ConsoleTargetTests.cs`

**Interfaces:**
- Consumes: `ConsoleMenuTemplate.Source(string projectName)` → the generated `EntityMenu<T>` source string.
- Produces: the generated `Add()` runs `Validator.TryValidateObject(...)` and skips `Create` on failure.

- [ ] **Step 1: Write the failing test**

Add to `CodeGenerator.Tests/App/ConsoleTargetTests.cs` inside `ConsoleTargetTests`:

```csharp
    [TestMethod]
    public void Menu_ValidatesBeforeCreate()
    {
        var src = ConsoleMenuTemplate.Source("Shop");

        StringAssert.Contains(src, "using System.ComponentModel.DataAnnotations;");
        StringAssert.Contains(src, "Validator.TryValidateObject");
        StringAssert.Contains(src, "Not added.");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ConsoleTargetTests.Menu_ValidatesBeforeCreate"`
Expected: FAIL — the string is not present.

- [ ] **Step 3: Implement enforcement in the template**

In `AppScaffolding/ConsoleMenuTemplate.cs`, add the using inside the generated source's using block (after `using System.Reflection;`):

```csharp
using System.ComponentModel.DataAnnotations;
```

Replace the generated `Add()` method body (the `private void Add()` block) with:

```csharp
    private void Add()
    {
        var item = new T();
        foreach (var prop in Editable())
        {
            System.Console.Write($"{prop.Name} ({prop.PropertyType.Name}): ");
            var value = Parse(System.Console.ReadLine() ?? string.Empty, prop.PropertyType);
            if (value is not null) prop.SetValue(item, value);
        }

        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(
                item, new ValidationContext(item), errors, validateAllProperties: true))
        {
            foreach (var error in errors)
                System.Console.WriteLine("  " + error.ErrorMessage);
            System.Console.WriteLine("Not added.");
            return;
        }

        _repository.Create(item);
        System.Console.WriteLine("Added.");
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ConsoleTargetTests|FullyQualifiedName~GeneratedProjectBuildTests"`
Expected: PASS — the structure test passes and the generated console project still compiles (`GeneratedProject_Builds("console")`).

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/ConsoleMenuTemplate.cs CodeGenerator.Tests/App/ConsoleTargetTests.cs
git commit -m "feat: enforce validation in the generated console app

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Console foreign-key picker

**Files:**
- Modify: `AppScaffolding/ConsoleMenuTemplate.cs`
- Modify: `AppScaffolding/ConsoleProgramTemplate.cs`
- Test: `CodeGenerator.Tests/App/ConsoleTargetTests.cs`, `CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs`

**Interfaces:**
- Consumes: `ConsoleMenuTemplate.Source(string)`, `ConsoleProgramTemplate.Build(string, IReadOnlyList<string>)`.
- Produces: `EntityMenu<T>` gains a second constructor parameter `Func<Type, System.Collections.IEnumerable?> related`; `Program` builds a `typeof(Entity) → GetAll()` registry, wraps it in a local `Related` function, and passes it to every `EntityMenu`.

- [ ] **Step 1: Write the failing tests**

Add to `ConsoleTargetTests`:

```csharp
    [TestMethod]
    public void Menu_OffersAPickerForForeignKeys()
    {
        var src = ConsoleMenuTemplate.Source("Shop");

        StringAssert.Contains(src, "Func<Type, System.Collections.IEnumerable?>");
        StringAssert.Contains(src, "TryPickForeignKey");
    }

    [TestMethod]
    public void Program_BuildsARelatedRowsRegistryAndPassesIt()
    {
        var src = ConsoleProgramTemplate.Build("Shop", new[] { "Book", "Member" });

        StringAssert.Contains(src, "[typeof(Book)] = () => new BookRepository().GetAll()");
        StringAssert.Contains(src, "[typeof(Member)] = () => new MemberRepository().GetAll()");
        StringAssert.Contains(src, "new EntityMenu<Book>(new BookRepository(), Related)");
        StringAssert.Contains(src, "new EntityMenu<Member>(new MemberRepository(), Related)");
    }
```

Replace the existing `Program_RegistersOneMenuPerEntity` assertions for the constructor calls (the two `new EntityMenu<…>(…)` lines) so they match the new signature:

```csharp
    [TestMethod]
    public void Program_RegistersOneMenuPerEntity()
    {
        var src = ConsoleProgramTemplate.Build("Shop", new[] { "Book", "Member" });

        StringAssert.Contains(src, "using Shop.Data;");
        StringAssert.Contains(src, "using Shop.Models;");
        StringAssert.Contains(src, "new EntityMenu<Book>(new BookRepository(), Related)");
        StringAssert.Contains(src, "new EntityMenu<Member>(new MemberRepository(), Related)");
    }
```

Add a relationship-bearing console build test to `GeneratedProjectBuildTests`:

```csharp
    [TestMethod]
    [TestCategory("Integration")]
    public void GeneratedConsoleProject_WithRelationship_Builds()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Customer",
            Fields = { new CandidateField { Name = "Name", Type = "string" } }
        });
        var order = new CandidateEntity
        {
            Name = "Order",
            Fields = { new CandidateField { Name = "Total", Type = "decimal" } }
        };
        order.Relationships.Add(new CandidateRelationship { Target = "Customer", Role = "references" });
        model.Entities.Add(order);

        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);
        try
        {
            var result = new AppScaffolder().Scaffold(model, "Shop", workspace, target);
            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            var (exit, output) = RunDotNet($"build \"{result.ProjectPath}\" -c Release");
            Assert.AreEqual(0, exit, $"dotnet build failed:\n{output}");
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }
```

Add `using DomainDiscovery.Model;` and `using CodeGenerationEngine.Models;` to the top of `GeneratedProjectBuildTests.cs` only if not already present (it already imports `DomainDiscovery.Model`; `CandidateRelationship` lives there too, so no new using is needed).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ConsoleTargetTests"`
Expected: FAIL — new signature and picker helpers not present; `Program_RegistersOneMenuPerEntity` now asserts the new signature.

- [ ] **Step 3: Update `ConsoleMenuTemplate` — constructor, Add loop, and helpers**

In `AppScaffolding/ConsoleMenuTemplate.cs`, replace the generated field/constructor block:

```csharp
    private readonly Repository<T> _repository;

    public EntityMenu(Repository<T> repository) => _repository = repository;
```

with:

```csharp
    private readonly Repository<T> _repository;
    private readonly Func<Type, System.Collections.IEnumerable?> _related;

    public EntityMenu(Repository<T> repository, Func<Type, System.Collections.IEnumerable?> related)
    {
        _repository = repository;
        _related = related;
    }
```

Change the `foreach` inside `Add()` so a foreign key is picked rather than typed. Replace:

```csharp
        foreach (var prop in Editable())
        {
            System.Console.Write($"{prop.Name} ({prop.PropertyType.Name}): ");
            var value = Parse(System.Console.ReadLine() ?? string.Empty, prop.PropertyType);
            if (value is not null) prop.SetValue(item, value);
        }
```

with:

```csharp
        foreach (var prop in Editable())
        {
            if (TryPickForeignKey(prop, out var foreignKey))
            {
                prop.SetValue(item, foreignKey);
                continue;
            }

            System.Console.Write($"{prop.Name} ({prop.PropertyType.Name}): ");
            var value = Parse(System.Console.ReadLine() ?? string.Empty, prop.PropertyType);
            if (value is not null) prop.SetValue(item, value);
        }
```

Add these helper methods after `Delete()` (before `Editable()`):

```csharp
    // A foreign key is an int <Nav>Id whose sibling <Nav> is one of the app's entity types.
    // When it is, offer a numbered picker over that entity's existing rows instead of a raw id.
    // Returns true when the property IS a foreign key (whether or not a row was picked), so the
    // caller does not also prompt for a bare number.
    private bool TryPickForeignKey(PropertyInfo prop, out int foreignKey)
    {
        foreignKey = 0;

        if (prop.PropertyType != typeof(int)
            || !prop.Name.EndsWith("Id", StringComparison.Ordinal)
            || prop.Name.Length <= 2)
            return false;

        var navName = prop.Name.Substring(0, prop.Name.Length - 2);
        var nav = typeof(T).GetProperty(navName);
        if (nav is null)
            return false;

        var rows = _related(nav.PropertyType);
        if (rows is null)
            return false;   // sibling is not a known entity type -- an ordinary int

        var candidates = rows.Cast<object>().ToList();
        if (candidates.Count == 0)
        {
            System.Console.WriteLine($"{navName}: (no records yet -- leaving unset)");
            return true;
        }

        System.Console.WriteLine($"{navName}:");
        for (var i = 0; i < candidates.Count; i++)
            System.Console.WriteLine($"  {i + 1}. {Label(candidates[i])}");
        System.Console.Write("> ");

        if (int.TryParse(System.Console.ReadLine(), out var choice)
            && choice >= 1 && choice <= candidates.Count)
            foreignKey = IdOf(candidates[choice - 1]);

        return true;
    }

    private static int IdOf(object row)
    {
        var id = row.GetType().GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return id?.GetValue(row) is int value ? value : 0;
    }

    // The same display convention the WPF combo box uses: a Name or Title, else the first
    // string field, else just the Id -- so a picked row reads as more than a bare number.
    private static string Label(object row)
    {
        var type = row.GetType();
        var display = type.GetProperty("Name") ?? type.GetProperty("Title")
            ?? type.GetProperties().FirstOrDefault(p => p.PropertyType == typeof(string));
        var text = display?.GetValue(row)?.ToString();
        return string.IsNullOrWhiteSpace(text) ? $"#{IdOf(row)}" : $"{text} (#{IdOf(row)})";
    }
```

(`System`, `System.Collections`, `System.Linq`, and `System.Reflection` are already imported by the template — `Func`, `Type`, `Cast`, `FirstOrDefault`, `PropertyInfo`, `BindingFlags` all resolve.)

- [ ] **Step 4: Update `ConsoleProgramTemplate` — build and inject the registry**

Replace the whole body of `ConsoleProgramTemplate.Build` with:

```csharp
    public static string Build(string projectName, IReadOnlyList<string> entityNames)
    {
        var mapEntries = string.Join(",\n",
            entityNames.Select(name =>
                $"    [typeof({name})] = () => new {name}Repository().GetAll()"));

        var registrations = string.Join(",\n",
            entityNames.Select(name =>
                $"    (\"{name}\", () => new EntityMenu<{name}>(new {name}Repository(), Related).Run())"));

        return $$"""
using {{projectName}};
using {{projectName}}.Data;
using {{projectName}}.Models;

// typeof(Entity) -> that entity's stored rows, so a menu can offer a picker for a foreign key.
var repositories = new System.Collections.Generic.Dictionary<Type, Func<System.Collections.IEnumerable>>
{
{{mapEntries}}
};

System.Collections.IEnumerable? Related(Type entityType) =>
    repositories.TryGetValue(entityType, out var get) ? get() : null;

var menus = new (string Name, Action Run)[]
{
{{registrations}}
};

while (true)
{
    Console.WriteLine("\nEntities:");
    for (var i = 0; i < menus.Length; i++)
        Console.WriteLine($"  {i + 1}. {menus[i].Name}");
    Console.WriteLine("  0. Exit");
    Console.Write("> ");

    var input = Console.ReadLine();
    if (input is null || input.Trim() == "0") break;
    if (int.TryParse(input, out var n) && n >= 1 && n <= menus.Length)
        menus[n - 1].Run();
    else
        Console.WriteLine("Unrecognized choice.");
}
""";
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ConsoleTargetTests|FullyQualifiedName~GeneratedProjectBuildTests"`
Expected: PASS — structure tests pass, the plain console project builds, and the relationship console project builds (compiling the picker code path).

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/ConsoleMenuTemplate.cs AppScaffolding/ConsoleProgramTemplate.cs CodeGenerator.Tests/App/ConsoleTargetTests.cs CodeGenerator.Tests/App/GeneratedProjectBuildTests.cs
git commit -m "feat: pick related rows for foreign keys in the generated console app

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: WPF validation enforcement on Save

**Files:**
- Modify: `AppScaffolding/ViewModelTemplates.cs`
- Test: `CodeGenerator.Tests/App/ViewModelTemplatesTests.cs`

**Interfaces:**
- Consumes: `ViewModelTemplates.EntityViewModel(string appName)` → the generated `EntityViewModel<T>` source string.
- Produces: the generated `Save()` validates each record and refuses to persist an invalid set, reporting the first error via `Status`.

- [ ] **Step 1: Write the failing test**

Add to `CodeGenerator.Tests/App/ViewModelTemplatesTests.cs` (inside the existing `[TestClass]`):

```csharp
    [TestMethod]
    public void EntityViewModel_ValidatesBeforeSaving()
    {
        var src = ViewModelTemplates.EntityViewModel("Payroll");

        StringAssert.Contains(src, "using System.ComponentModel.DataAnnotations;");
        StringAssert.Contains(src, "Validator.TryValidateObject");
        StringAssert.Contains(src, "Cannot save:");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ViewModelTemplatesTests.EntityViewModel_ValidatesBeforeSaving"`
Expected: FAIL — the string is not present.

- [ ] **Step 3: Implement Save-time enforcement**

In `AppScaffolding/ViewModelTemplates.cs`, in the `EntityViewModel(string appName)` generated source, add two usings to its using block (after `using System.Linq;`):

```csharp
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
```

Replace the generated `Save()` expression-bodied member:

```csharp
    private void Save() =>
        Status = _store.SaveAll(Records.ToList())
            ? $"Saved {Records.Count} {Title} record(s)."
            : $"Could not write {typeof(T).Name}.json.";
```

with:

```csharp
    private void Save()
    {
        foreach (var record in Records)
        {
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(
                    record!, new ValidationContext(record!), errors, validateAllProperties: true))
            {
                Status = $"Cannot save: {errors[0].ErrorMessage}";
                return;
            }
        }

        Status = _store.SaveAll(Records.ToList())
            ? $"Saved {Records.Count} {Title} record(s)."
            : $"Could not write {typeof(T).Name}.json.";
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ViewModelTemplatesTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/ViewModelTemplates.cs CodeGenerator.Tests/App/ViewModelTemplatesTests.cs
git commit -m "feat: enforce validation on Save in the generated WPF app

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: WPF foreign-key combo-box column

**Files:**
- Modify: `AppScaffolding/MainWindowTemplate.cs`
- Test: `CodeGenerator.Tests/App/MainWindowTemplateTests.cs`

**Interfaces:**
- Consumes: `MainWindowTemplate.CodeBehind(string appName)` → the generated `MainWindow.xaml.cs` source.
- Produces: `OnAutoGeneratingColumn` replaces a foreign-key column with a `DataGridComboBoxColumn` bound to the target entity's rows.

- [ ] **Step 1: Write the failing test**

Add to `CodeGenerator.Tests/App/MainWindowTemplateTests.cs`:

```csharp
    [TestMethod]
    public void CodeBehind_BuildsComboColumnsForForeignKeys()
    {
        var source = MainWindowTemplate.CodeBehind("Payroll");

        StringAssert.Contains(source, "TryBuildRelationshipColumn");
        StringAssert.Contains(source, "DataGridComboBoxColumn");
        StringAssert.Contains(source, "SelectedValuePath = \"Id\"");
        StringAssert.Contains(source, "SelectedValueBinding = new System.Windows.Data.Binding(e.PropertyName)");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~MainWindowTemplateTests.CodeBehind_BuildsComboColumnsForForeignKeys"`
Expected: FAIL — the string is not present.

- [ ] **Step 3: Implement the combo column in the code-behind**

In `AppScaffolding/MainWindowTemplate.cs`, in the `CodeBehind(string appName)` generated source, add one using after `using System.Windows.Controls;`:

```csharp
using System.Linq;
```

Replace the generated `OnAutoGeneratingColumn` method and the `IsDisplayable` helper with the following (this keeps the existing Id-lock and nav-cancel behavior, and inserts the foreign-key combo between them):

```csharp
    // Column presentation is a view concern, so the Id lock lives here rather
    // than in a view model. This is the only code-behind in the generated app.
    private void OnAutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (string.Equals(e.PropertyName, "Id", StringComparison.OrdinalIgnoreCase))
            e.Column.IsReadOnly = true;

        // A foreign key (int <Nav>Id whose sibling <Nav> is another entity) is shown as a combo
        // box over that entity's rows, so a user picks a Customer rather than typing its id.
        if (TryBuildRelationshipColumn(sender, e))
            return;

        // A navigation property has no sensible column: a DataGrid would render Customer as its
        // type name and List<Payment> as "System.Collections.Generic.List`1[Payment]". The
        // foreign key next to it is the editable, meaningful one.
        if (!IsDisplayable(e.PropertyType))
            e.Cancel = true;
    }

    private bool TryBuildRelationshipColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyType != typeof(int)
            || !e.PropertyName.EndsWith("Id", StringComparison.Ordinal)
            || e.PropertyName.Length <= 2)
            return false;

        if (sender is not DataGrid grid
            || grid.ItemsSource?.GetType() is not Type source
            || !source.IsGenericType)
            return false;

        var itemType = source.GetGenericArguments()[0];
        var navName = e.PropertyName.Substring(0, e.PropertyName.Length - 2);
        var nav = itemType.GetProperty(navName);
        if (nav is null || !IsEntityType(nav.PropertyType))
            return false;

        if (DataContext is not MainViewModel main)
            return false;

        var tab = main.Entities.FirstOrDefault(vm => vm.Title == navName);
        if (tab is null)
            return false;

        e.Column = new DataGridComboBoxColumn
        {
            Header = navName,
            ItemsSource = tab.Items,
            SelectedValuePath = "Id",
            DisplayMemberPath = DisplayMember(nav.PropertyType),
            SelectedValueBinding = new System.Windows.Data.Binding(e.PropertyName),
        };
        return true;
    }

    // An entity type is one of the app's own model classes: a reference type that is neither a
    // string nor a generic collection. (Navigation collections are cancelled by IsDisplayable.)
    private static bool IsEntityType(Type type) =>
        type.IsClass && type != typeof(string) && !type.IsGenericType;

    // The same display convention the console picker uses: a Name or Title, else the first
    // string field, else the Id.
    private static string DisplayMember(Type type)
    {
        var display = type.GetProperty("Name") ?? type.GetProperty("Title")
            ?? type.GetProperties().FirstOrDefault(p => p.PropertyType == typeof(string));
        return display?.Name ?? "Id";
    }

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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~MainWindowTemplateTests"`
Expected: PASS.

- [ ] **Step 5: Verify a generated WPF app compiles (Windows, manual)**

This is not an automated CI test (WPF does not build on Linux). On the Windows dev machine, scaffold and build a WPF app with a relationship and confirm exit code 0:

```bash
# from the repo root, in a scratch workspace
dotnet run --project CodeGenerator -- "build app: a library that lends books to members"
# approve the model as wpf, then:
dotnet build "<workspace>/<AppName>"
```

Expected: `Build succeeded`. If the combo column fails to compile, apply the spec's documented fallback (console-only relationship UX; revert this task's code-behind change and note WPF combo deferred).

- [ ] **Step 6: Commit**

```bash
git add AppScaffolding/MainWindowTemplate.cs CodeGenerator.Tests/App/MainWindowTemplateTests.cs
git commit -m "feat: pick related rows via a combo column in the generated WPF app

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Final verification

- [ ] **Run the full suite:** `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
  Expected: all green (CI enforces `-warnaserror`, so the build must also be warning-free).
- [ ] Confirm the four automated feature areas are covered: mapper attributes (Task 1), console enforce + picker compile (Tasks 2–3), WPF enforce + combo structure (Tasks 4–5).

## Self-review

**Spec coverage:**
- Part A (inferred attributes) → Task 1. ✓ (`[Key]`, name→`[Required]`+`[StringLength]`, email→`[EmailAddress]`, phone→`[Phone]`, other string→`[StringLength]`, DataAnnotations using.)
- Part B (enforcement) → Task 2 (console `Add`) + Task 4 (WPF `Save`). ✓
- Part C (relationship surfacing) → Task 3 (console picker) + Task 5 (WPF combo). ✓
- Shared display convention → identical `Name`/`Title`/first-string/`Id` rule in Task 3 `Label` and Task 5 `DisplayMember`. ✓
- WebApi/Worker/ClassLibrary get attributes via the shared core (Task 1) and no enforcement wiring — matches the spec. ✓
- Testing approach (mapper unit, console compile, WPF structure + local Windows build) → covered. ✓

**Placeholder scan:** no TBD/TODO; every code step shows complete code. ✓

**Type consistency:** the resolver type `Func<Type, System.Collections.IEnumerable?>` is identical in `ConsoleMenuTemplate` (constructor) and the `Related` local function in `ConsoleProgramTemplate`. `TryPickForeignKey`/`TryBuildRelationshipColumn`/`DisplayMember`/`Label`/`IsEntityType` names are used consistently within their tasks. `PropertyRule.Name` values (`"required"`, `"maxlength"`) match the generator's switch. ✓
