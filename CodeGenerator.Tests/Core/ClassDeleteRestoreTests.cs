using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize] // ValidationRegistry is a process-wide static, and one test swaps Console.Out.
public sealed class ClassDeleteRestoreTests
{
    private const string Csproj = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";

    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "cg_delrestore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        ValidationRegistry.Clear();
    }

    [TestCleanup]
    public void Cleanup()
    {
        ValidationRegistry.Clear();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private string Write(string relative, string code)
    {
        var file = Path.GetFullPath(Path.Combine(_workspace, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, code);
        return file;
    }

    private string PathOf(string relative) => Path.GetFullPath(Path.Combine(_workspace, relative));

    private void AssertLiveSourcesCompile()
    {
        var files = ClassFiles.Under(_workspace);
        var result = Compiler.CompileSources([.. files.Select(f => (f, File.ReadAllText(f)))]);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
    }

    private const string CustomerCode = "namespace App { public partial class Customer { public string Name { get; set; } = \"\"; } }";
    private const string CustomerValidation = "namespace App { public partial class Customer { public bool Validate() => Name.Length > 0; } }";

    /// <summary>The file is moved whole, so the enum and the record struct beside the class went
    /// with it, and the message named only the class.</summary>
    [TestMethod]
    public void Delete_refuses_a_class_whose_file_declares_other_types_and_moves_nothing()
    {
        Write("App/App.csproj", Csproj);
        var code = """
            namespace App;
            public class Customer
            {
                public CustomerStatus Status { get; set; }
                public class Address { }
            }
            public enum CustomerStatus { Active }
            public readonly record struct Money(decimal Amount);
            """;
        var file = Write("App/Customer.cs", code);

        var (deleted, message) = ClassDeletionService.DeleteClass(_workspace, "Customer");

        Assert.IsFalse(deleted, message);
        Assert.AreEqual(code, File.ReadAllText(file));
        StringAssert.Contains(message, "enum CustomerStatus");
        StringAssert.Contains(message, "record struct Money");
        Assert.DoesNotContain("Address", message);
        Assert.IsFalse(Directory.Exists(PathOf("App/DeletedClasses")));
    }

    /// <summary>A class with only a nested type is still deleted: the nested type is part of it.</summary>
    [TestMethod]
    public void Delete_takes_a_class_with_a_nested_type()
    {
        Write("App/App.csproj", Csproj);
        Write("App/Customer.cs", "namespace App { public class Customer { public class Address { } } }");

        var (deleted, message) = ClassDeletionService.DeleteClass(_workspace, "Customer");

        Assert.IsTrue(deleted, message);
        Assert.IsFalse(File.Exists(PathOf("App/Customer.cs")));
    }

    [TestMethod]
    // delete class left Customer.Validation.cs behind, a partial of a class that no longer exists,
    // and list classes showed it as a class called Customer.Validation.
    public void Delete_takes_the_validation_partial_and_restore_brings_it_back()
    {
        Write("App/App.csproj", Csproj);
        Write("App/Customer.cs", CustomerCode);
        Write("App/Customer.Validation.cs", CustomerValidation);
        Write("App/Order.cs", "namespace App { public class Order { } }");

        var (deleted, message) = ClassDeletionService.DeleteClass(_workspace, "Customer");

        Assert.IsTrue(deleted, message);
        Assert.IsFalse(File.Exists(PathOf("App/Customer.cs")), message);
        Assert.IsFalse(File.Exists(PathOf("App/Customer.Validation.cs")), message);
        StringAssert.Contains(message, "Customer.Validation.cs");
        AssertLiveSourcesCompile();

        var (restored, restoreMessage) = ClassRestoreService.RestoreClass(_workspace, "Customer");

        Assert.IsTrue(restored, restoreMessage);
        Assert.AreEqual(CustomerCode, File.ReadAllText(PathOf("App/Customer.cs")));
        Assert.AreEqual(CustomerValidation, File.ReadAllText(PathOf("App/Customer.Validation.cs")));
        AssertLiveSourcesCompile();
    }

    [TestMethod]
    // The copies were kept as .cs inside the project, which an SDK project compiles: a new
    // Customer beside a kept Customer.Validation.cs failed with CS0260.
    public void The_kept_copies_are_not_compiled_with_the_project()
    {
        Write("App/App.csproj", Csproj);
        Write("App/Customer.cs", CustomerCode);
        Write("App/Customer.Validation.cs", CustomerValidation);
        Assert.IsTrue(ClassDeletionService.DeleteClass(_workspace, "Customer").Success);
        Write("App/Customer.cs", "namespace App { public class Customer { public int Age { get; set; } } }");

        // What the SDK's default Compile glob takes: every .cs under the project folder.
        var globbed = Directory.GetFiles(PathOf("App"), "*.cs", SearchOption.AllDirectories);
        var result = Compiler.CompileSources([.. globbed.Select(f => (f, File.ReadAllText(f)))]);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
        Assert.HasCount(2, Directory.GetFiles(PathOf("App/DeletedClasses")));
    }

    [TestMethod]
    public void Delete_drops_the_class_rules_and_says_so()
    {
        Write("App/App.csproj", Csproj);
        Write("App/Customer.cs", CustomerCode);
        foreach (var rule in ValidationRuleCatalog.Default.ParseOne("Name is required", ValidationCategory.Data,
                     new ValidationParseContext { Fragment = "Name is required", ClassName = "Customer" }))
            ValidationRegistry.Add("Customer", rule);
        Assert.IsNotEmpty(ValidationRegistry.Get("Customer"));

        var (deleted, message) = ClassDeletionService.DeleteClass(_workspace, "customer");

        Assert.IsTrue(deleted, message);
        Assert.IsEmpty(ValidationRegistry.Get("Customer"));
        StringAssert.Contains(message, "validation rule");
    }

    [TestMethod]
    // File.Move(overwrite: true) replaced the live class with the deleted copy, and said "restored".
    public void Restore_refuses_while_a_live_class_has_the_name()
    {
        Write("App/App.csproj", Csproj);
        Write("App/Customer.cs", "namespace App { public class Customer { public int Old { get; set; } } }");
        Assert.IsTrue(ClassDeletionService.DeleteClass(_workspace, "Customer").Success);
        const string live = "namespace App { public class Customer { public int New { get; set; } } }";
        Write("App/Customer.cs", live);

        var (restored, message) = ClassRestoreService.RestoreClass(_workspace, "Customer");

        Assert.IsFalse(restored, message);
        StringAssert.Contains(message, "already exists");
        Assert.AreEqual(live, File.ReadAllText(PathOf("App/Customer.cs")));
        Assert.HasCount(1, DeletedClassRegistry.Get(_workspace, "Customer"), "the deleted copy is still restorable");
    }

    [TestMethod]
    // Two deleted copies: one restore removed both registry entries and moved whichever backup the
    // folder listed first over the other, so the older copy was lost.
    public void Two_deleted_copies_restore_newest_first_one_per_restore()
    {
        Write("App/App.csproj", Csproj);
        const string v1 = "namespace App { public class Customer { public int V1 { get; set; } } }";
        const string v2 = "namespace App { public class Customer { public int V2 { get; set; } } }";
        Write("App/Customer.cs", v1);
        Assert.IsTrue(ClassDeletionService.DeleteClass(_workspace, "Customer").Success);
        Thread.Sleep(20);
        Write("App/Customer.cs", v2);
        Assert.IsTrue(ClassDeletionService.DeleteClass(_workspace, "Customer").Success);

        var (first, message) = ClassRestoreService.RestoreClass(_workspace, "Customer");

        Assert.IsTrue(first, message);
        Assert.AreEqual(v2, File.ReadAllText(PathOf("App/Customer.cs")));
        Assert.HasCount(1, DeletedClassRegistry.Get(_workspace, "Customer"));
        StringAssert.Contains(message, "1 older deleted copy");

        File.Delete(PathOf("App/Customer.cs"));
        var (second, secondMessage) = ClassRestoreService.RestoreClass(_workspace, "Customer");

        Assert.IsTrue(second, secondMessage);
        Assert.AreEqual(v1, File.ReadAllText(PathOf("App/Customer.cs")));
        Assert.IsEmpty(DeletedClassRegistry.Get(_workspace, "Customer"));
    }

    [TestMethod]
    // Each restore took the first backup named Customer_* anywhere in the workspace, so with a
    // Customer deleted from two projects a project could get the other one's file.
    public void Same_named_classes_from_two_projects_each_go_back_to_their_own_project()
    {
        Write("A/A.csproj", Csproj);
        Write("B/B.csproj", Csproj);
        const string a = "namespace A { public class Customer { public int InA { get; set; } } }";
        const string b = "namespace B { public class Customer { public int InB { get; set; } } }";
        Write("A/Customer.cs", a);
        Write("B/Customer.cs", b);
        Assert.IsTrue(ClassDeletionService.DeleteClass(_workspace, "Customer").Success);

        Assert.IsTrue(ClassRestoreService.RestoreClass(_workspace, "Customer").Success);
        Assert.IsTrue(ClassRestoreService.RestoreClass(_workspace, "Customer").Success);

        Assert.AreEqual(a, File.ReadAllText(PathOf("A/Customer.cs")));
        Assert.AreEqual(b, File.ReadAllText(PathOf("B/Customer.cs")));
    }

    [TestMethod]
    // restore class Nope printed nothing at all, and the service's own text was "Restore
    // metadata not found.", which does not say what to do.
    public void Restoring_a_class_with_no_deleted_copy_says_why()
    {
        Write("App/App.csproj", Csproj);
        Write("App/Customer.cs", CustomerCode);

        var (none, noneMessage) = ClassRestoreService.RestoreClass(_workspace, "Nope");
        var (live, liveMessage) = ClassRestoreService.RestoreClass(_workspace, "Customer");

        Assert.IsFalse(none);
        StringAssert.Contains(noneMessage, "No deleted copy of class 'Nope'");
        StringAssert.Contains(noneMessage, "list deleted classes");
        Assert.IsFalse(live);
        StringAssert.Contains(liveMessage, "already exists");
    }

    [TestMethod]
    public void List_classes_does_not_show_a_partial_file_as_its_own_class()
    {
        Write("App/Customer.cs", CustomerCode);
        Write("App/Customer.Validation.cs", CustomerValidation);
        Write("App/Validation/BookValidator.Custom.cs", "namespace App { public partial class BookValidator { } }");
        Write("App/Validation/BookValidator.Generated.cs", "namespace App { public partial class BookValidator { } }");

        var names = ProjectManager.ClassNamesIn(_workspace);

        CollectionAssert.Contains(names.ToList(), "Customer");
        CollectionAssert.DoesNotContain(names.ToList(), "Customer.Validation");
        // No BookValidator.cs: its halves are the only names the class can be reached by.
        CollectionAssert.Contains(names.ToList(), "BookValidator.Custom");
    }

    [TestMethod]
    // It printed "Registry Count: N" to the console and showed the UTC time as if it were local.
    public void List_deleted_classes_prints_no_debug_line_and_shows_local_time()
    {
        Write("App/App.csproj", Csproj);
        Write("App/Customer.cs", CustomerCode);
        Assert.IsTrue(ClassDeletionService.DeleteClass(_workspace, "Customer").Success);
        var entry = DeletedClassRegistry.Get(_workspace, "Customer").Single();

        var console = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        string listing;
        try
        {
            listing = DeletedClassListingService.GetDeletedClasses(_workspace);
        }
        finally
        {
            Console.SetOut(console);
        }

        Assert.AreEqual(string.Empty, captured.ToString());
        Assert.AreEqual(DateTimeKind.Utc, entry.DeletedAt.Kind);
        StringAssert.Contains(listing, entry.DeletedAt.ToLocalTime().ToString("g"));
    }
}
