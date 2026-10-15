using System.Text;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Edits of code the user already has change only what was asked for: the file keeps its
/// byte-order mark, its CRLF line ends, its blank lines, its #region layout and its one-line
/// statements, and the result compiles without new nullable warnings.
/// </summary>
/// <remarks>
/// The fixture is the shape of a real hand-written library: BOM and CRLF, nullable on, a
/// file-scoped and a block namespace, XML docs, #region, attributes, a partial class over two
/// files, a nested class, and an enum and a record struct in the class's file.
/// </remarks>
[TestClass]
[DoNotParallelize] // create class goes through CommandHandlingService, which keeps static state.
public class SourceFidelityEditTests
{
    private string _root = string.Empty;

    private const string CustomerSource = """
        // Copyright (c) Sf Ltd. All rights reserved.
        using System.ComponentModel.DataAnnotations;

        namespace SfClsLib.Models;

        /// <summary>
        /// A customer who buys from the shop.
        /// </summary>
        [Serializable]
        public partial class Customer : EntityBase, IAuditable
        {
            /// <summary>Maximum name length.</summary>
            public const int MaxNameLength = 80;

            #region Identity

            /// <summary>The primary key.</summary>
            [Key]
            public int CustomerId { get; set; }

            [Required]
            [MaxLength(MaxNameLength)]
            public string Name { get; set; } = "Unknown";

            #endregion

            [Range(0, 150)]
            public int? Age { get; init; }

            public decimal CreditLimit { get; private set; } = 1000.50m;

            public CustomerStatus Status { get; set; } = CustomerStatus.Active;

            public List<string> Tags { get; set; } = new();

            public Dictionary<string, decimal> Balances { get; set; } = new Dictionary<string, decimal>();

            public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

            /// <summary>Raises the credit limit.</summary>
            /// <param name="amount">How much to add.</param>
            public void RaiseLimit(decimal amount)
            {
                if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
                CreditLimit += amount; // keep running total
            }

            public class Address
            {
                public string Street { get; set; } = string.Empty;
            }
        }

        public enum CustomerStatus
        {
            Active = 1,
            Closed = 9,
        }

        /// <summary>A value type next to the class.</summary>
        public readonly record struct Money(decimal Amount, string Currency = "GBP");

        """;

    private const string NotesSource = """
        namespace SfClsLib.Models;

        public partial class Customer
        {
            public string? Notes { get; set; }
        }

        """;

    private const string EntityBaseSource = """
        using System;

        namespace SfClsLib.Models
        {
            /// <summary>Base for every entity.</summary>
            public abstract class EntityBase
            {
                public Guid RowId { get; set; } = Guid.NewGuid();

                public virtual string DisplayName => GetType().Name;
            }

            public interface IAuditable
            {
                DateTime CreatedOn { get; set; }
            }
        }

        """;

    private const string OrderSource = """
        namespace SfClsLib.Models;

        /// <summary>An order placed by a customer.</summary>
        public class Order : EntityBase
        {
            public int OrderId { get; set; }

            public Customer? Customer { get; set; }

            public List<OrderLine> Lines { get; set; } = [];
        }

        public record OrderLine(int ProductId, int Quantity)
        {
            public decimal UnitPrice { get; init; } = 0m;
        }

        """;

    // Indented with tabs, in a block namespace.
    private const string ProductSource = "namespace SfClsLib.Models\n{\n\tpublic class Product\n\t{\n\t\tpublic int ProductId { get; set; }\n\n\t\tpublic decimal Price { get; set; } = 9.99m;\n\t}\n}\n";

    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;

        """;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "sf-edit-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "Models"));

        File.WriteAllText(Path.Combine(_root, "SfClsLib.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);

        Write("Customer.cs", CustomerSource);
        Write("Customer.Notes.cs", NotesSource);
        Write("EntityBase.cs", EntityBaseSource);
        Write("Order.cs", OrderSource);
        Write("Product.cs", ProductSource);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string PathOf(string name) => Path.Combine(_root, "Models", name);

    /// <summary>Written as the user's editor writes it: a BOM and CRLF.</summary>
    private void Write(string name, string source)
    {
        var crlf = source.Replace("\r\n", "\n").Replace("\n", "\r\n");
        File.WriteAllText(PathOf(name), crlf, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Original(string source) => source.Replace("\r\n", "\n").Replace("\n", "\r\n");

    private byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));

    private string Text(string name) => File.ReadAllText(PathOf(name));

    private void AssertKeptBomAndCrlf(string name)
    {
        var bytes = Bytes(name);
        Assert.IsTrue(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            $"{name} lost its byte-order mark");

        var text = Text(name);
        var lone = text.Replace("\r\n", string.Empty).Count(c => c == '\n');
        Assert.AreEqual(0, lone, $"{name} has LF-only line ends:\n{text}");
        Assert.IsTrue(text.EndsWith("\r\n", StringComparison.Ordinal), $"{name} lost its final newline");
    }

    /// <summary>Every line of the original is still there, in order, and only
    /// <paramref name="added"/> lines are new.</summary>
    private void AssertOnlyAdded(string name, string original, int added)
    {
        var before = Original(original).Split("\r\n");
        var after = Text(name).Split("\r\n");

        var at = 0;
        foreach (var line in after)
            if (at < before.Length && line == before[at])
                at++;

        Assert.AreEqual(before.Length, at,
            $"{name}: original line {at + 1} ('{(at < before.Length ? before[at] : "")}') is gone or changed:\n{Text(name)}");
        Assert.HasCount(before.Length + added, after, Text(name));
    }

    /// <summary>Compiles every file, and fails on an error or a CS8618 (non-nullable left null).</summary>
    private void AssertCompilesWithoutNullWarnings()
    {
        var sources = Directory.GetFiles(Path.Combine(_root, "Models"), "*.cs")
            .Select(f => (f, File.ReadAllText(f)))
            .Append(("Usings.cs", ImplicitUsings));

        var result = Compiler.CompileSources(sources);
        var bad = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error || d.Id == "CS8618")
            .ToList();

        Assert.IsEmpty(bad, string.Join(Environment.NewLine, bad));
    }

    [TestMethod]
    public void The_fixture_compiles_as_written() => AssertCompilesWithoutNullWarnings();

    [TestMethod]
    public void Add_property_inserts_one_member_and_changes_nothing_else()
    {
        Assert.IsTrue(ClassPropertyService.AddProperty(_root, "Customer", "Phone", "string", out var message), message);

        AssertKeptBomAndCrlf("Customer.cs");

        // A blank line and the property: the members are separated by blank lines.
        AssertOnlyAdded("Customer.cs", CustomerSource, 2);
        StringAssert.Contains(Text("Customer.cs"), "\r\n    public string Phone { get; set; } = string.Empty;\r\n}");
        AssertCompilesWithoutNullWarnings();
    }

    [TestMethod]
    [DataRow("List<string>", "new()")]
    [DataRow("string[]", "[]")]
    [DataRow("IReadOnlyList<int>", "[]")]
    [DataRow("Order", "null!")]
    public void Add_property_of_a_reference_type_in_a_nullable_project_starts_with_a_value(string type, string value)
    {
        Assert.IsTrue(ClassPropertyService.AddProperty(_root, "Customer", "Extra", type, out var message), message);

        StringAssert.Contains(Text("Customer.cs"), $"public {type} Extra {{ get; set; }} = {value};");
        AssertCompilesWithoutNullWarnings();
    }

    [TestMethod]
    public void Add_property_of_a_value_or_nullable_type_has_no_initial_value()
    {
        Assert.IsTrue(ClassPropertyService.AddProperty(_root, "Customer", "Count", "int", out var m1), m1);
        Assert.IsTrue(ClassPropertyService.AddProperty(_root, "Customer", "Fax", "string?", out var m2), m2);

        StringAssert.Contains(Text("Customer.cs"), "public int Count { get; set; }\r\n");
        StringAssert.Contains(Text("Customer.cs"), "public string? Fax { get; set; }\r\n");
    }

    [TestMethod]
    public void Add_property_keeps_a_tab_indented_file_in_tabs()
    {
        Assert.IsTrue(ClassPropertyService.AddProperty(_root, "Product", "Sku", "int", out var message), message);

        AssertKeptBomAndCrlf("Product.cs");
        AssertOnlyAdded("Product.cs", ProductSource, 2);
        StringAssert.Contains(Text("Product.cs"), "\r\n\t\tpublic int Sku { get; set; }\r\n\t}");
    }

    [TestMethod]
    public void Add_method_inserts_one_member_and_changes_nothing_else()
    {
        Assert.IsTrue(ClassMethodService.AddMethod(_root, "Customer", "Close", "void", out var message), message);

        AssertKeptBomAndCrlf("Customer.cs");
        var text = Text("Customer.cs");
        StringAssert.Contains(text, "if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));");
        StringAssert.Contains(text, "/// <param name=\"amount\">How much to add.</param>");
        StringAssert.Contains(text, "\r\n    #region Identity\r\n");
        StringAssert.Contains(text, "public void Close()");
        AssertOnlyAdded("Customer.cs", CustomerSource, text.Split("\r\n").Length - Original(CustomerSource).Split("\r\n").Length);
        AssertCompilesWithoutNullWarnings();
    }

    [TestMethod]
    public void Inherits_changes_only_the_declaration_line()
    {
        Assert.IsTrue(ClassInheritanceService.AddInheritance(_root, "Product", "EntityBase", out var message), message);

        AssertKeptBomAndCrlf("Product.cs");
        Assert.AreEqual(
            Original(ProductSource).Replace("public class Product\r\n", "public class Product : EntityBase\r\n"),
            Text("Product.cs"));
        AssertCompilesWithoutNullWarnings();
    }

    [TestMethod]
    public void Implementing_another_interface_changes_only_the_declaration_line()
    {
        Assert.IsTrue(ClassInheritanceService.AddInheritance(_root, "Order", "IDisposable", out var message), message);

        Assert.AreEqual(
            Original(OrderSource).Replace("public class Order : EntityBase\r\n", "public class Order : EntityBase, IDisposable\r\n"),
            Text("Order.cs"));
        AssertKeptBomAndCrlf("Order.cs");
    }

    [TestMethod]
    public void Remove_rename_and_retype_keep_the_byte_order_mark()
    {
        Assert.IsTrue(ClassPropertyService.RemoveProperty(_root, "Customer", "CreatedOn", out var m1), m1);
        AssertKeptBomAndCrlf("Customer.cs");

        Write("Customer.cs", CustomerSource);
        Assert.IsTrue(ClassPropertyRenameService.RenameProperty(_root, "Customer", "Name", "FullName", out var m2), m2);
        AssertKeptBomAndCrlf("Customer.cs");

        Write("Customer.cs", CustomerSource);
        Assert.IsTrue(ClassPropertyTypeService.ChangeType(_root, "Customer", "CreditLimit", "double", out var m3), m3);
        AssertKeptBomAndCrlf("Customer.cs");
    }

    [TestMethod]
    public void Rename_class_keeps_the_byte_order_mark_of_every_file_it_edits()
    {
        Assert.IsTrue(ClassRenameService.RenameClass(_root, "Customer", "Client", out var message), message);

        AssertKeptBomAndCrlf("Client.cs");
        AssertKeptBomAndCrlf("Client.Notes.cs");
        AssertKeptBomAndCrlf("Order.cs");
        StringAssert.Contains(Text("Order.cs"), "public Client? Customer { get; set; }");
    }

    [TestMethod]
    [DataRow("Status", "string", "= string.Empty;")]
    [DataRow("Tags", "string[]", "= [];")]
    [DataRow("Balances", "Dictionary<string, double>", "= new();")]
    public void Retype_to_a_reference_type_gives_a_value_when_the_old_one_does_not_fit(
        string property, string type, string initializer)
    {
        Assert.IsTrue(ClassPropertyTypeService.ChangeType(_root, "Customer", property, type, out var message), message);

        StringAssert.Contains(Text("Customer.cs"), $"public {type} {property} {{ get; set; }} {initializer}\r\n");
        AssertCompilesWithoutNullWarnings();
    }

    [TestMethod]
    public void Retype_to_string_removes_a_range_check()
    {
        Assert.IsTrue(ClassPropertyTypeService.ChangeType(_root, "Customer", "Age", "string", out var message), message);

        var text = Text("Customer.cs");
        Assert.DoesNotContain("[Range(0, 150)]", text);
        StringAssert.Contains(message, "[Range]");
        StringAssert.Contains(text, "public string Age { get; init; } = string.Empty;");
        AssertCompilesWithoutNullWarnings();
    }

    [TestMethod]
    public void Show_methods_shows_the_parameters()
    {
        var methods = ClassMethodLister.MethodsIn(PathOf("Customer.cs")).Select(m => m.Display).ToList();

        CollectionAssert.Contains(methods, "void RaiseLimit(decimal amount)");
    }

    /// <summary>GetType().Name in EntityBase is System.Type's Name, not Customer's.</summary>
    [TestMethod]
    public void Rename_property_names_only_code_that_uses_that_class_member()
    {
        Write("Report.cs", """
            namespace SfClsLib.Models;

            public static class Report
            {
                public static string Of(Customer customer) => customer.Name;
            }
            """);

        Assert.IsTrue(ClassPropertyRenameService.RenameProperty(_root, "Customer", "Name", "FullName", out var message), message);

        var users = MemberReferences.UsersOf(_root, "Customer", "Name");

        CollectionAssert.Contains(users.ToList(), "Report.Of()");
        Assert.IsFalse(users.Any(u => u.StartsWith("EntityBase", StringComparison.Ordinal)), string.Join(", ", users));
    }

    [TestMethod]
    [DataRow("OrderLine", "record OrderLine")]
    [DataRow("IAuditable", "interface IAuditable")]
    public void Create_class_refuses_a_name_another_file_declares(string name, string declaration)
    {
        var before = Directory.GetFiles(Path.Combine(_root, "Models")).Length;
        var command = new CodeGenerationEngine.Models.ParsedCommand
        {
            Intent = CodeGenerationEngine.Models.IntentType.CreateClass,
            ClassName = name,
            OutputFolder = Path.Combine(_root, "Models"),
            OriginalSentence = "create class " + name,
        };
        CodeGenerationEngine.Models.ProjectMetaData? project = null;

        var writer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(writer);
        bool ok;
        try
        {
            ok = CodeGenerationEngine.Service.CommandHandlingService.Execute(command, ref project, [], _root, _ => { });
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.IsFalse(ok);
        StringAssert.Contains(writer.ToString(), $"'{name}' already exists: it is declared in");

        // delete class works on a class's own file, so it is not offered for one declared elsewhere.
        Assert.DoesNotContain("delete class", writer.ToString());
        Assert.HasCount(before, Directory.GetFiles(Path.Combine(_root, "Models")));
        Assert.IsFalse(File.Exists(PathOf(name + ".cs")));
        StringAssert.Contains(File.ReadAllText(ClassFiles.DeclaringFile(_root, name)!), declaration);
    }

    [TestMethod]
    public void Create_class_leaves_a_project_file_that_needs_nothing_byte_for_byte()
    {
        var csproj = Path.Combine(_root, "Tabbed.csproj");
        var original = "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n\t<PropertyGroup>\r\n\t\t<TargetFramework>net10.0</TargetFramework>\r\n\t</PropertyGroup>\r\n\r\n</Project>\r\n";
        File.WriteAllText(csproj, original);
        var bytes = File.ReadAllBytes(csproj);

        ProjectFileUpdater.Update(csproj, PathOf("Thing.cs"), "public class Thing { }");

        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(csproj));

        // When a line is needed, only that is added, in the file's own tabs and line ends.
        ProjectFileUpdater.Update(csproj, PathOf("Thing.cs"), "public class Thing { [Required] public string A { get; set; } = \"\"; }");

        Assert.AreEqual(
            original.Replace("\r\n</Project>", "\r\n\t<ItemGroup>\r\n\t\t<PackageReference Include=\"System.ComponentModel.Annotations\" Version=\"5.0.0\" />\r\n\t</ItemGroup>\r\n</Project>"),
            File.ReadAllText(csproj));
        Assert.AreNotEqual(0xEF, File.ReadAllBytes(csproj)[0], "a byte-order mark was added");
    }

    [TestMethod]
    [DataRow("OrderLine", "Order.cs")]
    [DataRow("Money", "Customer.cs")]
    [DataRow("IAuditable", "EntityBase.cs")]
    public void A_type_declared_in_another_type_file_can_get_a_property(string type, string file)
    {
        Assert.IsTrue(ClassPropertyService.AddProperty(_root, type, "Note", "int", out var message), message);

        StringAssert.Contains(Text(file), " Note { get;");
        AssertKeptBomAndCrlf(file);

        // An interface's new member is Customer's to implement (the command says so); the
        // record and the record struct compile as they are.
        if (type != "IAuditable")
            AssertCompilesWithoutNullWarnings();
    }
}
