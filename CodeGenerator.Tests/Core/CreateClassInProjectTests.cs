using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Gaps 5 #4: <c>create class</c> was compiled alone, so a project interface or base class was
/// "CS0246 could not be found", while the class, saved anyway, then failed <c>dotnet build</c>
/// with CS0535 for the interface members it never implemented.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CreateClassInProjectTests
{
    private string _workspace = string.Empty;
    private string LastOutput { get; set; } = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_cip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_workspace, "Shop"));
        File.WriteAllText(Path.Combine(_workspace, "Shop", "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        ProjectManager.CurrentProject = new ProjectMetaData
        {
            Success = true,
            ProjectName = "Shop",
            ProjectPath = Path.Combine(_workspace, "Shop"),
            CsProjPath = Path.Combine(_workspace, "Shop", "Shop.csproj")
        };
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private bool Run(string input)
    {
        ProjectMetaData? project = ProjectManager.CurrentProject;
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try { return CommandFront.Run(input, ref project, [], _workspace, _ => { }); }
        finally
        {
            Console.SetOut(original);
            LastOutput = writer.ToString();
        }
    }

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(_workspace, "Shop", name), text);

    /// <summary>The project's files, with the SDK's implicit usings as a project with
    /// ImplicitUsings on gets them.</summary>
    private IEnumerable<(string, string)> ProjectSources(string prefix = "") =>
        Directory.GetFiles(Path.Combine(_workspace, "Shop"), "*.cs", SearchOption.AllDirectories)
            .Select(f => (f, prefix + File.ReadAllText(f)))
            .Append(("GlobalUsings.g.cs", "global using System; global using System.Collections.Generic; global using System.Linq;"));

    private string[] ProjectErrors() =>
    [
        .. Compiler.CompileSources(ProjectSources())
            .Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
    ];

    [TestMethod]
    public void A_class_implementing_a_project_interface_gets_its_members_and_compiles()
    {
        Write("IShape.cs", """
            namespace Shop;

            public interface IShape
            {
                decimal Area { get; }
                void Draw(int scale, string? label);
                string Name { get; set; }
            }
            """);

        Assert.IsTrue(Run("create class Circle with properties Radius decimal implements IShape"), LastOutput);

        Assert.DoesNotContain("CS0246", LastOutput, LastOutput);
        Assert.DoesNotContain("does not compile", LastOutput, LastOutput);
        var errors = ProjectErrors();
        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors) + Environment.NewLine
            + File.ReadAllText(Path.Combine(_workspace, "Shop", "Circle.cs")));
        StringAssert.Contains(LastOutput, "Area");
        StringAssert.Contains(LastOutput, "Draw");
        StringAssert.Contains(File.ReadAllText(Path.Combine(_workspace, "Shop", "Circle.cs")), "NotImplementedException");
    }

    [TestMethod]
    public void Create_class_with_a_base_after_any_name_writes_the_base()
    {
        Write("Base1.cs", "namespace Shop;\n\npublic class Base1 { public int Id { get; set; } }\n");

        Assert.IsTrue(Run("create class A inherits Base1"), LastOutput);
        Assert.IsTrue(Run("create class B : Base1"), LastOutput);

        StringAssert.Contains(File.ReadAllText(Path.Combine(_workspace, "Shop", "A.cs")), "class A : Base1");
        StringAssert.Contains(File.ReadAllText(Path.Combine(_workspace, "Shop", "B.cs")), "class B : Base1");
        Assert.IsEmpty(ProjectErrors());
    }

    [TestMethod]
    public void Create_class_with_base_and_interface_writes_both_in_order()
    {
        Write("Base1.cs", "namespace Shop;\n\npublic class Base1 { public int Id { get; set; } }\n");
        Write("IMark1.cs", "namespace Shop;\n\npublic interface IMark1 { }\n");

        Assert.IsTrue(Run("create class B : Base1, IMark1"), LastOutput);

        StringAssert.Contains(File.ReadAllText(Path.Combine(_workspace, "Shop", "B.cs")), "class B : Base1, IMark1");
        Assert.IsEmpty(ProjectErrors());
    }

    [TestMethod]
    public void Create_struct_with_a_colon_interface_compiles()
    {
        Write("IShape1.cs", "namespace Shop;\n\npublic interface IShape1 { }\n");

        Assert.IsTrue(Run("create struct P : IShape1"), LastOutput);

        StringAssert.Contains(File.ReadAllText(Path.Combine(_workspace, "Shop", "P.cs")), "struct P : IShape1");
        Assert.IsEmpty(ProjectErrors());
    }

    [TestMethod]
    public void A_class_inheriting_a_project_class_is_not_reported_as_unknown()
    {
        Write("Base1.cs", "namespace Shop;\n\npublic class Base1 { public int Id { get; set; } }\n");

        Assert.IsTrue(Run("create class Square inherits Base1"), LastOutput);

        Assert.DoesNotContain("CS0246", LastOutput, LastOutput);
        Assert.DoesNotContain("does not compile", LastOutput, LastOutput);
        StringAssert.Contains(File.ReadAllText(Path.Combine(_workspace, "Shop", "Square.cs")), "Base1");
        Assert.IsEmpty(ProjectErrors());
    }

    // It was reported and saved anyway, and "create class" said it had succeeded: the project then
    // failed dotnet build. Inside a project the class is checked with the project's own files, so
    // an error is the class's own, and it is not written.
    [TestMethod]
    public void A_class_that_does_not_compile_in_its_project_is_not_saved()
    {
        Assert.IsFalse(Run("create class Square inherits Nowhere1"), LastOutput);

        StringAssert.Contains(LastOutput, "CS0246");
        StringAssert.Contains(LastOutput, "Square was not created");
        Assert.DoesNotContain("Validate succeeded", LastOutput, LastOutput);
        Assert.IsFalse(File.Exists(Path.Combine(_workspace, "Shop", "Square.cs")), LastOutput);
        Assert.IsEmpty(ProjectErrors());
    }

    /// <summary>The project's files compiled with nullable on: its errors and its CS8618 warnings.</summary>
    private string[] NullableProblems() =>
    [
        .. Compiler.CompileSources(ProjectSources("#nullable enable\n"))
            .Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error || d.Id == "CS8618")
            .Select(d => d.ToString())
    ];

    private void WriteProjectTypes()
    {
        File.WriteAllText(Path.Combine(_workspace, "Shop", "Shop.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>");
        Write("Buyer.cs", "namespace Shop;\n\npublic enum Buyer { Retail }\n");
        Write("Shape.cs", "namespace Shop;\n\npublic class Shape { }\n");
    }

    private string Read(string name) => File.ReadAllText(Path.Combine(_workspace, "Shop", name));

    [TestMethod]
    public void Create_class_with_project_enum_and_class()
    {
        WriteProjectTypes();

        Assert.IsTrue(Run("create class Customer with properties Kind Buyer, Home Shape"), LastOutput);

        var code = Read("Customer.cs");
        StringAssert.Contains(code, "public Buyer Kind { get; set; }");
        Assert.DoesNotContain("Buyer Kind { get; set; } =", code);
        StringAssert.Contains(code, "public Shape Home { get; set; } = null!;");
        Assert.DoesNotContain("Ignored", LastOutput, LastOutput);
        var problems = NullableProblems();
        Assert.IsEmpty(problems, string.Join(Environment.NewLine, problems) + Environment.NewLine + code);
    }

    [TestMethod]
    public void Create_class_with_built_in_and_project_types_mixed()
    {
        WriteProjectTypes();

        Assert.IsTrue(Run("create class Customer with properties Name string, Kind Buyer, Home Shape, Data byte[]"), LastOutput);

        var code = Read("Customer.cs");
        StringAssert.Contains(code, "public Buyer Kind { get; set; }");
        StringAssert.Contains(code, "public Shape Home { get; set; } = null!;");
        StringAssert.Contains(code, "public byte[] Data { get; set; } = global::System.Array.Empty<byte>();");
        Assert.DoesNotContain("Ignored", LastOutput, LastOutput);
        Assert.IsEmpty(NullableProblems(), code);
    }

    [TestMethod]
    public void Create_class_with_untyped_name_and_project_enum()
    {
        WriteProjectTypes();

        // A typed entry makes the list a typed one, as "Name, Age int" always was: the untyped
        // name is reported, not given an inferred type.
        Assert.IsTrue(Run("create class Customer with properties Name, Kind Buyer"), LastOutput);

        var code = Read("Customer.cs");
        StringAssert.Contains(code, "public Buyer Kind { get; set; }");
        StringAssert.Contains(LastOutput, "Ignored 'Name': it has no type");
        Assert.IsEmpty(NullableProblems(), code);
    }

    [TestMethod]
    [DataRow("create class Customer", "Customer.cs")]
    [DataRow("create record Customer", "Customer.cs")]
    [DataRow("create static class Registry", "Registry.cs")]
    public void Wrapped_project_and_dotnet_types_compile_with_no_CS8618(string start, string file)
    {
        WriteProjectTypes();

        var properties = start.Contains("static")
            ? "Main Shape, Created DateTimeOffset"
            : "Homes List<Shape>, Old Shape[], Spare Shape?, Created DateTimeOffset, Map Dictionary<string, int>, "
                + "Link System.Uri, Maybe Shape?[], Kinds List<Buyer>, Main Shape";
        Assert.IsTrue(Run($"{start} with properties {properties}"), LastOutput);

        var code = Read(file);
        StringAssert.Contains(code, "Main");
        StringAssert.Contains(code, "Shape Main { get; ");
        StringAssert.Contains(code, "} = null!;");
        Assert.DoesNotContain("Ignored", LastOutput, LastOutput);
        var problems = NullableProblems();
        Assert.IsEmpty(problems, string.Join(Environment.NewLine, problems) + Environment.NewLine + code);
        if (!start.Contains("static"))
        {
            StringAssert.Contains(code, "Dictionary<string, int> Map");
            StringAssert.Contains(code, "System.Uri Link");
            StringAssert.Contains(code, "Maybe { get; ");
        }
    }

    [TestMethod]
    public void A_project_type_in_another_folder_of_the_project_is_accepted()
    {
        WriteProjectTypes();
        Directory.CreateDirectory(Path.Combine(_workspace, "Shop", "Models"));
        Write(Path.Combine("Models", "Tag.cs"), "namespace Shop.Models;\n\npublic class Tag { }\n");

        Assert.IsTrue(Run("create class Post with properties Title string, Label Shop.Models.Tag"), LastOutput);

        StringAssert.Contains(Read("Post.cs"), "Shop.Models.Tag Label");
        Assert.IsEmpty(NullableProblems(), Read("Post.cs"));
    }

    [TestMethod]
    public void A_modifier_read_as_the_name_asks_for_the_type()
    {
        WriteProjectTypes();

        Assert.IsFalse(Run("create class Customer with properties Title string, required Email"), LastOutput);

        StringAssert.Contains(LastOutput, "Did you leave out the type?");
        Assert.IsFalse(File.Exists(Path.Combine(_workspace, "Shop", "Customer.cs")), LastOutput);
    }

    [TestMethod]
    public void A_refusal_with_no_project_leaves_no_generated_folder()
    {
        ProjectManager.CurrentProject = null;
        Directory.Delete(Path.Combine(_workspace, "Shop"), recursive: true);

        Assert.IsFalse(Run("create class Customer with properties Kind Buyr"), LastOutput);

        StringAssert.Contains(LastOutput, "Type 'Buyr' of 'Kind' is not a built-in type");
        Assert.IsFalse(Directory.Exists(Path.Combine(_workspace, "Generated")), LastOutput);
    }

    [TestMethod]
    public void An_untyped_entry_beside_a_typed_one_is_reported_without_the_relationship_note()
    {
        Assert.IsTrue(Run("create class Customer with properties Name string, Notes"), LastOutput);

        StringAssert.Contains(LastOutput, "Ignored 'Notes': it has no type; give one after the name, as in Notes string.");
        Assert.DoesNotContain("Relationships between classes", LastOutput, LastOutput);
    }

    [TestMethod]
    public void Create_struct_with_project_class()
    {
        WriteProjectTypes();

        Assert.IsTrue(Run("create struct P with properties Owner Shape"), LastOutput);

        var code = Read("P.cs");
        StringAssert.Contains(code, "public Shape Owner { get; set; }");
        Assert.DoesNotContain("null!", code);
        Assert.IsEmpty(ProjectErrors(), code);
    }

    [TestMethod]
    public void Create_class_with_unknown_type_is_refused()
    {
        WriteProjectTypes();

        Assert.IsFalse(Run("create class Customer with properties Kind Buyr"), LastOutput);

        StringAssert.Contains(LastOutput, "Type 'Buyr' of 'Kind' is not a built-in type and the project does not declare it.");
        Assert.IsFalse(File.Exists(Path.Combine(_workspace, "Shop", "Customer.cs")), LastOutput);
    }

    [TestMethod]
    public void Create_class_with_unknown_type_beside_a_built_in_is_refused()
    {
        WriteProjectTypes();

        Assert.IsFalse(Run("create class Customer with properties Name string, Owner Shap"), LastOutput);

        StringAssert.Contains(LastOutput, "Type 'Shap' of 'Owner' is not a built-in type and the project does not declare it.");
        Assert.IsFalse(File.Exists(Path.Combine(_workspace, "Shop", "Customer.cs")), LastOutput);
    }

    [TestMethod]
    public void Create_interface_with_project_class()
    {
        WriteProjectTypes();

        Assert.IsTrue(Run("create interface INamed with properties Name string, Owner Shape"), LastOutput);

        var code = Read("INamed.cs");
        StringAssert.Contains(code, "Shape Owner { get; set; }");
        StringAssert.Contains(code, "string Name { get; set; }");
        Assert.DoesNotContain("Ignored", LastOutput, LastOutput);
        Assert.IsEmpty(ProjectErrors(), code);
    }

    [TestMethod]
    public void Create_interface_with_a_dotnet_type()
    {
        WriteProjectTypes();

        Assert.IsTrue(Run("create interface IStamped with properties Created DateTimeOffset"), LastOutput);

        StringAssert.Contains(Read("IStamped.cs"), "DateTimeOffset Created { get; set; }");
        Assert.IsEmpty(ProjectErrors(), Read("IStamped.cs"));
    }

    [TestMethod]
    public void Create_interface_with_unknown_type_is_refused()
    {
        WriteProjectTypes();

        Assert.IsFalse(Run("create interface INamed with properties Name string, Owner Shap"), LastOutput);

        StringAssert.Contains(LastOutput, "Type 'Shap' of 'Owner' is not a built-in type and the project does not declare it.");
        Assert.IsFalse(File.Exists(Path.Combine(_workspace, "Shop", "INamed.cs")), LastOutput);
    }

    [TestMethod]
    public void The_class_can_be_created_once_its_base_exists()
    {
        Assert.IsFalse(Run("create class Square inherits Shape1"), LastOutput);
        Assert.IsTrue(Run("create class Shape1"), LastOutput);

        Assert.IsTrue(Run("create class Square inherits Shape1"), LastOutput);

        Assert.IsTrue(File.Exists(Path.Combine(_workspace, "Shop", "Square.cs")), LastOutput);
        Assert.IsEmpty(ProjectErrors());
    }
}
