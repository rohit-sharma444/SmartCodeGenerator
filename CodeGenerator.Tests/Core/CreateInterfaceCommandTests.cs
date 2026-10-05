using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Undo;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary><c>create interface</c> run as typed, through <see cref="CommandFront"/>, into a
/// workspace with no project: the interface lands in <c>Generated</c>.</summary>
[TestClass]
[DoNotParallelize]
public sealed class CreateInterfaceCommandTests
{
    private string _workspace = string.Empty;
    private string _shadowParent = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_createiface_" + Guid.NewGuid().ToString("N"));
        _shadowParent = Path.Combine(Path.GetTempPath(), "cg_createiface_sh_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        Directory.CreateDirectory(_shadowParent);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        foreach (var d in new[] { _workspace, _shadowParent })
            try { Directory.Delete(d, recursive: true); } catch (IOException) { }
    }

    private (bool Handled, string Output) Run(string input)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            ProjectMetaData? project = null;
            var handled = CommandFront.Run(input, ref project, [], _workspace, _ => { });
            return (handled, writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private string GeneratedDir => Path.Combine(_workspace, "Generated");

    private string[] FilesWritten() =>
        [.. Directory.GetFiles(GeneratedDir, "*.cs").Select(Path.GetFileName).Order()!];

    private static string[] AssertCompilesAndDeclaresInterfaces(string file)
    {
        var code = File.ReadAllText(file);
        var result = Compiler.CompileSources([(file, code)]);
        Assert.IsTrue(result.Success,
            "Interface file does not compile:" + Environment.NewLine
            + string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));

        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        return [.. root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Select(i => i.Identifier.Text)];
    }

    [TestMethod]
    [DataRow("create interface IShape", "IShape")]
    [DataRow("create interface ishape", "Ishape")]
    [DataRow("create interface shape", "IShape")]
    public void A_new_interface_is_written(string input, string expected)
    {
        var (handled, output) = Run(input);

        Assert.IsTrue(handled, output);
        CollectionAssert.AreEqual(new[] { expected + ".cs" }, FilesWritten());
        CollectionAssert.AreEqual(new[] { expected },
            AssertCompilesAndDeclaresInterfaces(Path.Combine(GeneratedDir, expected + ".cs")));
    }

    [TestMethod]
    // "create interface IShape" over an existing IShape.cs wrote over it unchecked, losing every
    // member a user had added. "Shape" names IShape too, and "ishape" names
    // Ishape.cs, which on Windows is IShape.cs.
    [DataRow("create interface IShape")]
    [DataRow("create interface Shape")]
    [DataRow("create interface ishape")]
    [DataRow("create interface shape")]
    public void An_existing_interface_is_not_replaced(string again)
    {
        Directory.CreateDirectory(GeneratedDir);
        var file = Path.Combine(GeneratedDir, "IShape.cs");
        const string code = "namespace GeneratedCode { public interface IShape { double Area(); } }";
        File.WriteAllText(file, code);

        var (handled, output) = Run(again);

        Assert.IsFalse(handled, output);
        Assert.AreEqual(code, File.ReadAllText(file), "the interface file was rewritten");
        CollectionAssert.AreEqual(new[] { "IShape.cs" }, FilesWritten());
        CollectionAssert.AreEqual(new[] { "IShape" }, AssertCompilesAndDeclaresInterfaces(file));
        StringAssert.Contains(output, file, output);
    }

    [TestMethod]
    // Any file of that name is somebody's code: a class file called IShape.cs is not replaced either.
    public void An_existing_class_file_of_that_name_is_not_replaced()
    {
        Directory.CreateDirectory(GeneratedDir);
        var file = Path.Combine(GeneratedDir, "IShape.cs");
        const string code = "namespace GeneratedCode { public class IShape { public int Sides { get; set; } } }";
        File.WriteAllText(file, code);

        var (handled, output) = Run("create interface IShape");

        Assert.IsFalse(handled, output);
        Assert.AreEqual(code, File.ReadAllText(file));
    }

    [TestMethod]
    public void A_refused_create_leaves_no_undo_entry()
    {
        Directory.CreateDirectory(GeneratedDir);
        File.WriteAllText(Path.Combine(GeneratedDir, "IShape.cs"),
            "namespace GeneratedCode { public interface IShape { double Area(); } }");
        using var journal = new UndoJournal(_workspace, shadowParent: _shadowParent);

        using (journal.Begin("create interface IShape"))
            Assert.IsFalse(Run("create interface IShape").Handled);

        Assert.IsFalse(journal.CanUndo, journal.NextUndoLabel);
    }
}
