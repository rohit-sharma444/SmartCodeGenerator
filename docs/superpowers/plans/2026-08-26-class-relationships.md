# Class Relationships Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The tool reads a project's classes, works out which relationships are missing, proposes them with the evidence behind each one, and writes the foreign key and navigation properties once the user confirms.

**Architecture:** Detection and emission are pure code in `CodeGenerationEngine` — classes in, proposals out, then proposals to file edits guarded by a Roslyn compile and a rollback. The asking is a port (`IChoiceAsker`) implemented twice, by `Menu.Choose` on the console and by `ChatSession.Ask` in the browser, and never called from inside a command — only after one has finished, because both surfaces buffer a command's output while it runs.

**Tech Stack:** C# on `net10.0`, `ImplicitUsings` and `Nullable` enabled, MSTest 4.0.2, bUnit 2.9.0, Roslyn (`Microsoft.CodeAnalysis.CSharp`), Blazor Server.

**Spec:** `docs/superpowers/specs/2026-08-26-class-relationships-design.md`

## Global Constraints

- Target framework `net10.0`; `ImplicitUsings` enable; `Nullable` enable; `LangVersion` latest (`Directory.Build.props`).
- CI builds `SmartCodeGenerator/SmartCodeGenerator.slnx` in **Release** with `-warnaserror` and then runs every test project. A warning is a build failure there and not locally, so run `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror` before claiming a task is done. Analyzer errors do not reappear on an incremental build.
- New engine code goes in `CodeGenerationEngine`; new console code in `CodeGenerator/App`; new chat code in `CodeGenerator/Web/Chat`. All new tests go in `CodeGenerator.Tests`, which already references every project involved.
- A foreign key is **exactly** `int`, named `<Target>Id`. Not `int?`. `ForeignKeys.TargetsOf` (`AppScaffolding/ForeignKeys.cs:23`) skips anything else, and everything downstream reads that rule.
- Navigation properties carry `[JsonIgnore]` and `[Browsable(false)]`, needing `System.Text.Json.Serialization` and `System.ComponentModel`.
- **Never ask a question from inside a running command.** `CommandColour.Run` (`CodeGenerator/App/CommandColour.cs:72`) redirects `Console.Out` into a buffer for the whole command, and `ChatSession` captures output the same way. A prompt printed inside either is a question nobody can see. Every question in this feature is asked *after* the command returns.
- Class files are found by the rule in `ClassMethodLister.FileOf` (`CodeGenerationEngine/Core/ClassMethodLister.cs:53`): `*.cs` under the workspace root, excluding `\bin\`, `\obj\` and anything under `DeletedClasses`.
- Emitted C# is proved by compiling it (`Compiler.CompileSources`), never by substring assertions.
- Existing piped/scripted tests must stay green **without being edited**.

---

### Task 1: One definition of a relationship

The triple that makes a relationship — the foreign key, the forward reference, the inverse collection — is currently spelled out inside `DomainModelMapper`. Move it into the engine so the new flow and `build app` cannot drift, and prove `build app`'s output did not change.

**Files:**
- Create: `CodeGenerationEngine/Core/Relationships/RelationshipKind.cs`
- Create: `CodeGenerationEngine/Core/Relationships/RelationshipConvention.cs`
- Modify: `AppScaffolding/DomainModelMapper.cs:187-236` (`AddNavigationProperties`, `AddNavigation`, `Pluralize`)
- Test: `CodeGenerator.Tests/Core/Relationships/RelationshipConventionTests.cs`

**Interfaces:**
- Consumes: `ClassDefinition`, `PropertyDefinition` (`CodeGenerationEngine/Models`).
- Produces:
  - `enum RelationshipKind { OneToMany, OneToOne, ManyToMany }`
  - `static string RelationshipConvention.ForeignKeyName(string target)` → `"CustomerId"`
  - `static string RelationshipConvention.Pluralize(string name)`
  - `static PropertyDefinition RelationshipConvention.ForeignKey(string target)`
  - `static PropertyDefinition RelationshipConvention.Forward(string target)`
  - `static PropertyDefinition RelationshipConvention.InverseCollection(string owner)`
  - `static PropertyDefinition RelationshipConvention.InverseReference(string owner)`
  - `static bool RelationshipConvention.CanAdd(ClassDefinition target, string propertyName)`
  - `const string RelationshipConvention.JsonSerializationNamespace`, `.ComponentModelNamespace`

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerationEngine.Core.Relationships;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Relationships;

/// <summary>The one definition of what a relationship is made of. `build app` and the
/// relationship flow both emit through this, so a change here changes both.</summary>
[TestClass]
public class RelationshipConventionTests
{
    [TestMethod]
    public void The_foreign_key_is_an_int_named_after_its_target()
    {
        var key = RelationshipConvention.ForeignKey("Customer");

        Assert.AreEqual("CustomerId", key.Name);
        Assert.AreEqual("int", key.Type);
        Assert.IsEmpty(key.Attributes);
    }

    [TestMethod]
    public void The_forward_reference_is_typed_as_the_target_and_hidden_from_json_and_grids()
    {
        var forward = RelationshipConvention.Forward("Customer");

        Assert.AreEqual("Customer", forward.Name);
        Assert.AreEqual("Customer", forward.Type);
        Assert.Contains("JsonIgnore", forward.Attributes);
        Assert.Contains("Browsable(false)", forward.Attributes);
    }

    [TestMethod]
    public void The_inverse_of_a_one_to_many_is_a_pluralized_list()
    {
        var inverse = RelationshipConvention.InverseCollection("Order");

        Assert.AreEqual("Orders", inverse.Name);
        Assert.AreEqual("List<Order>", inverse.Type);
    }

    [TestMethod]
    public void The_inverse_of_a_one_to_one_is_a_single_reference()
    {
        var inverse = RelationshipConvention.InverseReference("Employee");

        Assert.AreEqual("Employee", inverse.Name);
        Assert.AreEqual("Employee", inverse.Type);
    }

    [TestMethod]
    public void A_y_after_a_consonant_pluralizes_to_ies()
        => Assert.AreEqual("Companies", RelationshipConvention.Pluralize("Company"));

    [TestMethod]
    public void A_name_that_collides_with_the_class_itself_cannot_be_added()
    {
        // CS0542: a member may not have the same name as its enclosing type.
        var customer = new ClassDefinition { Name = "Customer" };

        Assert.IsFalse(RelationshipConvention.CanAdd(customer, "Customer"));
    }

    [TestMethod]
    public void A_name_already_taken_cannot_be_added()
    {
        var customer = new ClassDefinition { Name = "Customer" };
        customer.Properties.Add(new PropertyDefinition { Name = "Orders", Type = "List<Order>" });

        Assert.IsFalse(RelationshipConvention.CanAdd(customer, "Orders"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~RelationshipConventionTests`
Expected: FAIL — `RelationshipConvention` does not exist (CS0246).

- [ ] **Step 3: Write minimal implementation**

`CodeGenerationEngine/Core/Relationships/RelationshipKind.cs`:

```csharp
namespace CodeGenerationEngine.Core.Relationships;

/// <summary>How two classes are related, which decides which side carries the foreign key
/// and what the inverse property looks like.</summary>
public enum RelationshipKind
{
    /// <summary>The owner carries the foreign key; the target gets a collection.</summary>
    OneToMany,

    /// <summary>The owner carries the foreign key; the target gets a single reference.</summary>
    OneToOne,

    /// <summary>Neither side carries a foreign key. A join class does, and both sides get a
    /// collection of it.</summary>
    ManyToMany
}
```

`CodeGenerationEngine/Core/Relationships/RelationshipConvention.cs`:

```csharp
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core.Relationships;

/// <summary>
/// What a relationship between two classes is made of, in one place.
/// </summary>
/// <remarks>
/// This was spelled out inside <c>DomainModelMapper.AddNavigation</c>, which only the
/// <c>build app</c> path could reach. Relationships set by hand have to produce the same triple —
/// <c>&lt;Target&gt;Id</c>, a forward reference, an inverse — or <c>ForeignKeys.TargetsOf</c>
/// cannot see them and neither can the seeder, the generated validators, the console picker or the
/// screen split. Two spellings of that triple is how the two paths come to disagree.
/// </remarks>
public static class RelationshipConvention
{
    public const string JsonSerializationNamespace = "System.Text.Json.Serialization";
    public const string ComponentModelNamespace = "System.ComponentModel";

    private const string JsonIgnoreAttribute = "JsonIgnore";
    private const string NotBrowsableAttribute = "Browsable(false)";

    /// <summary>The foreign key field name for a target class.</summary>
    public static string ForeignKeyName(string target) => target + "Id";

    /// <summary>The foreign key. Plain <c>int</c> and no attributes: it is data the user edits,
    /// and <c>ForeignKeys.TargetsOf</c> rejects any other type.</summary>
    public static PropertyDefinition ForeignKey(string target) =>
        new() { Name = ForeignKeyName(target), Type = "int" };

    /// <summary>The reference from the foreign-key owner to its target.</summary>
    public static PropertyDefinition Forward(string target) => Navigation(target, target);

    /// <summary>The collection on the target of a one-to-many.</summary>
    public static PropertyDefinition InverseCollection(string owner) =>
        Navigation(Pluralize(owner), $"List<{owner}>");

    /// <summary>The single reference on the target of a one-to-one.</summary>
    public static PropertyDefinition InverseReference(string owner) => Navigation(owner, owner);

    /// <summary>
    /// Whether <paramref name="propertyName"/> can be added to <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// Two refusals, both from <c>AddNavigation</c>: a member may not share its enclosing type's
    /// name (CS0542), and a name already present must not be written twice.
    /// </remarks>
    public static bool CanAdd(ClassDefinition target, string propertyName)
    {
        if (string.Equals(propertyName, target.Name, StringComparison.OrdinalIgnoreCase))
            return false;

        return !target.Properties.Any(
            p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// English plural, well enough for a collection property name. Not a linguistics engine: the
    /// worst case is an inverse collection called "Companys", which compiles and reads oddly.
    /// </summary>
    public static string Pluralize(string name)
    {
        if (name.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("x", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("ch", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("sh", StringComparison.OrdinalIgnoreCase))
            return name + "es";

        if (name.Length > 1
            && name.EndsWith("y", StringComparison.OrdinalIgnoreCase)
            && !"aeiou".Contains(char.ToLowerInvariant(name[^2])))
            return name[..^1] + "ies";

        return name + "s";
    }

    private static PropertyDefinition Navigation(string name, string type) =>
        new()
        {
            Name = name,
            Type = type,
            Attributes = { JsonIgnoreAttribute, NotBrowsableAttribute }
        };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~RelationshipConventionTests`
Expected: PASS, 7 tests.

- [ ] **Step 5: Point `DomainModelMapper` at the convention**

In `AppScaffolding/DomainModelMapper.cs`, delete the private `Pluralize` method and the four private constants (`JsonSerializationNamespace`, `JsonIgnoreAttribute`, `ComponentModelNamespace`, `NotBrowsableAttribute`) **only if nothing else in the file uses them** — check with a search first, several are used by `AddAuditFields` and by the screen templates. Leave any that are still referenced.

Rewrite `AddNavigation` to build its property from the convention:

```csharp
    private static void AddNavigation(ClassDefinition definition, PropertyDefinition navigation)
    {
        if (!RelationshipConvention.CanAdd(definition, navigation.Name))
            return;

        definition.Properties.Add(navigation);

        if (!definition.Usings.Contains(RelationshipConvention.JsonSerializationNamespace))
            definition.Usings.Add(RelationshipConvention.JsonSerializationNamespace);

        if (!definition.Usings.Contains(RelationshipConvention.ComponentModelNamespace))
            definition.Usings.Add(RelationshipConvention.ComponentModelNamespace);
    }
```

and the two call sites in `AddNavigationProperties`:

```csharp
                AddNavigation(owner, RelationshipConvention.Forward(targetName));
                AddNavigation(target, RelationshipConvention.InverseCollection(ownerName));
```

Add `using CodeGenerationEngine.Core.Relationships;` at the top of the file.

- [ ] **Step 6: Prove `build app`'s output did not change**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~DomainModelMapperTests`
Expected: PASS, **with no test edited**. If any test needs changing, the refactor changed behaviour — stop and find out why rather than editing the test.

Then the whole suite: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerationEngine/Core/Relationships AppScaffolding/DomainModelMapper.cs CodeGenerator.Tests/Core/Relationships
git commit -m "refactor: one definition of what a relationship is made of"
```

---

### Task 2: One list of a workspace's class files

Three places already implement "the .cs files that count" and the new detector would be a fourth. Extract it once.

**Files:**
- Create: `CodeGenerationEngine/Core/ClassFiles.cs`
- Modify: `CodeGenerationEngine/Core/ClassMethodLister.cs:53-65` (`FileOf`)
- Test: `CodeGenerator.Tests/Core/ClassFilesTests.cs`

**Interfaces:**
- Produces:
  - `static IReadOnlyList<string> ClassFiles.Under(string searchRoot)`
  - `static string? ClassFiles.FileOf(string searchRoot, string className)`

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

/// <summary>Which .cs files count as the workspace's classes. Build output and the recycle
/// bin are skipped, or a stale copy under obj wins the race against the source.</summary>
[TestClass]
public class ClassFilesTests
{
    [TestMethod]
    public void Source_files_are_listed()
    {
        var root = NewRoot();
        Write(root, "Customer.cs", "public class Customer { }");

        var files = ClassFiles.Under(root);

        Assert.HasCount(1, files);
        Assert.AreEqual("Customer.cs", Path.GetFileName(files[0]));
    }

    [TestMethod]
    public void Build_output_and_deleted_classes_are_skipped()
    {
        var root = NewRoot();
        Write(root, "Customer.cs", "public class Customer { }");
        Write(Path.Combine(root, "obj"), "Customer.cs", "public class Customer { }");
        Write(Path.Combine(root, "bin"), "Customer.cs", "public class Customer { }");
        Write(Path.Combine(root, "DeletedClasses"), "Order.cs", "public class Order { }");

        var files = ClassFiles.Under(root);

        Assert.HasCount(1, files);
        Assert.AreEqual("Customer.cs", Path.GetFileName(files[0]));
    }

    [TestMethod]
    public void A_missing_root_lists_nothing()
        => Assert.IsEmpty(ClassFiles.Under(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid())));

    [TestMethod]
    public void FileOf_matches_the_file_named_after_the_class()
    {
        var root = NewRoot();
        Write(root, "Customer.cs", "public class Customer { }");
        Write(root, "Order.cs", "public class Order { }");

        Assert.AreEqual("Order.cs", Path.GetFileName(ClassFiles.FileOf(root, "Order")!));
    }

    [TestMethod]
    public void FileOf_returns_null_when_there_is_no_such_class()
        => Assert.IsNull(ClassFiles.FileOf(NewRoot(), "Nothing"));

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string directory, string name, string code)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), code);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~ClassFilesTests`
Expected: FAIL — `ClassFiles` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace CodeGenerationEngine.Core;

/// <summary>
/// The <c>.cs</c> files under a workspace that count as its classes.
/// </summary>
/// <remarks>
/// One definition, because there were three. Build output is skipped, or a stale copy under
/// <c>obj</c> wins the race against the source and the reader is shown yesterday's members;
/// <c>DeletedClasses</c> is skipped because <c>ProjectManager.ClassNamesIn</c> skips it too, so a
/// name that lives only in there is never asked about.
/// </remarks>
public static class ClassFiles
{
    public static IReadOnlyList<string> Under(string searchRoot)
    {
        if (string.IsNullOrWhiteSpace(searchRoot) || !Directory.Exists(searchRoot))
            return [];

        return
        [
            .. Directory
                .GetFiles(searchRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f =>
                    !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    && !f.Contains("DeletedClasses", StringComparison.OrdinalIgnoreCase))
        ];
    }

    /// <summary>The file a class lives in, or null. Matched by filename, which is the convention
    /// the whole tool works on: a class is a <c>.cs</c> file with its name.</summary>
    public static string? FileOf(string searchRoot, string className) =>
        Under(searchRoot).FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
            .Equals(className, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 4: Point `ClassMethodLister.FileOf` at it**

Replace the body of `ClassMethodLister.FileOf` with `=> ClassFiles.FileOf(searchRoot, className);`, keeping its existing XML doc comment in place with a line saying the rule now lives in `ClassFiles`.

- [ ] **Step 5: Run the tests**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ClassFilesTests|FullyQualifiedName~ClassMethodLister|FullyQualifiedName~ProjectTree"`
Expected: PASS, no existing test edited.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/Core/ClassFiles.cs CodeGenerationEngine/Core/ClassMethodLister.cs CodeGenerator.Tests/Core/ClassFilesTests.cs
git commit -m "refactor: one list of a workspace's class files"
```

---

### Task 3: Reading the workspace into class snapshots

The detector must be pure, so something else has to do the file reading. A snapshot is a class's name, its file, and its properties.

**Files:**
- Create: `CodeGenerationEngine/Core/Relationships/ClassSnapshot.cs`
- Test: `CodeGenerator.Tests/Core/Relationships/ClassSnapshotTests.cs`

**Interfaces:**
- Consumes: `ClassFiles.Under` (Task 2), `EntityExtractor.ExtractProperties`.
- Produces:
  - `sealed record ClassSnapshot(string Name, string File, IReadOnlyList<PropertyDefinition> Properties)`
  - `ClassDefinition ClassSnapshot.ToDefinition()`
  - `static IReadOnlyList<ClassSnapshot> ClassSnapshots.Read(string workspaceRoot)`

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerationEngine.Core.Relationships;

namespace CodeGenerator.Tests.Core.Relationships;

/// <summary>What the detector is given: every class in the workspace with its file and its
/// properties, read once so the detector itself can stay pure.</summary>
[TestClass]
public class ClassSnapshotTests
{
    [TestMethod]
    public void Every_class_is_read_with_its_properties()
    {
        var root = NewRoot();
        Write(root, "Order.cs", """
            namespace Shop;
            public class Order
            {
                public int Id { get; set; }
                public int CustomerId { get; set; }
            }
            """);
        Write(root, "Customer.cs", "namespace Shop; public class Customer { public int Id { get; set; } }");

        var snapshots = ClassSnapshots.Read(root);

        Assert.HasCount(2, snapshots);

        var order = snapshots.Single(s => s.Name == "Order");
        Assert.AreEqual("Order.cs", Path.GetFileName(order.File));
        Assert.Contains("CustomerId", order.Properties.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public void A_snapshot_converts_to_a_class_definition_for_the_convention_to_check()
    {
        var root = NewRoot();
        Write(root, "Customer.cs", "public class Customer { public int Id { get; set; } }");

        var definition = ClassSnapshots.Read(root).Single().ToDefinition();

        Assert.AreEqual("Customer", definition.Name);
        Assert.HasCount(1, definition.Properties);
    }

    [TestMethod]
    public void A_file_with_no_class_in_it_contributes_nothing()
    {
        var root = NewRoot();
        Write(root, "Notes.cs", "// nothing but a comment");

        Assert.IsEmpty(ClassSnapshots.Read(root));
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cs-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string directory, string name, string code)
        => File.WriteAllText(Path.Combine(directory, name), code);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~ClassSnapshotTests`
Expected: FAIL — `ClassSnapshots` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core.Relationships;

/// <summary>One class as the detector sees it: its name, the file it lives in, and its data.</summary>
public sealed record ClassSnapshot(
    string Name,
    string File,
    IReadOnlyList<PropertyDefinition> Properties)
{
    /// <summary>The same class as a <see cref="ClassDefinition"/>, which is what
    /// <see cref="RelationshipConvention.CanAdd"/> takes.</summary>
    public ClassDefinition ToDefinition()
    {
        var definition = new ClassDefinition { Name = Name };
        definition.Properties.AddRange(Properties);
        return definition;
    }
}

/// <summary>
/// Reads a workspace's classes so the detector does not have to touch the disk.
/// </summary>
/// <remarks>
/// The class name comes from the declaration in the file rather than from the file name, because
/// the detector matches property types against class names and a type is what the compiler saw,
/// not what the file was called. Properties come from
/// <see cref="EntityExtractor.ExtractProperties"/>, the same reader the chat page's tree uses, so
/// the two cannot disagree about what a class holds.
/// </remarks>
public static class ClassSnapshots
{
    public static IReadOnlyList<ClassSnapshot> Read(string workspaceRoot)
    {
        var snapshots = new List<ClassSnapshot>();

        foreach (var file in ClassFiles.Under(workspaceRoot))
        {
            var root = CSharpSyntaxTree.ParseText(System.IO.File.ReadAllText(file)).GetRoot();

            var declared = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault();

            if (declared is null)
                continue;

            snapshots.Add(new ClassSnapshot(
                declared.Identifier.Text,
                file,
                EntityExtractor.ExtractProperties(file)));
        }

        return snapshots;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~ClassSnapshotTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Relationships/ClassSnapshot.cs CodeGenerator.Tests/Core/Relationships/ClassSnapshotTests.cs
git commit -m "feat: read a workspace's classes into snapshots"
```

---

### Task 4: The detector

Classes in, proposals out. No console, no writes. This is the whole "the tool works it out itself" claim, so the tests matter more here than anywhere.

**Files:**
- Create: `CodeGenerationEngine/Core/Relationships/RelationshipProposal.cs`
- Create: `CodeGenerationEngine/Core/Relationships/RelationshipDetector.cs`
- Test: `CodeGenerator.Tests/Core/Relationships/RelationshipDetectorTests.cs`

**Interfaces:**
- Consumes: `ClassSnapshot` (Task 3), `RelationshipConvention` (Task 1).
- Produces:
  - `sealed record RelationshipProposal(string Owner, string Target, RelationshipKind Kind, string Reason)`
  - `string RelationshipProposal.Key` → `"Order->Customer"`, for the "already offered" memory
  - `static IReadOnlyList<RelationshipProposal> RelationshipDetector.Detect(IReadOnlyList<ClassSnapshot> classes)`

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerationEngine.Core.Relationships;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Relationships;

/// <summary>
/// What the tool proposes on its own, and — more importantly — what it does not.
/// </summary>
/// <remarks>
/// Every proposal is a schema change the user is asked to approve, so a wrong one costs more than
/// a missing one. The negative cases below are the ones that matter.
/// </remarks>
[TestClass]
public class RelationshipDetectorTests
{
    [TestMethod]
    public void An_orphan_foreign_key_is_proposed_as_a_one_to_many()
    {
        var classes = new[]
        {
            Snapshot("Order", ("Id", "int"), ("CustomerId", "int")),
            Snapshot("Customer", ("Id", "int"))
        };

        var proposals = RelationshipDetector.Detect(classes);

        Assert.HasCount(1, proposals);
        Assert.AreEqual("Order", proposals[0].Owner);
        Assert.AreEqual("Customer", proposals[0].Target);
        Assert.AreEqual(RelationshipKind.OneToMany, proposals[0].Kind);
        Assert.Contains("CustomerId", proposals[0].Reason);
    }

    [TestMethod]
    public void An_orphan_navigation_property_is_proposed_too()
    {
        var classes = new[]
        {
            Snapshot("Order", ("Id", "int"), ("Customer", "Customer")),
            Snapshot("Customer", ("Id", "int"))
        };

        var proposals = RelationshipDetector.Detect(classes);

        Assert.HasCount(1, proposals);
        Assert.AreEqual("Order", proposals[0].Owner);
        Assert.AreEqual("Customer", proposals[0].Target);
    }

    [TestMethod]
    public void A_class_named_after_another_is_proposed_as_its_child()
    {
        var classes = new[]
        {
            Snapshot("OrderLine", ("Id", "int"), ("Quantity", "int")),
            Snapshot("Order", ("Id", "int"))
        };

        var proposals = RelationshipDetector.Detect(classes);

        Assert.HasCount(1, proposals);
        Assert.AreEqual("OrderLine", proposals[0].Owner);
        Assert.AreEqual("Order", proposals[0].Target);
        Assert.Contains("named after", proposals[0].Reason);
    }

    [TestMethod]
    public void A_complete_relationship_is_not_proposed_again()
    {
        var classes = new[]
        {
            Snapshot("Order", ("Id", "int"), ("CustomerId", "int"), ("Customer", "Customer")),
            Snapshot("Customer", ("Id", "int"), ("Orders", "List<Order>"))
        };

        Assert.IsEmpty(RelationshipDetector.Detect(classes));
    }

    [TestMethod]
    public void A_nullable_foreign_key_is_not_a_foreign_key_and_is_not_proposed()
    {
        // ForeignKeys.TargetsOf requires exactly int. Proposing a navigation property for an int?
        // would produce something that looks related and behaves unrelated.
        var classes = new[]
        {
            Snapshot("Order", ("Id", "int"), ("CustomerId", "int?")),
            Snapshot("Customer", ("Id", "int"))
        };

        Assert.IsEmpty(RelationshipDetector.Detect(classes));
    }

    [TestMethod]
    public void An_id_pointing_at_no_such_class_is_not_proposed()
    {
        var classes = new[] { Snapshot("Order", ("Id", "int"), ("SupplierId", "int")) };

        Assert.IsEmpty(RelationshipDetector.Detect(classes));
    }

    [TestMethod]
    public void The_classs_own_Id_is_not_a_relationship_to_itself()
    {
        var classes = new[] { Snapshot("Order", ("Id", "int")), Snapshot("Id", ("Value", "string")) };

        Assert.IsEmpty(RelationshipDetector.Detect(classes));
    }

    [TestMethod]
    public void A_self_reference_is_not_proposed()
    {
        var classes = new[] { Snapshot("Order", ("Id", "int"), ("OrderId", "int")) };

        Assert.IsEmpty(RelationshipDetector.Detect(classes));
    }

    [TestMethod]
    public void Nothing_is_proposed_when_the_inverse_name_is_already_taken()
    {
        // Customer already has an `Orders` of its own. Adding a second is CS0102, and the
        // convention refuses it, so there is nothing to propose.
        var classes = new[]
        {
            Snapshot("Order", ("Id", "int"), ("CustomerId", "int")),
            Snapshot("Customer", ("Id", "int"), ("Orders", "string"))
        };

        Assert.IsEmpty(RelationshipDetector.Detect(classes));
    }

    [TestMethod]
    public void A_proposal_keys_on_its_two_class_names_so_it_can_be_remembered()
        => Assert.AreEqual(
            "Order->Customer",
            new RelationshipProposal("Order", "Customer", RelationshipKind.OneToMany, "because").Key);

    private static ClassSnapshot Snapshot(string name, params (string Name, string Type)[] properties)
        => new(
            name,
            $"C:\\ws\\{name}.cs",
            [.. properties.Select(p => new PropertyDefinition { Name = p.Name, Type = p.Type })]);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~RelationshipDetectorTests`
Expected: FAIL — `RelationshipDetector` does not exist.

- [ ] **Step 3: Write minimal implementation**

`RelationshipProposal.cs`:

```csharp
namespace CodeGenerationEngine.Core.Relationships;

/// <summary>
/// One relationship the tool believes is missing, and why it believes it.
/// </summary>
/// <remarks>
/// <paramref name="Reason"/> is not decoration. The user is being asked to approve a change to
/// their schema, and "because Order.CustomerId is an int and Customer exists" is the difference
/// between a decision and a guess.
/// </remarks>
public sealed record RelationshipProposal(
    string Owner,
    string Target,
    RelationshipKind Kind,
    string Reason)
{
    /// <summary>Identifies the pair, so a proposal the user has already refused is not put to
    /// them again. Keyed on the two names and not on the kind: refusing a one-to-many between
    /// these two is a refusal of the pair.</summary>
    public string Key => $"{Owner}->{Target}";
}
```

`RelationshipDetector.cs`:

```csharp
namespace CodeGenerationEngine.Core.Relationships;

/// <summary>
/// Works out which relationships a set of classes is missing.
/// </summary>
/// <remarks>
/// Pure: classes in, proposals out, nothing read from disk and nothing written. Three signals,
/// and each one is checked against <see cref="RelationshipConvention.CanAdd"/> before it becomes a
/// proposal — a proposal that cannot be written is worse than no proposal, because the user
/// accepts it and nothing happens.
/// </remarks>
public static class RelationshipDetector
{
    public static IReadOnlyList<RelationshipProposal> Detect(
        IReadOnlyList<ClassSnapshot> classes)
    {
        var byName = new Dictionary<string, ClassSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in classes)
            byName[snapshot.Name] = snapshot;

        var proposals = new List<RelationshipProposal>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var owner in classes)
        {
            foreach (var target in OrphanForeignKeys(owner, byName))
                Add(owner, target,
                    $"because {owner.Name}.{RelationshipConvention.ForeignKeyName(target.Name)} "
                    + $"is an int and {target.Name} exists");

            foreach (var target in OrphanNavigations(owner, byName))
                Add(owner, target,
                    $"because {owner.Name}.{target.Name} is typed as {target.Name} "
                    + "and has no foreign key beside it");

            if (NamedAfterParent(owner, byName) is { } parent)
                Add(owner, parent,
                    $"because {owner.Name} is named after {parent.Name}, which exists");
        }

        return proposals;

        void Add(ClassSnapshot owner, ClassSnapshot target, string reason)
        {
            var proposal = new RelationshipProposal(
                owner.Name, target.Name, RelationshipKind.OneToMany, reason);

            if (!Writable(owner, target) || !seen.Add(proposal.Key))
                return;

            proposals.Add(proposal);
        }
    }

    /// <summary>An <c>int &lt;Target&gt;Id</c> whose target exists and which has no navigation
    /// property beside it. Exactly <c>int</c>: <c>ForeignKeys.TargetsOf</c> rejects anything
    /// else, so a nullable one is not a foreign key and must not be finished as though it were.
    /// </summary>
    private static IEnumerable<ClassSnapshot> OrphanForeignKeys(
        ClassSnapshot owner, Dictionary<string, ClassSnapshot> byName)
    {
        foreach (var property in owner.Properties)
        {
            if (property.Type != "int" || property.Name.Length <= 2)
                continue;

            if (!property.Name.EndsWith("Id", StringComparison.Ordinal)
                || string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                continue;

            var targetName = property.Name[..^2];

            if (string.Equals(targetName, owner.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!byName.TryGetValue(targetName, out var target))
                continue;

            if (HasNavigationTo(owner, target.Name))
                continue;

            yield return target;
        }
    }

    /// <summary>A property typed as another class, with no <c>&lt;Target&gt;Id</c> beside it.</summary>
    private static IEnumerable<ClassSnapshot> OrphanNavigations(
        ClassSnapshot owner, Dictionary<string, ClassSnapshot> byName)
    {
        foreach (var property in owner.Properties)
        {
            if (!byName.TryGetValue(property.Type, out var target))
                continue;

            if (string.Equals(target.Name, owner.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (HasForeignKeyTo(owner, target.Name))
                continue;

            yield return target;
        }
    }

    /// <summary>A class whose name begins with another class's name, and which is not already
    /// related to it. The one rule that invents a foreign key rather than completing one.</summary>
    private static ClassSnapshot? NamedAfterParent(
        ClassSnapshot owner, Dictionary<string, ClassSnapshot> byName)
    {
        ClassSnapshot? best = null;

        foreach (var candidate in byName.Values)
        {
            if (string.Equals(candidate.Name, owner.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!owner.Name.StartsWith(candidate.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (HasForeignKeyTo(owner, candidate.Name) || HasNavigationTo(owner, candidate.Name))
                continue;

            // The longest matching prefix: `SalesOrderLine` belongs to `SalesOrder`, not `Sales`.
            if (best is null || candidate.Name.Length > best.Name.Length)
                best = candidate;
        }

        return best;
    }

    private static bool HasNavigationTo(ClassSnapshot owner, string targetName) =>
        owner.Properties.Any(p => string.Equals(p.Type, targetName, StringComparison.Ordinal));

    private static bool HasForeignKeyTo(ClassSnapshot owner, string targetName) =>
        owner.Properties.Any(p =>
            p.Type == "int"
            && string.Equals(
                p.Name, RelationshipConvention.ForeignKeyName(targetName),
                StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether both sides can actually take the properties the convention would add.</summary>
    private static bool Writable(ClassSnapshot owner, ClassSnapshot target)
    {
        var ownerDefinition = owner.ToDefinition();
        var targetDefinition = target.ToDefinition();

        var forward = RelationshipConvention.Forward(target.Name);
        var inverse = RelationshipConvention.InverseCollection(owner.Name);

        var forwardOk = RelationshipConvention.CanAdd(ownerDefinition, forward.Name)
                        || HasNavigationTo(owner, target.Name);

        return forwardOk && RelationshipConvention.CanAdd(targetDefinition, inverse.Name);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~RelationshipDetectorTests`
Expected: PASS, 10 tests.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Relationships CodeGenerator.Tests/Core/Relationships
git commit -m "feat: detect the relationships a project is missing"
```

---

### Task 5: Writing an attributed property with its usings

`ClassPropertyService.AddProperty` writes a bare auto-property and adds no usings, so it cannot write a navigation property. This is its sibling. `AddProperty` itself is not touched.

**Files:**
- Create: `CodeGenerationEngine/Core/Relationships/AttributedPropertyWriter.cs`
- Test: `CodeGenerator.Tests/Core/Relationships/AttributedPropertyWriterTests.cs`

**Interfaces:**
- Consumes: `ClassMemberEditor.TryAddMember`, `PropertyDefinition`.
- Produces: `static bool AttributedPropertyWriter.TryAdd(string filePath, string className, PropertyDefinition property, IEnumerable<string> usings, out string error)`

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerationEngine.Core.Relationships;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Relationships;

/// <summary>Writing a navigation property into a file that already exists: the attributes and
/// the usings they need, in both namespace styles.</summary>
[TestClass]
public class AttributedPropertyWriterTests
{
    [TestMethod]
    public void The_property_and_its_attributes_are_written()
    {
        var file = WriteFile("""
            namespace Shop;

            public class Order
            {
                public int Id { get; set; }
            }
            """);

        var ok = AttributedPropertyWriter.TryAdd(
            file, "Order", Navigation("Customer", "Customer"),
            [RelationshipConvention.JsonSerializationNamespace], out var error);

        Assert.IsTrue(ok, error);

        var code = File.ReadAllText(file);
        Assert.Contains("JsonIgnore", code);
        Assert.Contains("Browsable(false)", code);
        Assert.Contains("Customer Customer", code);
    }

    [TestMethod]
    public void The_usings_are_added_once_and_only_when_missing()
    {
        var file = WriteFile("""
            using System.Text.Json.Serialization;

            namespace Shop;

            public class Order
            {
                public int Id { get; set; }
            }
            """);

        AttributedPropertyWriter.TryAdd(
            file, "Order", Navigation("Customer", "Customer"),
            [RelationshipConvention.JsonSerializationNamespace,
             RelationshipConvention.ComponentModelNamespace],
            out var error);

        var code = File.ReadAllText(file);

        Assert.HasCount(1, Occurrences(code, "using System.Text.Json.Serialization;"), error);
        Assert.HasCount(1, Occurrences(code, "using System.ComponentModel;"));
    }

    [TestMethod]
    public void A_block_scoped_namespace_works_too()
    {
        var file = WriteFile("""
            namespace Shop
            {
                public class Order
                {
                    public int Id { get; set; }
                }
            }
            """);

        var ok = AttributedPropertyWriter.TryAdd(
            file, "Order", Navigation("Customer", "Customer"),
            [RelationshipConvention.ComponentModelNamespace], out var error);

        Assert.IsTrue(ok, error);
        Assert.Contains("using System.ComponentModel;", File.ReadAllText(file));
    }

    [TestMethod]
    public void A_class_that_is_not_there_is_reported_rather_than_written()
    {
        var file = WriteFile("public class Order { }");

        var ok = AttributedPropertyWriter.TryAdd(
            file, "Missing", Navigation("Customer", "Customer"), [], out var error);

        Assert.IsFalse(ok);
        Assert.Contains("Missing", error);
    }

    private static PropertyDefinition Navigation(string name, string type) =>
        new() { Name = name, Type = type, Attributes = { "JsonIgnore", "Browsable(false)" } };

    private static List<int> Occurrences(string text, string needle)
    {
        var found = new List<int>();
        var at = text.IndexOf(needle, StringComparison.Ordinal);

        while (at >= 0)
        {
            found.Add(at);
            at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return found;
    }

    private static string WriteFile(string code)
    {
        var directory = Path.Combine(Path.GetTempPath(), "apw-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);

        var file = Path.Combine(directory, "Order.cs");
        File.WriteAllText(file, code);
        return file;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~AttributedPropertyWriterTests`
Expected: FAIL — `AttributedPropertyWriter` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core.Relationships;

/// <summary>
/// Adds a property with attributes, and the usings those attributes need, to a file that already
/// exists.
/// </summary>
/// <remarks>
/// <see cref="ClassPropertyService.AddProperty"/> writes
/// <c>public T Name { get; set; }</c> and nothing else — no attributes, and no usings — so a
/// navigation property written through it would not compile once <c>[JsonIgnore]</c> was on it.
/// <c>AddProperty</c> is deliberately left alone: it is on the <c>add property</c> command's path
/// and nothing about that command is changing.
/// </remarks>
public static class AttributedPropertyWriter
{
    public static bool TryAdd(
        string filePath,
        string className,
        PropertyDefinition property,
        IEnumerable<string> usings,
        out string error)
    {
        var attributes = property.Attributes.Count == 0
            ? string.Empty
            : string.Concat(property.Attributes.Select(a => $"[{a}]{Environment.NewLine}"));

        var member = $"{attributes}public {property.Type} {property.Name} {{ get; set; }}";

        if (!ClassMemberEditor.TryAddMember(filePath, className, member, out error))
            return false;

        EnsureUsings(filePath, usings);
        return true;
    }

    /// <summary>
    /// Adds any missing using directives to the top of the file.
    /// </summary>
    /// <remarks>
    /// Written on the compilation unit rather than inside the namespace, so it works the same for a
    /// file-scoped namespace and a block-scoped one. A directive already present — at either
    /// level — is left alone rather than duplicated, which would be CS0105 under
    /// <c>-warnaserror</c>.
    /// </remarks>
    private static void EnsureUsings(string filePath, IEnumerable<string> usings)
    {
        var wanted = usings
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (wanted.Length == 0)
            return;

        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath));

        if (tree.GetRoot() is not CompilationUnitSyntax unit)
            return;

        var present = unit.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(u => u.Name?.ToString())
            .Where(n => n is not null)
            .ToHashSet(StringComparer.Ordinal);

        var missing = wanted
            .Where(u => !present.Contains(u))
            .Select(u => SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(u)))
            .ToArray();

        if (missing.Length == 0)
            return;

        var updated = unit.AddUsings(missing);

        File.WriteAllText(filePath, updated.NormalizeWhitespace().ToFullString());
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~AttributedPropertyWriterTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Relationships/AttributedPropertyWriter.cs CodeGenerator.Tests/Core/Relationships/AttributedPropertyWriterTests.cs
git commit -m "feat: write an attributed property and the usings it needs"
```

---

### Task 6: Applying a relationship, with compile and rollback

The hazard task. A property typed as another class cannot compile in isolation, which is exactly what the existing per-class check does. Verify with `CompileSources` over the whole class set, and put every touched file back if it fails.

**Files:**
- Create: `CodeGenerationEngine/Core/Relationships/RelationshipWriter.cs`
- Test: `CodeGenerator.Tests/Core/Relationships/RelationshipWriterTests.cs`

**Interfaces:**
- Consumes: `RelationshipProposal` (Task 4), `ClassSnapshots.Read` (Task 3), `AttributedPropertyWriter.TryAdd` (Task 5), `RelationshipConvention` (Task 1), `Compiler.CompileSources`.
- Produces:
  - `sealed record RelationshipWritten(bool Ok, string Message, IReadOnlyList<string> Added)`
  - `static RelationshipWritten RelationshipWriter.Apply(string workspaceRoot, RelationshipProposal proposal)`

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerationEngine.Core.Relationships;

namespace CodeGenerator.Tests.Core.Relationships;

/// <summary>
/// Writing a relationship into real files, and putting them back when it does not compile.
/// </summary>
/// <remarks>
/// A half-written relationship — a foreign key with no navigation property, or the reverse — is
/// invisible to `ForeignKeys.TargetsOf` and therefore to the seeder, the validators, the console
/// picker and the screen split. It is worse than no relationship, so rollback is part of the
/// feature rather than error handling bolted on.
/// </remarks>
[TestClass]
public class RelationshipWriterTests
{
    [TestMethod]
    public void Both_sides_are_written_and_the_result_compiles()
    {
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n    public int CustomerId { get; set; }\n}\n");
        Write(root, "Customer.cs", "namespace Shop;\n\npublic class Customer\n{\n    public int Id { get; set; }\n}\n");

        var result = RelationshipWriter.Apply(
            root,
            new RelationshipProposal("Order", "Customer", RelationshipKind.OneToMany, "because"));

        Assert.IsTrue(result.Ok, result.Message);

        var order = File.ReadAllText(Path.Combine(root, "Order.cs"));
        var customer = File.ReadAllText(Path.Combine(root, "Customer.cs"));

        Assert.Contains("Customer Customer", order);
        Assert.Contains("JsonIgnore", order);
        Assert.Contains("List<Order> Orders", customer);
        Assert.Contains("using System.ComponentModel;", customer);
    }

    [TestMethod]
    public void The_missing_foreign_key_is_written_when_only_the_navigation_property_exists()
    {
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n    public Customer Customer { get; set; } = new();\n}\n");
        Write(root, "Customer.cs", "namespace Shop;\n\npublic class Customer\n{\n    public int Id { get; set; }\n}\n");

        var result = RelationshipWriter.Apply(
            root,
            new RelationshipProposal("Order", "Customer", RelationshipKind.OneToMany, "because"));

        Assert.IsTrue(result.Ok, result.Message);
        Assert.Contains("int CustomerId", File.ReadAllText(Path.Combine(root, "Order.cs")));
    }

    [TestMethod]
    public void A_one_to_one_gets_a_single_reference_back_not_a_collection()
    {
        var root = NewRoot();
        Write(root, "Employee.cs", "namespace Shop;\n\npublic class Employee\n{\n    public int Id { get; set; }\n}\n");
        Write(root, "Locker.cs", "namespace Shop;\n\npublic class Locker\n{\n    public int Id { get; set; }\n}\n");

        var result = RelationshipWriter.Apply(
            root,
            new RelationshipProposal("Employee", "Locker", RelationshipKind.OneToOne, "because"));

        Assert.IsTrue(result.Ok, result.Message);

        var locker = File.ReadAllText(Path.Combine(root, "Locker.cs"));
        Assert.Contains("Employee Employee", locker);
        Assert.DoesNotContain("List<", locker);
    }

    [TestMethod]
    public void A_relationship_that_does_not_compile_leaves_every_file_byte_for_byte()
    {
        var root = NewRoot();

        // Customer is a static class, so a `List<Order> Orders` on it is CS0708 — a field
        // in a static class. The write succeeds, the compile does not, and both files must
        // come back.
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n    public int CustomerId { get; set; }\n}\n");
        Write(root, "Customer.cs", "namespace Shop;\n\npublic static class Customer\n{\n}\n");

        var before = Snapshot(root);

        var result = RelationshipWriter.Apply(
            root,
            new RelationshipProposal("Order", "Customer", RelationshipKind.OneToMany, "because"));

        Assert.IsFalse(result.Ok, "a relationship that does not compile must not be reported as written");
        CollectionAssert.AreEquivalent(before, Snapshot(root));
    }

    [TestMethod]
    public void A_target_class_that_is_not_in_the_workspace_is_refused_before_anything_is_written()
    {
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n}\n");

        var before = Snapshot(root);

        var result = RelationshipWriter.Apply(
            root,
            new RelationshipProposal("Order", "Nowhere", RelationshipKind.OneToMany, "because"));

        Assert.IsFalse(result.Ok);
        CollectionAssert.AreEquivalent(before, Snapshot(root));
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText);

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "rw-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string directory, string name, string code)
        => File.WriteAllText(Path.Combine(directory, name), code);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~RelationshipWriterTests`
Expected: FAIL — `RelationshipWriter` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core.Relationships;

/// <summary>What happened when a relationship was applied.</summary>
public sealed record RelationshipWritten(
    bool Ok,
    string Message,
    IReadOnlyList<string> Added);

/// <summary>
/// Writes one relationship into the workspace, or leaves the workspace exactly as it was.
/// </summary>
/// <remarks>
/// The verification is <see cref="Compiler.CompileSources"/> over every class in the workspace,
/// NOT <see cref="Compiler.Compile"/> over the file just edited. A navigation property is typed as
/// another class, so it cannot resolve in a single-file compilation — the per-class check on the
/// <c>create class</c> path would report CS0246 for a relationship that is perfectly correct.
/// <para>
/// A synthetic <c>global using</c> tree is compiled alongside the sources because the real projects
/// have <c>ImplicitUsings</c> enabled and a bare Roslyn compilation does not. Without it a
/// <c>List&lt;T&gt;</c> inverse collection fails to compile here while building fine on disk, and
/// the honest fix is to make the check match the compiler rather than to add a using the
/// <c>build app</c> path does not emit.
/// </para>
/// </remarks>
public static class RelationshipWriter
{
    /// <summary>Mirrors the implicit usings a `net10.0` class library gets, so the verification
    /// compile sees what `dotnet build` sees.</summary>
    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    public static RelationshipWritten Apply(string workspaceRoot, RelationshipProposal proposal)
    {
        var classes = ClassSnapshots.Read(workspaceRoot);

        var owner = Find(classes, proposal.Owner);
        var target = Find(classes, proposal.Target);

        if (owner is null)
            return new RelationshipWritten(false, $"Class '{proposal.Owner}' not found.", []);

        if (target is null)
            return new RelationshipWritten(false, $"Class '{proposal.Target}' not found.", []);

        var originals = classes.ToDictionary(c => c.File, c => File.ReadAllText(c.File));

        var added = new List<string>();

        try
        {
            foreach (var (file, className, property) in Parts(owner, target, proposal.Kind))
            {
                if (!AttributedPropertyWriter.TryAdd(
                        file, className, property, UsingsFor(property), out var error))
                    return Rollback(originals, error);

                added.Add($"{className}.{property.Name} ({property.Type})");
            }

            var verdict = Verify(workspaceRoot);

            if (!verdict.Ok)
                return Rollback(originals, verdict.Problem);

            return new RelationshipWritten(
                true,
                $"{proposal.Owner} and {proposal.Target} are now related.",
                added);
        }
        catch (Exception ex)
        {
            return Rollback(originals, ex.Message);
        }
    }

    /// <summary>The properties this relationship needs, skipping any the class already has.</summary>
    private static IEnumerable<(string File, string ClassName, PropertyDefinition Property)> Parts(
        ClassSnapshot owner, ClassSnapshot target, RelationshipKind kind)
    {
        var foreignKey = RelationshipConvention.ForeignKey(target.Name);
        var forward = RelationshipConvention.Forward(target.Name);

        var inverse = kind == RelationshipKind.OneToOne
            ? RelationshipConvention.InverseReference(owner.Name)
            : RelationshipConvention.InverseCollection(owner.Name);

        if (!Has(owner, foreignKey.Name))
            yield return (owner.File, owner.Name, foreignKey);

        if (!Has(owner, forward.Name))
            yield return (owner.File, owner.Name, forward);

        if (!Has(target, inverse.Name))
            yield return (target.File, target.Name, inverse);
    }

    private static IEnumerable<string> UsingsFor(PropertyDefinition property) =>
        property.Attributes.Count == 0
            ? []
            : [RelationshipConvention.JsonSerializationNamespace,
               RelationshipConvention.ComponentModelNamespace];

    private static (bool Ok, string Problem) Verify(string workspaceRoot)
    {
        var sources = new List<(string Path, string Code)>
        {
            ("__ImplicitUsings.cs", ImplicitUsings)
        };

        foreach (var file in ClassFiles.Under(workspaceRoot))
            sources.Add((file, File.ReadAllText(file)));

        var result = Compiler.CompileSources(sources);

        if (result.Success)
            return (true, string.Empty);

        var errors = result.Diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .Take(3);

        return (false, "That relationship does not compile: " + string.Join("; ", errors));
    }

    private static RelationshipWritten Rollback(
        Dictionary<string, string> originals, string why)
    {
        foreach (var (file, code) in originals)
            File.WriteAllText(file, code);

        return new RelationshipWritten(false, why + " Nothing was changed.", []);
    }

    private static bool Has(ClassSnapshot snapshot, string propertyName) =>
        snapshot.Properties.Any(
            p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));

    private static ClassSnapshot? Find(IReadOnlyList<ClassSnapshot> classes, string name) =>
        classes.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~RelationshipWriterTests`
Expected: PASS, 5 tests. If the rollback test fails because the bad relationship *did* compile, replace the `Customer.cs` fixture with one that cannot take the property — a `sealed record` with a positional parameter of the same name will do — rather than weakening the assertion.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Relationships/RelationshipWriter.cs CodeGenerator.Tests/Core/Relationships/RelationshipWriterTests.cs
git commit -m "feat: apply a relationship, verified by compiling and rolled back on failure"
```

---

### Task 7: The join class for many-to-many

Neither side of a many-to-many carries a foreign key; a third class does. This is the only shape that creates a file.

**Files:**
- Create: `CodeGenerationEngine/Core/Relationships/JoinClassWriter.cs`
- Test: `CodeGenerator.Tests/Core/Relationships/JoinClassWriterTests.cs`
- Modify: `CodeGenerationEngine/Core/Relationships/RelationshipWriter.cs` (route `ManyToMany` to the join writer)

**Interfaces:**
- Consumes: `RoslynClassGenerator`, `ClassSnapshots.Read`, `RelationshipConvention`, `RelationshipWriter`'s verification.
- Produces:
  - `static string JoinClassWriter.DefaultName(string a, string b)` → `"OrderProduct"`
  - `static RelationshipWritten JoinClassWriter.Apply(string workspaceRoot, string a, string b, string joinName)`

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerationEngine.Core.Relationships;

namespace CodeGenerator.Tests.Core.Relationships;

/// <summary>A many-to-many is a third class with two foreign keys. It is the only shape that
/// creates a file, so it is also the only one whose rollback has to delete one.</summary>
[TestClass]
public class JoinClassWriterTests
{
    [TestMethod]
    public void The_default_name_is_the_two_class_names_joined()
        => Assert.AreEqual("OrderProduct", JoinClassWriter.DefaultName("Order", "Product"));

    [TestMethod]
    public void The_join_class_carries_both_foreign_keys_and_both_references()
    {
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n}\n");
        Write(root, "Product.cs", "namespace Shop;\n\npublic class Product\n{\n    public int Id { get; set; }\n}\n");

        var result = JoinClassWriter.Apply(root, "Order", "Product", "OrderProduct");

        Assert.IsTrue(result.Ok, result.Message);

        var join = File.ReadAllText(Path.Combine(root, "OrderProduct.cs"));
        Assert.Contains("int OrderId", join);
        Assert.Contains("int ProductId", join);
        Assert.Contains("Order Order", join);
        Assert.Contains("Product Product", join);
    }

    [TestMethod]
    public void Both_parents_get_a_collection_of_the_join_class()
    {
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n}\n");
        Write(root, "Product.cs", "namespace Shop;\n\npublic class Product\n{\n    public int Id { get; set; }\n}\n");

        JoinClassWriter.Apply(root, "Order", "Product", "OrderProduct");

        Assert.Contains("List<OrderProduct> OrderProducts", File.ReadAllText(Path.Combine(root, "Order.cs")));
        Assert.Contains("List<OrderProduct> OrderProducts", File.ReadAllText(Path.Combine(root, "Product.cs")));
    }

    [TestMethod]
    public void A_name_already_taken_is_refused_before_anything_is_written()
    {
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n}\n");
        Write(root, "Product.cs", "namespace Shop;\n\npublic class Product\n{\n    public int Id { get; set; }\n}\n");
        Write(root, "OrderProduct.cs", "namespace Shop;\n\npublic class OrderProduct\n{\n}\n");

        var result = JoinClassWriter.Apply(root, "Order", "Product", "OrderProduct");

        Assert.IsFalse(result.Ok);
        Assert.Contains("OrderProduct", result.Message);
    }

    [TestMethod]
    public void A_failed_join_deletes_the_file_it_created()
    {
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n}\n");

        // Product does not exist, so the join cannot compile.
        var result = JoinClassWriter.Apply(root, "Order", "Product", "OrderProduct");

        Assert.IsFalse(result.Ok);
        Assert.IsFalse(File.Exists(Path.Combine(root, "OrderProduct.cs")),
            "a join class that did not work out must not be left on disk");
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string directory, string name, string code)
        => File.WriteAllText(Path.Combine(directory, name), code);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~JoinClassWriterTests`
Expected: FAIL — `JoinClassWriter` does not exist.

- [ ] **Step 3: Write minimal implementation**

Extract the verification and rollback from `RelationshipWriter` into `internal` members both writers use — `RelationshipWriter.Verify(workspaceRoot)` and `RelationshipWriter.Restore(originals, created, why)` — where `created` is a list of files to delete on failure. Then:

```csharp
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core.Relationships;

/// <summary>
/// The third class a many-to-many needs, and the collections that point at it.
/// </summary>
/// <remarks>
/// The tool never proposes one of these: a join class it could detect already reads as two
/// ordinary foreign keys, which the detector finishes without needing to know it is a join. This
/// runs only when the user asks for a many-to-many outright.
/// <para>
/// The class it writes has two foreign keys and no date of its own, so the master/transaction
/// classifier reads it as a master and will give it a screen of its own if the project is later
/// built as an app. That is recorded in the design and deliberately not changed here.
/// </para>
/// </remarks>
public static class JoinClassWriter
{
    public static string DefaultName(string a, string b) => a + b;

    public static RelationshipWritten Apply(
        string workspaceRoot, string a, string b, string joinName)
    {
        var classes = ClassSnapshots.Read(workspaceRoot);

        if (classes.Any(c => string.Equals(c.Name, joinName, StringComparison.OrdinalIgnoreCase)))
            return new RelationshipWritten(
                false, $"There is already a class called '{joinName}'.", []);

        var left = classes.FirstOrDefault(c => string.Equals(c.Name, a, StringComparison.OrdinalIgnoreCase));
        var right = classes.FirstOrDefault(c => string.Equals(c.Name, b, StringComparison.OrdinalIgnoreCase));

        if (left is null || right is null)
            return new RelationshipWritten(
                false, $"Both '{a}' and '{b}' have to exist first.", []);

        var originals = classes.ToDictionary(c => c.File, c => File.ReadAllText(c.File));
        var created = new List<string>();
        var added = new List<string>();

        try
        {
            var join = new ClassDefinition { Name = joinName };
            join.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
            join.Properties.Add(RelationshipConvention.ForeignKey(left.Name));
            join.Properties.Add(RelationshipConvention.Forward(left.Name));
            join.Properties.Add(RelationshipConvention.ForeignKey(right.Name));
            join.Properties.Add(RelationshipConvention.Forward(right.Name));
            join.Usings.Add(RelationshipConvention.JsonSerializationNamespace);
            join.Usings.Add(RelationshipConvention.ComponentModelNamespace);

            var joinFile = Path.Combine(Path.GetDirectoryName(left.File)!, joinName + ".cs");

            File.WriteAllText(joinFile, RoslynClassGenerator.Generate(join));
            created.Add(joinFile);
            added.Add($"new class {joinName}");

            var collection = RelationshipConvention.InverseCollection(joinName);

            foreach (var parent in new[] { left, right })
            {
                if (!AttributedPropertyWriter.TryAdd(
                        parent.File, parent.Name, collection,
                        [RelationshipConvention.JsonSerializationNamespace,
                         RelationshipConvention.ComponentModelNamespace],
                        out var error))
                    return RelationshipWriter.Restore(originals, created, error);

                added.Add($"{parent.Name}.{collection.Name} ({collection.Type})");
            }

            var verdict = RelationshipWriter.Verify(workspaceRoot);

            if (!verdict.Ok)
                return RelationshipWriter.Restore(originals, created, verdict.Problem);

            return new RelationshipWritten(
                true, $"{a} and {b} are now related through {joinName}.", added);
        }
        catch (Exception ex)
        {
            return RelationshipWriter.Restore(originals, created, ex.Message);
        }
    }
}
```

Check `RoslynClassGenerator`'s public generate method name and signature before writing this — if it is not `Generate(ClassDefinition)`, use whatever `NaturalLanguageCodeEngine` calls and keep the shape of the code above.

- [ ] **Step 4: Route `ManyToMany` from `RelationshipWriter.Apply`**

At the top of `RelationshipWriter.Apply`:

```csharp
        if (proposal.Kind == RelationshipKind.ManyToMany)
            return JoinClassWriter.Apply(
                workspaceRoot, proposal.Owner, proposal.Target,
                JoinClassWriter.DefaultName(proposal.Owner, proposal.Target));
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~JoinClassWriterTests|FullyQualifiedName~RelationshipWriterTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/Core/Relationships CodeGenerator.Tests/Core/Relationships
git commit -m "feat: a join class for many-to-many"
```

---

### Task 8: The flow and its port

Everything decidable about the conversation, with the asking behind an interface. This is where "already offered" lives.

**Files:**
- Create: `CodeGenerator/App/Relationships/IChoiceAsker.cs`
- Create: `CodeGenerator/App/Relationships/RelationshipFlow.cs`
- Test: `CodeGenerator.Tests/App/Relationships/RelationshipFlowTests.cs`

**Interfaces:**
- Consumes: `RelationshipDetector.Detect`, `ClassSnapshots.Read`, `RelationshipWriter.Apply`.
- Produces:
  - `interface IChoiceAsker { int? Choose(string title, IReadOnlyList<string> options, int defaultIndex); }`
  - `sealed class RelationshipFlow(string workspaceRoot, IChoiceAsker asker, Action<string> report)`
  - `bool RelationshipFlow.Run()` — false when there was nothing to offer
  - `static IReadOnlyList<string> RelationshipFlow.CardinalityLabels(string owner, string target)`

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerator.App.Relationships;

namespace CodeGenerator.Tests.App.Relationships;

/// <summary>
/// The conversation, driven by a scripted asker instead of a person.
/// </summary>
/// <remarks>
/// The offer is remembered per candidate rather than per session: a user who declines once and
/// then creates more classes must still be asked about the new ones, and a candidate they refused
/// must never come back.
/// </remarks>
[TestClass]
public class RelationshipFlowTests
{
    [TestMethod]
    public void Nothing_to_propose_means_nothing_is_asked()
    {
        var root = NewRoot();
        Write(root, "Customer.cs", "namespace Shop;\n\npublic class Customer\n{\n    public int Id { get; set; }\n}\n");

        var asker = new ScriptedAsker();

        var offered = new RelationshipFlow(root, asker, _ => { }).Run();

        Assert.IsFalse(offered);
        Assert.IsEmpty(asker.Asked);
    }

    [TestMethod]
    public void Accepting_the_offer_and_the_proposal_writes_the_relationship()
    {
        var root = TwoClasses();

        // 0 = yes to the offer, 0 = accept the proposal, then 1 = no to "anything else".
        var asker = new ScriptedAsker(0, 0, 1);
        var said = new List<string>();

        var offered = new RelationshipFlow(root, asker, said.Add).Run();

        Assert.IsTrue(offered);
        Assert.Contains("Customer Customer", File.ReadAllText(Path.Combine(root, "Order.cs")));
        Assert.IsTrue(said.Any(s => s.Contains("Orders")), "it must say what it wrote");
    }

    [TestMethod]
    public void Declining_the_offer_writes_nothing()
    {
        var root = TwoClasses();
        var before = File.ReadAllText(Path.Combine(root, "Order.cs"));

        var asker = new ScriptedAsker(1);

        new RelationshipFlow(root, asker, _ => { }).Run();

        Assert.AreEqual(before, File.ReadAllText(Path.Combine(root, "Order.cs")));
    }

    [TestMethod]
    public void A_refused_candidate_is_not_offered_again()
    {
        var root = TwoClasses();
        var flow = new RelationshipFlow(root, new ScriptedAsker(1), _ => { });

        Assert.IsTrue(flow.Run(), "the first run has something to offer");
        Assert.IsFalse(flow.Run(), "the same candidate must not come back");
    }

    [TestMethod]
    public void A_candidate_that_appears_later_re_arms_the_offer()
    {
        var root = TwoClasses();
        var flow = new RelationshipFlow(root, new ScriptedAsker(1), _ => { });

        flow.Run();

        Write(root, "Invoice.cs", "namespace Shop;\n\npublic class Invoice\n{\n    public int Id { get; set; }\n    public int CustomerId { get; set; }\n}\n");

        Assert.IsTrue(flow.Run(), "a class created after the decline must still be asked about");
    }

    [TestMethod]
    public void Cancelling_the_offer_is_a_decline_not_a_yes()
    {
        // Menu.Choose returns null on Esc or when input ends. Anything but an explicit yes
        // must leave the schema alone.
        var root = TwoClasses();
        var before = File.ReadAllText(Path.Combine(root, "Order.cs"));

        new RelationshipFlow(root, new ScriptedAsker(null), _ => { }).Run();

        Assert.AreEqual(before, File.ReadAllText(Path.Combine(root, "Order.cs")));
    }

    [TestMethod]
    public void The_cardinality_labels_name_both_classes_in_both_directions()
    {
        var labels = RelationshipFlow.CardinalityLabels("Order", "Customer");

        Assert.HasCount(4, labels);
        Assert.Contains("Order", labels[0]);
        Assert.Contains("Customer", labels[0]);
    }

    private static string TwoClasses()
    {
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n    public int CustomerId { get; set; }\n}\n");
        Write(root, "Customer.cs", "namespace Shop;\n\npublic class Customer\n{\n    public int Id { get; set; }\n}\n");
        return root;
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "rf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string directory, string name, string code)
        => File.WriteAllText(Path.Combine(directory, name), code);

    /// <summary>Answers in order, then cancels for anything further.</summary>
    private sealed class ScriptedAsker(params int?[] answers) : IChoiceAsker
    {
        private int _at;

        public List<string> Asked { get; } = [];

        public int? Choose(string title, IReadOnlyList<string> options, int defaultIndex)
        {
            Asked.Add(title);
            return _at < answers.Length ? answers[_at++] : null;
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~RelationshipFlowTests`
Expected: FAIL — `RelationshipFlow` does not exist.

- [ ] **Step 3: Write minimal implementation**

`IChoiceAsker.cs`:

```csharp
namespace CodeGenerator.App.Relationships;

/// <summary>
/// Asks a question with a fixed set of answers, wherever the user happens to be.
/// </summary>
/// <remarks>
/// A port rather than a call to <c>Menu.Choose</c>, because the same conversation has to happen on
/// a console and on a Blazor page — and because a piped run supplies none of it. Null means
/// cancelled: Esc, a closed tab, or input that ran out. Every caller must treat null as the
/// safest answer rather than as a default.
/// </remarks>
public interface IChoiceAsker
{
    int? Choose(string title, IReadOnlyList<string> options, int defaultIndex);
}
```

`RelationshipFlow.cs`:

```csharp
using CodeGenerationEngine.Core.Relationships;

namespace CodeGenerator.App.Relationships;

/// <summary>
/// The relationship conversation: offer, propose, write, report.
/// </summary>
/// <remarks>
/// Never run from inside a command. <c>CommandColour.Run</c> redirects <c>Console.Out</c> into a
/// buffer for a command's whole life, and the chat page captures output the same way — a question
/// asked in there is a question nobody can see, which is exactly why <c>build app</c>, <c>batch</c>
/// and <c>demo</c> are kept out of both. This runs after the command has finished and its output
/// has been written.
/// <para>
/// What is remembered is the candidates already put to the user, not a single "asked already"
/// flag. A flag would mean that declining once silences the tool for every class created
/// afterwards, which is the session where it is most useful.
/// </para>
/// </remarks>
public sealed class RelationshipFlow(
    string workspaceRoot,
    IChoiceAsker asker,
    Action<string> report)
{
    private static readonly IReadOnlyList<string> YesNo = ["Yes", "No"];

    private readonly HashSet<string> _refused = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The four cardinality answers, worded from the owner's side so the direction is
    /// in the sentence rather than in a separate question.</summary>
    public static IReadOnlyList<string> CardinalityLabels(string owner, string target) =>
    [
        $"each {owner} has one {target}  (many {RelationshipConvention.Pluralize(owner)} per {target})",
        $"each {owner} has many {RelationshipConvention.Pluralize(target)}",
        $"exactly one each way",
        "skip this one"
    ];

    /// <summary>
    /// Runs the conversation. Returns false when there was nothing outstanding to offer, so the
    /// caller knows nothing was put on screen.
    /// </summary>
    public bool Run()
    {
        var outstanding = Outstanding();

        if (outstanding.Count == 0)
            return false;

        var first = outstanding[0];

        var offer = asker.Choose(
            $"{first.Owner}.{RelationshipConvention.ForeignKeyName(first.Target)} points at "
            + $"{first.Target} but nothing links them. Set relationships now?",
            YesNo,
            1);

        if (offer != 0)
        {
            foreach (var proposal in outstanding)
                _refused.Add(proposal.Key);

            return true;
        }

        foreach (var proposal in outstanding)
            Consider(proposal);

        return true;
    }

    private void Consider(RelationshipProposal proposal)
    {
        var choice = asker.Choose(
            $"{proposal.Owner} -> {proposal.Target}, {proposal.Reason}. How are they related?",
            CardinalityLabels(proposal.Owner, proposal.Target),
            0);

        var kind = choice switch
        {
            0 => RelationshipKind.OneToMany,
            1 => RelationshipKind.ManyToMany,
            2 => RelationshipKind.OneToOne,
            _ => (RelationshipKind?)null
        };

        _refused.Add(proposal.Key);

        if (kind is null)
            return;

        var written = RelationshipWriter.Apply(
            workspaceRoot, proposal with { Kind = kind.Value });

        report(written.Ok
            ? written.Message + " Added: " + string.Join(", ", written.Added)
            : written.Message);
    }

    private List<RelationshipProposal> Outstanding() =>
    [
        .. RelationshipDetector
            .Detect(ClassSnapshots.Read(workspaceRoot))
            .Where(p => !_refused.Contains(p.Key))
    ];
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~RelationshipFlowTests`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/Relationships CodeGenerator.Tests/App/Relationships
git commit -m "feat: the relationship conversation, behind a question port"
```

---

### Task 9: Console wiring, outside the buffered region

**Files:**
- Create: `CodeGenerator/App/Relationships/ConsoleChoiceAsker.cs`
- Modify: `CodeGenerator/App/CodeGeneratorStarter.cs` (after the `CommandColour.Run(...)` call in the REPL loop, around line 293-318)
- Test: `CodeGenerator.Tests/App/Relationships/ConsoleRelationshipGateTests.cs`

**Interfaces:**
- Consumes: `IChoiceAsker` (Task 8), `Menu.Choose`.
- Produces: `sealed class ConsoleChoiceAsker : IChoiceAsker`

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerator.App.Relationships;

namespace CodeGenerator.Tests.App.Relationships;

/// <summary>
/// A piped run must never be offered anything.
/// </summary>
/// <remarks>
/// This is the mechanism by which every existing scripted test stays green without being edited:
/// with no asker there is no conversation, so a script's next line is never swallowed as an answer
/// to a menu it could not see.
/// </remarks>
[TestClass]
public class ConsoleRelationshipGateTests
{
    [TestMethod]
    public void No_asker_means_no_flow_and_no_writes()
    {
        var root = Path.Combine(Path.GetTempPath(), "gate-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        File.WriteAllText(Path.Combine(root, "Order.cs"),
            "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n    public int CustomerId { get; set; }\n}\n");
        File.WriteAllText(Path.Combine(root, "Customer.cs"),
            "namespace Shop;\n\npublic class Customer\n{\n    public int Id { get; set; }\n}\n");

        var before = File.ReadAllText(Path.Combine(root, "Order.cs"));

        // What the REPL does when input is redirected: it builds no flow at all.
        var flow = RelationshipGate.For(root, asker: null, report: _ => { });

        Assert.IsNull(flow, "a redirected run must not get a flow");
        Assert.AreEqual(before, File.ReadAllText(Path.Combine(root, "Order.cs")));
    }

    [TestMethod]
    public void An_asker_means_a_flow()
        => Assert.IsNotNull(RelationshipGate.For(
            Path.GetTempPath(), new NeverAsks(), _ => { }));

    private sealed class NeverAsks : IChoiceAsker
    {
        public int? Choose(string title, IReadOnlyList<string> options, int defaultIndex) => null;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~ConsoleRelationshipGateTests`
Expected: FAIL — `RelationshipGate` does not exist.

- [ ] **Step 3: Write minimal implementation**

Add to `CodeGenerator/App/Relationships/ConsoleChoiceAsker.cs`:

```csharp
namespace CodeGenerator.App.Relationships;

/// <summary>Asks on the console, through the menu every other question in this tool uses.</summary>
/// <remarks>
/// <see cref="Menu.Choose"/> already picks an arrow-key list on a real console and a numbered list
/// when input or output is redirected, so nothing here needs to know which it is talking to.
/// </remarks>
public sealed class ConsoleChoiceAsker : IChoiceAsker
{
    public int? Choose(string title, IReadOnlyList<string> options, int defaultIndex)
        => Menu.Choose(title, options, defaultIndex);
}

/// <summary>Whether there is a conversation to be had at all.</summary>
/// <remarks>
/// One place, so the console and the chat page cannot disagree about it, and so the piped case is
/// testable without a console: no asker, no flow.
/// </remarks>
public static class RelationshipGate
{
    public static RelationshipFlow? For(
        string workspaceRoot, IChoiceAsker? asker, Action<string> report)
        => asker is null ? null : new RelationshipFlow(workspaceRoot, asker, report);
}
```

`Menu` is `internal`, so mark `ConsoleChoiceAsker` `internal` if the compiler complains about exposing it — `RelationshipGate` and `IChoiceAsker` stay public for the tests.

- [ ] **Step 4: Wire it into the REPL, after the buffered region**

In `CodeGeneratorStarter`, add a field beside the other session state:

```csharp
    /// <summary>The relationship conversation for this session, or null when input is redirected.
    /// Built once so what it has already offered is remembered across commands.</summary>
    private readonly RelationshipFlow? _relationships;
```

initialised in the constructor:

```csharp
        _relationships = RelationshipGate.For(
            _workspace.Root,
            Console.IsInputRedirected ? null : new ConsoleChoiceAsker(),
            Output.Info);
```

and called in the REPL loop **immediately after** the `CommandColour.Run(...)` block closes, still inside the same `try`:

```csharp
                // After the buffered block, never inside it: CommandColour redirects Console.Out
                // for the whole command, so a menu drawn in there is drawn into a buffer nobody
                // sees while ReadKey waits on a blank screen. `create class` is skipped so a run
                // of them is not interrupted.
                if (kind != CommandKind.Pipeline || !IsCreateClass(input))
                    _relationships?.Run();
```

with a small predicate beside it:

```csharp
    /// <summary>Whether the input was a class creation, which the relationship flow stays out of
    /// so that creating several classes in a row is not interrupted.</summary>
    private static bool IsCreateClass(string input) =>
        input.Contains("class", StringComparison.OrdinalIgnoreCase)
        && (input.TrimStart().StartsWith("create", StringComparison.OrdinalIgnoreCase)
            || input.TrimStart().StartsWith("add", StringComparison.OrdinalIgnoreCase)
            || input.TrimStart().StartsWith("make", StringComparison.OrdinalIgnoreCase)
            || input.TrimStart().StartsWith("new", StringComparison.OrdinalIgnoreCase));
```

Check `Output.Info` exists with that signature in `CodeGenerationEngine.Helper.Output`; if not, use whichever writer prints an ordinary line and say which in the commit message.

- [ ] **Step 5: Run the tests**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release`
Expected: PASS, and **no existing test edited**. Existing piped end-to-end tests (`PipelineEndToEndTests`, `SaveWorkspaceReproTests`, `SampleProjectsReproTests`) are the ones that prove the gate.

- [ ] **Step 6: Verify it on a real console**

No test can see a real prompt. Build the CLI and drive it by hand in a real terminal:

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
```

Then in a real console (not this session's pipe), with `PROJECTPATH` set to a scratch folder under a short path:

```
create class Customer with properties Name string, City string
create class Order with properties Reference string, CustomerId int
list classes
```

Confirm: nothing is asked after either `create class`; the offer appears after `list classes`; the arrow-key list responds; accepting writes both files; `show class Customer` shows the `Orders` collection. Record what you saw in the commit message.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerator/App/Relationships CodeGenerator/App/CodeGeneratorStarter.cs CodeGenerator.Tests/App/Relationships
git commit -m "feat: offer relationships on the console, after the buffered block"
```

---

### Task 10: One question mechanism in the chat

`AiQuestion` carries yes/no only. Generalise it rather than adding a second, parallel question system.

**Files:**
- Modify: `CodeGenerator/Web/Chat/ChatTurn.cs:43`
- Modify: `CodeGenerator/Web/Chat/ChatSession.cs:104-162, 398`
- Modify: `CodeGenerator/Web/Components/ContextStrip.razor:79-95`
- Modify: `CodeGenerator/wwwroot/chat.css` (buttons for N options)
- Modify: `CodeGenerator.Tests/Web/Chat/AiConfirmationTests.cs`
- Test: `CodeGenerator.Tests/Web/Chat/ChoiceQuestionTests.cs`

**Interfaces:**
- Produces:
  - `sealed record SessionQuestion(string Text, IReadOnlyList<string> Options, int DefaultIndex)`
  - `static SessionQuestion SessionQuestion.YesNo(string text)`
  - `SessionQuestion? ChatSession.Question`
  - `int? ChatSession.Ask(SessionQuestion question)`
  - `void ChatSession.Answer(int? index)`

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeIntelligence;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// A question with more than two answers, asked in the browser.
/// </summary>
/// <remarks>
/// The relationship flow asks which cardinality, which is four answers, not yes and no. The
/// mechanism is the same one the AI confirmation already uses — the answer has to travel back to a
/// worker thread blocked inside the flow — so every test here is about that thread being released.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class ChoiceQuestionTests
{
    [TestMethod]
    public void A_chosen_index_comes_back_to_the_waiting_thread()
    {
        var session = NewSession();
        var question = new SessionQuestion("How are they related?", ["one", "many", "both"], 0);

        var wait = Task.Run(() => session.Ask(question));

        WaitUntil(() => session.Question is not null);

        session.Answer(2);

        Assert.IsTrue(wait.Wait(TimeSpan.FromSeconds(5)), "the answer must release the thread");
        Assert.AreEqual(2, wait.Result);
        Assert.IsNull(session.Question);
    }

    [TestMethod]
    public void Ending_the_session_releases_it_with_a_cancellation()
    {
        var session = NewSession();

        var wait = Task.Run(() => session.Ask(
            new SessionQuestion("How are they related?", ["one", "many"], 0)));

        WaitUntil(() => session.Question is not null);

        session.End();

        Assert.IsTrue(wait.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsNull(wait.Result, "an abandoned question is a cancellation, never an answer");
    }

    [TestMethod]
    public void A_yes_no_question_still_reads_as_two_options()
    {
        var question = SessionQuestion.YesNo("Set relationships now?");

        Assert.HasCount(2, question.Options);
        Assert.AreEqual(1, question.DefaultIndex);
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition() && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        Assert.IsTrue(condition(), "the condition never became true");
    }

    private static ChatSession NewSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "cq-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        return new ChatSession(
            new Workspace(root, projectPathSet: false), AiLayer.Create(), []);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~ChoiceQuestionTests`
Expected: FAIL — `SessionQuestion` does not exist.

- [ ] **Step 3: Replace `AiQuestion` with `SessionQuestion`**

In `ChatTurn.cs`, replace the `AiQuestion` record:

```csharp
/// <summary>A question the session is waiting on an answer to.</summary>
/// <remarks>
/// Options rather than yes/no, because the relationship flow asks which cardinality and that is
/// four answers. The AI confirmation is the two-option case, built by <see cref="YesNo"/>, so
/// there is one question mechanism on this page rather than two that drift.
/// </remarks>
public sealed record SessionQuestion(
    string Text,
    IReadOnlyList<string> Options,
    int DefaultIndex)
{
    /// <summary>A yes/no question. The default points at No: a question that can change code must
    /// not act because someone dismissed it.</summary>
    public static SessionQuestion YesNo(string text) => new(text, ["Yes", "No"], 1);
}
```

- [ ] **Step 4: Generalise `ChatSession`'s question**

Change the field and members around lines 104-162:

```csharp
    private TaskCompletionSource<int?>? _answer;

    /// <summary>The question every tab is showing, or null.</summary>
    public SessionQuestion? Question { get; private set; }
```

`Ask` returns `int?` and its `pending` becomes `TaskCompletionSource<int?>`; the `if (Ended) return false;` becomes `return null;`. `Answer` takes the index:

```csharp
    /// <summary>Answers the question on screen. Does nothing if there is none.</summary>
    /// <remarks>Null is a cancellation — a closed tab, or a dismissed question — and is never
    /// treated as a choice by any caller.</remarks>
    public void Answer(int? index)
    {
        lock (_questionGate)
            _answer?.TrySetResult(index);
    }
```

`End` releases with `TrySetResult(null)` instead of `false`.

At line 398, the AI confirmation becomes:

```csharp
                r => Ask(SessionQuestion.YesNo(AiConfirmPrompt.Title(r))) == 0),
```

which keeps `AiConfirmPrompt.Accepted`'s rule — only index 0 is a yes — without a second spelling of it.

- [ ] **Step 5: Render N options in `ContextStrip.razor`**

Replace the two hard-coded buttons:

```razor
            <span class="question-text">@question.Text</span>
            @for (var i = 0; i < question.Options.Count; i++)
            {
                var index = i;
                <button type="button"
                        class="@(index == question.DefaultIndex ? "question-option is-default" : "question-option")"
                        @onclick="() => Session.Answer(index)">@question.Options[index]</button>
            }
```

In `chat.css`, rename the `.question-yes` / `.question-no` rules to `.question-option`, keep the accent colour on `.is-default`, and let the row wrap (`flex-wrap: wrap`) since four cardinality labels are long.

- [ ] **Step 6: Update `AiConfirmationTests`**

The existing tests construct `AiQuestion` and assert a `bool`. Change the constructions to `SessionQuestion.YesNo(...)` and the assertions to indices — `Assert.AreEqual(0, wait.Result)` for a yes, `Assert.AreEqual(1, ...)` for a no, `Assert.IsNull(wait.Result)` for the abandoned case. Do not weaken any assertion; each one is about the worker thread being released.

- [ ] **Step 7: Run the tests**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ChoiceQuestionTests|FullyQualifiedName~AiConfirmationTests|FullyQualifiedName~ChatChrome"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add CodeGenerator/Web CodeGenerator/wwwroot/chat.css CodeGenerator.Tests/Web
git commit -m "feat: one question mechanism in the chat, with more than two answers"
```

---

### Task 11: Chat wiring and narration

**Files:**
- Create: `CodeGenerator/Web/Chat/ChatChoiceAsker.cs`
- Modify: `CodeGenerator/Web/Chat/ChatSession.cs` (after the turn is added in `Send`)
- Test: `CodeGenerator.Tests/Web/Chat/ChatRelationshipTests.cs`

**Interfaces:**
- Consumes: `IChoiceAsker`, `RelationshipGate.For`, `ChatSession.Ask`.
- Produces: `sealed class ChatChoiceAsker(ChatSession session) : IChoiceAsker`

- [ ] **Step 1: Write the failing test**

```csharp
using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeIntelligence;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>The relationship conversation on the page: asked out-of-band, and narrated as a turn
/// once it is over.</summary>
[TestClass]
[DoNotParallelize]
public class ChatRelationshipTests
{
    [TestMethod]
    public void The_asker_puts_the_question_on_the_page_and_returns_the_answer()
    {
        var session = NewSession();
        var asker = new ChatChoiceAsker(session);

        var wait = Task.Run(() => asker.Choose("How are they related?", ["one", "many"], 0));

        WaitUntil(() => session.Question is not null);

        Assert.AreEqual("How are they related?", session.Question!.Text);
        Assert.HasCount(2, session.Question.Options);

        session.Answer(1);

        Assert.IsTrue(wait.Wait(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, wait.Result);
    }

    [TestMethod]
    public void What_the_flow_writes_becomes_a_turn_in_the_transcript()
    {
        var session = NewSession();
        var before = session.Turns.Count;

        session.Narrate("Order and Customer are now related.");

        Assert.AreEqual(before + 1, session.Turns.Count);
        Assert.Contains(
            "now related",
            string.Join(" ", session.Turns[^1].Blocks.Select(b => b.ToString())));
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition() && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        Assert.IsTrue(condition(), "the condition never became true");
    }

    private static ChatSession NewSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "cr-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        return new ChatSession(
            new Workspace(root, projectPathSet: false), AiLayer.Create(), []);
    }
}
```

Check the public name of the transcript collection on `ChatSession` before writing this — the test uses `Turns`; use whatever the class actually exposes.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~ChatRelationshipTests`
Expected: FAIL — `ChatChoiceAsker` and `Narrate` do not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using CodeGenerator.App.Relationships;

namespace CodeGenerator.Web.Chat;

/// <summary>
/// Asks the relationship questions on the page.
/// </summary>
/// <remarks>
/// Through <see cref="ChatSession.Ask"/> and never through <c>Console.WriteLine</c>: this page
/// captures a command's output and renders it only once the command finishes, which is the stated
/// reason <c>build app</c>, <c>batch</c> and <c>demo</c> are declined here. The question therefore
/// has to carry its own text — the proposal and the evidence included — because anything printed
/// alongside it would arrive after the answer was needed.
/// </remarks>
public sealed class ChatChoiceAsker(ChatSession session) : IChoiceAsker
{
    public int? Choose(string title, IReadOnlyList<string> options, int defaultIndex)
        => session.Ask(new SessionQuestion(title, options, defaultIndex));
}
```

Add to `ChatSession`, beside `Load`, which already does exactly this:

```csharp
    /// <summary>Puts a line in the transcript that no command produced.</summary>
    /// <remarks>
    /// The relationship flow runs after a command's output has been captured and written, so it
    /// cannot print into that turn. It gets its own, the way <see cref="Load"/> does.
    /// </remarks>
    public void Narrate(string text)
    {
        _turns.Add(new ChatTurn(
            string.Empty,
            CommandKind.Pipeline,
            ChatVerdict.Succeeded,
            [new TextBlock(text)],
            DateTimeOffset.Now));

        Raise();
    }
```

and a field with the console's counterpart, built once per session:

```csharp
    private RelationshipFlow? _relationships;
```

initialised lazily on first use (the asker needs `this`), then run in `Send` **after** the turn has been added and `Running` cleared, skipping class creation exactly as the console does.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~ChatRelationshipTests`
Expected: PASS.

- [ ] **Step 5: Verify it in a real browser**

The one thing no test sees. Start the chat, load a scratch workspace, and drive it:

```
create class Customer with properties Name string, City string
create class Order with properties Reference string, CustomerId int
list classes
```

Confirm: the offer appears in the strip after `list classes` and not after either `create class`; the four cardinality buttons are all visible and legible at a narrow width; clicking one advances; the transcript gains a line saying what was written; a second tab shows the same question and either tab can answer it; closing the only tab mid-question does not hang the console the chat was started from.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/Web CodeGenerator.Tests/Web/Chat/ChatRelationshipTests.cs
git commit -m "feat: offer relationships on the chat page"
```

---

### Task 12: Stop the silent drop

`create class Order with properties CustomerId int, Customer Customer` discards `Customer Customer` without a word. The detector now finishes the job afterwards, so the fix is to say so rather than to emit the property — emitting it would fail the isolated per-class compile with CS0246.

**Files:**
- Modify: `NaturalLanguageBridge/CommandEntityExtractor.cs:735-775` (`ExtractProperties`)
- Test: `CodeGenerator.Tests/Bridge/IgnoredPropertyTypeTests.cs`

**Interfaces:**
- Produces: `static IReadOnlyList<string> CommandEntityExtractor.IgnoredPropertyEntries(string sentence)`

- [ ] **Step 1: Write the failing test**

```csharp
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// A property whose type is not one of the primitives the extractor knows.
/// </summary>
/// <remarks>
/// The primary regex matches primitives and returns the moment one of them does, so anything else
/// in the list never reaches the fallback parser. It was dropped in silence. It is still dropped —
/// emitting a property typed as another class fails the isolated per-class compile — but the user
/// is told, and told where it is handled instead.
/// </remarks>
[TestClass]
public class IgnoredPropertyTypeTests
{
    [TestMethod]
    public void A_non_primitive_entry_beside_a_primitive_one_is_reported()
    {
        var ignored = CommandEntityExtractor.IgnoredPropertyEntries(
            "create class Order with properties CustomerId int, Customer Customer");

        Assert.HasCount(1, ignored);
        Assert.Contains("Customer", ignored[0]);
    }

    [TestMethod]
    public void An_all_primitive_list_reports_nothing()
        => Assert.IsEmpty(CommandEntityExtractor.IgnoredPropertyEntries(
            "create class Order with properties Reference string, Amount decimal"));

    [TestMethod]
    public void A_command_with_no_property_list_reports_nothing()
        => Assert.IsEmpty(CommandEntityExtractor.IgnoredPropertyEntries("list classes"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~IgnoredPropertyTypeTests`
Expected: FAIL — `IgnoredPropertyEntries` does not exist.

- [ ] **Step 3: Write minimal implementation**

Add beside `ExtractProperties`:

```csharp
    /// <summary>
    /// The entries in a property list that the primitive-matching path threw away.
    /// </summary>
    /// <remarks>
    /// The regex in <see cref="ExtractProperties"/> matches only the primitive type names and
    /// returns as soon as one matches, so <c>Customer Customer</c> in
    /// <c>CustomerId int, Customer Customer</c> never reaches the fallback parser. Reporting it is
    /// not the same as emitting it: a property typed as another class cannot compile in the
    /// single-file check the create-class path runs.
    /// </remarks>
    public static IReadOnlyList<string> IgnoredPropertyEntries(string sentence)
    {
        var section = ExtractPropertySection(sentence);

        if (string.IsNullOrWhiteSpace(section))
            return [];

        var matched = Regex.Matches(
            section,
            @"([A-Za-z_]\w*)\s+(string|int|long|bool|decimal|double|float|datetime|guid)\b",
            RegexOptions.IgnoreCase);

        if (matched.Count == 0)
            return [];

        var kept = matched.Select(m => m.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. Regex
                .Split(section, @"\s*(?:,|;|\band\b)\s*", RegexOptions.IgnoreCase)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0 && !kept.Any(k => t.StartsWith(k, StringComparison.OrdinalIgnoreCase)))
        ];
    }
```

Then in `CommandHandlingService.HandleCreateClass`, after the class is generated, report them:

```csharp
        foreach (var ignored in CommandEntityExtractor.IgnoredPropertyEntries(ctx.Command.RawInput ?? string.Empty))
        {
            Output.Warning(
                $"Ignored '{ignored}': only primitive property types can be given here. "
                + "Relationships between classes are set separately — the tool will offer.");
        }
```

Check what `ParsedCommand` calls the original text; if there is no such field, pass the sentence down from the caller rather than adding one.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter FullyQualifiedName~IgnoredPropertyTypeTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add NaturalLanguageBridge/CommandEntityExtractor.cs CodeGenerationEngine/Service/CommandHandlingService.cs CodeGenerator.Tests/Bridge/IgnoredPropertyTypeTests.cs
git commit -m "fix: say when a property type was ignored instead of dropping it in silence"
```

---

### Task 13: Documentation

**Files:**
- Modify: `docs/commands.md`
- Modify: `docs/user-manual-source.html`
- Modify: `docs/SmartCodeGenerator-User-Manual.docx` (regenerated)

- [ ] **Step 1: Add the behaviour to `docs/commands.md`**

There is no new command, so it does not belong in the command table. Add a short section after the table describing what the tool does on its own: what a relationship is (`<Target>Id` plus a navigation property), when the offer appears, the three signals it reads, the three cardinalities, that a many-to-many creates a join class the user names, and that a piped run is never asked.

- [ ] **Step 2: Mirror it into the manual source**

Add the same material as a section in `docs/user-manual-source.html`, matching the surrounding heading levels and style.

- [ ] **Step 3: Regenerate the .docx**

Convert the HTML with Word COM — there is no pandoc or python-docx on this machine. Then verify by unzipping the result and checking the new section is really in `word/document.xml`, rather than trusting that the conversion ran.

- [ ] **Step 4: Commit**

```bash
git add docs
git commit -m "docs: how the tool sets relationships between classes"
```

---

### Task 14: Whole-suite verification and the branch

- [ ] **Step 1: Build the way CI does**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror`
Expected: no warnings, no errors. `--no-incremental` matters: analyzer errors do not reappear on an incremental build, so a clean incremental build proves nothing.

- [ ] **Step 2: Run every test project**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release`
Expected: PASS. Compare the total against the count before this branch and confirm the increase is only the new tests, with no existing test edited or removed.

- [ ] **Step 3: Confirm the piped path is untouched**

Run: `git diff main --stat -- "*Tests*"` and confirm the only test files changed are new ones plus `AiConfirmationTests.cs` (Task 10, the question mechanism). Any other edited test file needs explaining.

- [ ] **Step 4: Push and open the pull request**

```bash
git push -u origin class-relationships
gh pr create --base main --title "Relationships between generated classes" --body "..."
```

The body should say what the tool now does on its own, list the three detection signals, name the join-class-as-a-master consequence, and note that `create class` is unchanged.

---

## Self-review

**Spec coverage.** Every section of the spec maps to a task: the convention → 1; detection → 3, 4; the trigger and the remembered candidates → 8, 9, 11; the three cardinalities → 1, 6, 7; the three surfaces → 8 (port), 9 (console), 10-11 (chat); writing into files → 5; compile and rollback → 6, 7; the adjacent defect → 12; testing → each task's own steps plus 14; docs → 13. The spec's "add a relationship I did not propose" branch is **partially** covered: `RelationshipFlow.Consider` handles the proposals and the many-to-many kind is reachable from the cardinality question, but the free-choice "pick any two classes" branch is not built. That is a deliberate cut for a first pass — the many-to-many path exists and is reachable for any pair the detector surfaces — and it should be raised with the user rather than quietly dropped.

**Placeholders.** None. Two tasks (7, 11, 12) carry an explicit instruction to check a symbol's real name before writing — `RoslynClassGenerator.Generate`, `ChatSession.Turns`, `ParsedCommand.RawInput`, `Output.Info` — rather than asserting a signature I have not read. Those are checks, not blanks.

**Type consistency.** `RelationshipKind`, `RelationshipProposal`, `ClassSnapshot`, `RelationshipWritten`, `IChoiceAsker.Choose`, `SessionQuestion` and `RelationshipFlow.Run` are spelled the same in every task that uses them. `Compiler.CompileSources` takes `IEnumerable<(string Path, string Code)>`, which is what Task 6 passes. `Menu.Choose` returns `int?`, which is what `IChoiceAsker` returns.
