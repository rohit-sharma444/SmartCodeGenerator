using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// The initial value <c>add property</c> writes compiles in the project it is written into - an
/// older one too - and is written only where nullable reference types are on at the class.
/// </summary>
[TestClass]
public class PropertyInitializerTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(Path.GetTempPath(), "pi-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // B6: `= [];` is C# 12 and a net6 project is C# 10; `new Dictionary<K,V>()` needs a using the
    // file may not have.
    [TestMethod]
    public void Initializers_compile_in_a_csharp_10_project_without_implicit_usings()
    {
        Project("net6.0", nullable: "enable", implicitUsings: "disable");
        File.WriteAllText(Path.Combine(_root, "Note.cs"), "namespace Shop\r\n{\r\n    public class Note\r\n    {\r\n        public int Id { get; set; }\r\n    }\r\n}\r\n");

        Add("Tags", "string[]");
        Add("Counts", "System.Collections.Generic.IDictionary<string, int>");
        Add("Names", "System.Collections.Generic.IList<string>");
        Add("Seen", "System.Collections.Generic.IEnumerable<string>");
        Add("Grid", "int[,]");
        Add("Lookup", "System.Collections.Generic.IReadOnlyDictionary<string, int>");

        var text = File.ReadAllText(Path.Combine(_root, "Note.cs"));
        var errors = CompileAt(LanguageVersion.CSharp10, text);

        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors) + Environment.NewLine + text);
        Assert.DoesNotContain("CS8618", string.Join(" ", Warnings(LanguageVersion.CSharp10, text)), text);
    }

    [TestMethod]
    public void A_concrete_collection_gets_an_initializer_that_compiles_before_csharp_9()
    {
        Project("net6.0", nullable: "enable", implicitUsings: "disable");
        File.WriteAllText(Path.Combine(_root, "Note.cs"), "using System.Collections.Generic;\r\n\r\nnamespace Shop\r\n{\r\n    public class Note\r\n    {\r\n        public int Id { get; set; }\r\n    }\r\n}\r\n");

        Add("Lines", "List<string>");

        var text = File.ReadAllText(Path.Combine(_root, "Note.cs"));

        // C# 8 is the first with nullable reference types; target-typed new() is C# 9.
        var errors = CompileAt(LanguageVersion.CSharp8, text);
        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors) + Environment.NewLine + text);
    }

    // B7: the directive in effect at the class decides, not the last one in the file.
    [TestMethod]
    [DataRow("#nullable enable\r\n", "\r\n#nullable disable\r\n", "", true, DisplayName = "enable before, disable at the end")]
    [DataRow("#nullable restore\r\n", "", "enable", true, DisplayName = "restore in a project that enables")]
    [DataRow("#nullable disable\r\n", "\r\n#nullable enable\r\n", "enable", false, DisplayName = "disable before, enable after")]
    [DataRow("", "", "enable", true, DisplayName = "the project alone")]
    [DataRow("", "", "", false, DisplayName = "nothing")]
    public void Nullable_is_read_where_the_class_is(string before, string after, string projectNullable, bool expected)
    {
        Project("net8.0", nullable: projectNullable, implicitUsings: "enable");
        File.WriteAllText(Path.Combine(_root, "Note.cs"),
            before + "namespace Shop\r\n{\r\n    public class Note\r\n    {\r\n        public int Id { get; set; }\r\n    }\r\n}\r\n" + after);

        Add("Title", "string");

        var text = File.ReadAllText(Path.Combine(_root, "Note.cs"));
        Assert.AreEqual(expected, text.Contains("public string Title { get; set; } = string.Empty;", StringComparison.Ordinal), text);
    }

    private void Add(string name, string type)
    {
        Assert.IsTrue(ClassPropertyService.AddProperty(_root, "Note", name, type, out var message), message);
    }

    private void Project(string framework, string nullable, string implicitUsings)
    {
        var nullableLine = nullable.Length == 0 ? string.Empty : $"    <Nullable>{nullable}</Nullable>\r\n";
        File.WriteAllText(Path.Combine(_root, "Shop.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n  <PropertyGroup>\r\n"
            + $"    <TargetFramework>{framework}</TargetFramework>\r\n"
            + nullableLine
            + $"    <ImplicitUsings>{implicitUsings}</ImplicitUsings>\r\n"
            + "  </PropertyGroup>\r\n</Project>\r\n");
    }

    private static List<string> CompileAt(LanguageVersion version, string code) =>
        [.. Compile(version, code).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())];

    private static List<string> Warnings(LanguageVersion version, string code) =>
        [.. Compile(version, code).Where(d => d.Severity == DiagnosticSeverity.Warning).Select(d => d.ToString())];

    private static IEnumerable<Diagnostic> Compile(LanguageVersion version, string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(version), path: "Note.cs");

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p) is "System.Runtime.dll" or "System.Private.CoreLib.dll"
                or "System.Collections.dll" or "netstandard.dll")
            .Select(p => MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            "Probe", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return compilation.GetDiagnostics();
    }
}
