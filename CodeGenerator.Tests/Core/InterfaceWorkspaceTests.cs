using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Undo;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// A workspace holding <c>IShape.cs</c>, as <c>create interface IShape</c> writes it, for the tests
/// of commands run on an interface. Commands run as typed, through <see cref="CommandFront"/>.
/// </summary>
public abstract class InterfaceWorkspaceTests
{
    protected string _workspace = string.Empty;
    protected string _shadowParent = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ValidationRegistry.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_ifacemember_" + Guid.NewGuid().ToString("N"));
        _shadowParent = Path.Combine(Path.GetTempPath(), "cg_ifacemember_sh_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        Directory.CreateDirectory(_shadowParent);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        ValidationRegistry.Clear();
        foreach (var d in new[] { _workspace, _shadowParent })
            try { Directory.Delete(d, recursive: true); } catch (IOException) { }
    }

    protected (bool Handled, string Output) Run(string input)
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

    protected string Write(string name, string code)
    {
        var file = Path.Combine(_workspace, name + ".cs");
        File.WriteAllText(file, code);
        return file;
    }

    protected string WriteShape(string members = "") =>
        Write("IShape", $"namespace Geo\n{{\n    public partial interface IShape\n    {{\n{members}    }}\n}}\n");

    protected string Shape => File.ReadAllText(Path.Combine(_workspace, "IShape.cs"));

    protected InterfaceDeclarationSyntax ParseShape() =>
        CSharpSyntaxTree.ParseText(Shape).GetRoot().DescendantNodes()
            .OfType<InterfaceDeclarationSyntax>().Single(i => i.Identifier.Text == "IShape");

    // Every .cs in the workspace as one assembly: an implementer out of step is a compile error.
    protected void AssertWorkspaceCompiles()
    {
        var sources = Directory.GetFiles(_workspace, "*.cs", SearchOption.AllDirectories)
            .Select(f => (f, File.ReadAllText(f))).ToList();
        var result = Compiler.CompileSources(sources);
        Assert.IsTrue(result.Success,
            "Workspace does not compile:" + Environment.NewLine
            + string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString()))
            + Environment.NewLine + string.Join(Environment.NewLine, sources.Select(s => s.Item2)));
    }
}
