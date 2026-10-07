using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize] // ValidationRegistry is a process-wide static.
public sealed class ClassRenameServiceTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "cg_renameclass_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private string Write(string relative, string code)
    {
        var file = Path.GetFullPath(Path.Combine(_workspace, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, code);
        return file;
    }

    private static void AssertCompiles(params string[] files)
    {
        var result = Compiler.CompileSources([.. files.Select(f => (f, File.ReadAllText(f)))]);
        Assert.IsTrue(result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
    }

    [TestMethod]
    public void A_class_is_renamed()
    {
        Write("Employee.cs", "namespace App { public class Employee { public string Name { get; set; } } }");

        Assert.IsTrue(ClassRenameService.RenameClass(_workspace, "Employee", "Staff", out var message), message);

        var renamed = Path.Combine(_workspace, "Staff.cs");
        Assert.IsFalse(File.Exists(Path.Combine(_workspace, "Employee.cs")));
        StringAssert.Contains(File.ReadAllText(renamed), "class Staff");
        AssertCompiles(renamed);
    }

    private const string Csproj = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>";

    private string[] AllSources() => [.. ClassFiles.Under(_workspace)];

    [TestMethod]
    // Renamed in one file only: the constructor kept the old name (CS1520) and Loan, the repository
    // and the validator still named Member (CS0246), under a "renamed" message.
    public void The_constructor_and_every_reference_are_renamed()
    {
        Write("Library/Library.csproj", Csproj);
        Write("Library/Models/Member.cs", """
            namespace Library.Models
            {
                public class Member
                {
                    public int Id { get; set; }
                    public List<Loan> Loans { get; set; } = null!;
                    public Member() { }
                }
            }
            """);
        Write("Library/Models/Loan.cs", """
            namespace Library.Models
            {
                public class Loan
                {
                    public Member Member { get; set; } = null!;
                    public int MemberId { get; set; }
                    public string Who() => Member.Id.ToString() + nameof(Member);
                }
            }
            """);
        Write("Library/Data/MemberRepository.cs", """
            using Library.Models;
            using M = Library.Models.Member;
            namespace Library.Data
            {
                public class MemberRepository
                {
                    private readonly List<Member> _items = new();
                    public Member Add() { var m = new Member(); _items.Add(m); return m; }
                    public M First() => _items[0];
                    public string Label => "Member";
                }
            }
            """);

        Assert.IsTrue(ClassRenameService.RenameClass(_workspace, "Member", "Patron", out var message, out var warnings), message);

        Assert.IsFalse(File.Exists(Path.Combine(_workspace, "Library/Models/Member.cs")));
        var loan = File.ReadAllText(Path.Combine(_workspace, "Library/Models/Loan.cs"));
        StringAssert.Contains(loan, "public Patron Member { get; set; }", "the property keeps its name, the type changes");
        StringAssert.Contains(loan, "Member.Id", "a property access is not the type");
        StringAssert.Contains(File.ReadAllText(Path.Combine(_workspace, "Library/Models/Patron.cs")), "public Patron()");
        Assert.IsTrue(warnings.Any(w => w.Contains("MemberRepository.cs")), string.Join("|", warnings));
        AssertCompiles([.. AllSources(), GlobalUsings()]);
    }

    private string GlobalUsings() => Write("Usings.g.cs",
        "global using System; global using System.Collections.Generic; global using System.Linq;");

    [TestMethod]
    // generate validator wrote Customer.Validation.cs; after the rename it still declared
    // partial class Customer, a second class (CS1061 where Validate() was called).
    public void The_validation_partial_is_renamed_with_the_class()
    {
        Write("Customer.cs", "namespace App { public partial class Customer { public string Name { get; set; } = \"\"; } }");
        Write("Customer.Validation.cs", "namespace App { public partial class Customer { public bool Validate() => Name.Length > 0; } }");
        Write("Use.cs", "namespace App { public static class Use { public static bool Run(Customer c) => c.Validate(); } }");

        Assert.IsTrue(ClassRenameService.RenameClass(_workspace, "customer", "Client", out var message), message);

        Assert.IsTrue(File.Exists(Path.Combine(_workspace, "Client.Validation.cs")), message);
        Assert.IsFalse(File.Exists(Path.Combine(_workspace, "Customer.Validation.cs")), message);
        StringAssert.Contains(File.ReadAllText(Path.Combine(_workspace, "Client.Validation.cs")), "partial class Client");
        AssertCompiles(AllSources());
    }

    [TestMethod]
    // The pattern knew only the word class, so the file moved and the interface kept its name.
    public void An_interface_is_renamed_and_its_implementers_follow()
    {
        Write("IShape.cs", "namespace App { public interface IShape { double Area(); } }");
        Write("Circle.cs", "namespace App { public class Circle : IShape { public double Area() => 1; } }");

        Assert.IsTrue(ClassRenameService.RenameClass(_workspace, "IShape", "IFigure", out var message), message);

        StringAssert.Contains(message, "Interface 'IShape' renamed");
        StringAssert.Contains(File.ReadAllText(Path.Combine(_workspace, "IFigure.cs")), "interface IFigure");
        StringAssert.Contains(File.ReadAllText(Path.Combine(_workspace, "Circle.cs")), ": IFigure");
        AssertCompiles(AllSources());
    }

    [TestMethod]
    public void A_test_project_referencing_the_class_project_is_renamed_too()
    {
        Write("Lib/Lib.csproj", Csproj);
        Write("Lib/Member.cs", "namespace Lib { public class Member { public int Id { get; set; } } }");
        Write("Lib.Tests/Lib.Tests.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"..\\Lib\\Lib.csproj\" /></ItemGroup></Project>");
        Write("Lib.Tests/MemberTests.cs", "using Lib; namespace Lib.Tests { public class MemberTests { public Member Make() => new Member(); } }");

        Assert.IsTrue(ClassRenameService.RenameClass(_workspace, "Member", "Patron", out var message, out var warnings), message);

        StringAssert.Contains(File.ReadAllText(Path.Combine(_workspace, "Lib.Tests/MemberTests.cs")), "public Patron Make() => new Patron();");
        Assert.IsEmpty(warnings, string.Join("|", warnings));
        AssertCompiles(AllSources());
    }

    [TestMethod]
    public void Code_outside_the_projects_it_could_bind_is_named()
    {
        Write("Lib/Lib.csproj", Csproj);
        Write("Lib/Member.cs", "namespace Lib { public class Member { } }");
        Write("Web/Web.csproj", Csproj);
        Write("Web/Pages/Members.razor", "@code { Lib.Member? item; }");

        Assert.IsTrue(ClassRenameService.RenameClass(_workspace, "Member", "Patron", out var message, out var warnings), message);

        Assert.IsTrue(warnings.Any(w => w.Contains("Members.razor")), string.Join("|", warnings));
    }

    [TestMethod]
    public void Renaming_to_the_same_name_says_nothing_changed()
    {
        const string code = "namespace App { public class Employee { } }";
        var file = Write("Employee.cs", code);

        Assert.IsTrue(ClassRenameService.RenameClass(_workspace, "Employee", "Employee", out var message), message);

        StringAssert.Contains(message, "nothing was changed");
        Assert.AreEqual(code, File.ReadAllText(file));
    }

    [TestMethod]
    // create class PascalCases a name; rename kept employee2 as typed.
    public void A_lower_case_new_name_gets_a_capital_like_create_class()
    {
        Write("Employee.cs", "namespace App { public class Employee { } }");

        Assert.IsTrue(ClassRenameService.RenameClass(_workspace, "Employee", "employee2", out var message), message);

        StringAssert.Contains(File.ReadAllText(Path.Combine(_workspace, "Employee2.cs")), "class Employee2");
    }

    [TestMethod]
    public void The_class_rules_move_with_it()
    {
        Write("Customer.cs", "namespace App { public class Customer { public string Name { get; set; } = \"\"; } }");
        ValidationRegistry.Clear();
        var rule = CodeGenerationEngine.Core.Validation.ValidationRuleCatalog.Default.ParseOne(
            "Name is required", CodeGenerationEngine.Core.Validation.ValidationCategory.Data,
            new CodeGenerationEngine.Core.Validation.ValidationParseContext { Fragment = "Name is required", ClassName = "Customer" });
        foreach (var r in rule)
            ValidationRegistry.Add("Customer", r);
        try
        {
            Assert.IsTrue(ClassRenameService.RenameClass(_workspace, "Customer", "Client", out var message), message);

            Assert.IsEmpty(ValidationRegistry.Get("Customer"));
            Assert.HasCount(rule.Count, ValidationRegistry.Get("Client"));
            Assert.IsNotEmpty(rule);
        }
        finally
        {
            ValidationRegistry.Clear();
        }
    }

    [TestMethod]
    // "rename class Employee to Manager" with Manager.cs there moved Employee.cs over it
    // (File.Move with overwrite), and the Manager class was gone.
    [DataRow("Manager.cs")]
    [DataRow("manager.cs")]
    [DataRow("Models/Manager.cs")]
    public void A_class_is_not_renamed_onto_an_existing_one(string existingPath)
    {
        const string employee = "namespace App { public class Employee { public string Name { get; set; } } }";
        const string manager = "namespace App.Models { public class Manager { public int Reports { get; set; } } }";
        var employeeFile = Write("Employee.cs", employee);
        var managerFile = Write(existingPath, manager);

        Assert.IsFalse(ClassRenameService.RenameClass(_workspace, "Employee", "Manager", out var message), message);

        Assert.AreEqual(employee, File.ReadAllText(employeeFile));
        Assert.AreEqual(manager, File.ReadAllText(managerFile));
        StringAssert.Contains(message, managerFile, message);
        AssertCompiles(employeeFile, managerFile);
    }
}
