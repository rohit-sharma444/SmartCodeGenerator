using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary><c>create class</c> run as typed, through <see cref="CommandFront"/>, into a workspace
/// with no project: the class lands in <c>Generated</c>.</summary>
[TestClass]
[DoNotParallelize]
public sealed class CreateClassCommandTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_createclass_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    private bool Run(string input)
    {
        ProjectMetaData? project = null;
        return CommandFront.Run(input, ref project, [], _workspace, _ => { });
    }

    private string GeneratedDir => Path.Combine(_workspace, "Generated");

    private string[] ClassFilesWritten() =>
        [.. Directory.GetFiles(GeneratedDir, "*.cs").Select(Path.GetFileName).Order()!];

    // The class names the file declares, read from the compiled tree.
    private static string[] AssertCompilesAndDeclares(string file, out string[] properties)
    {
        var code = File.ReadAllText(file);
        var result = Compiler.CompileSources([(file, code)]);
        Assert.IsTrue(result.Success,
            "Class file does not compile:" + Environment.NewLine
            + string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));

        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        properties = [.. root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Select(p => p.Identifier.Text)];
        return [.. root.DescendantNodes().OfType<ClassDeclarationSyntax>().Select(c => c.Identifier.Text)];
    }

    [TestMethod]
    // "create class employee" printed "Command not recognized".
    [DataRow("create class employee", "Employee")]
    [DataRow("create class manager", "Manager")]
    public void A_lower_case_name_writes_the_PascalCase_class(string input, string expected)
    {
        Assert.IsTrue(Run(input), input);

        CollectionAssert.AreEqual(new[] { expected + ".cs" }, ClassFilesWritten());
        var declared = AssertCompilesAndDeclares(Path.Combine(GeneratedDir, expected + ".cs"), out _);
        CollectionAssert.AreEqual(new[] { expected }, declared);
    }

    [TestMethod]
    public void A_lower_case_name_with_properties_writes_them()
    {
        Assert.IsTrue(Run("create class employee with properties Name string, Age int"));

        var declared = AssertCompilesAndDeclares(Path.Combine(GeneratedDir, "Employee.cs"), out var properties);
        CollectionAssert.AreEqual(new[] { "Employee" }, declared);
        CollectionAssert.AreEquivalent(new[] { "Name", "Age" }, properties);
    }
}
