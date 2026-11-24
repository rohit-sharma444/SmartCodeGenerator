using System.Linq;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// <c>add property</c> / <c>add method</c> rewrite a class file in place. Both used
/// to splice the new member in at the file's last <c>}</c>, which is the *namespace*
/// brace when the class sits in a block-scoped namespace — so the member landed
/// outside the class body and the file no longer compiled. These tests assert on the
/// parsed syntax tree (is the member a child of the class?) and on a real Roslyn
/// compile, rather than on substring presence, which the broken output also satisfied.
/// </summary>
[TestClass]
public class ClassMemberInsertionTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "member-insert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string WriteClass(string className, string body = "")
    {
        var path = Path.Combine(_root, className + ".cs");
        File.WriteAllText(path,
            $"namespace GeneratedCode\n{{\npublic class {className}\n{{\n{body}}}\n\n}}\n");
        return path;
    }

    private static ClassDeclarationSyntax ParseClass(string path, string className)
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
        var node = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .SingleOrDefault(c => c.Identifier.Text == className);
        Assert.IsNotNull(node, $"No class '{className}' in:\n{File.ReadAllText(path)}");
        return node!;
    }

    private static void AssertCompiles(string path)
    {
        var source = File.ReadAllText(path);
        var result = Compiler.Compile(source);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.IsEmpty(errors,
            $"Generated file does not compile:\n{string.Join("\n", errors)}\n\nSource:\n{source}");
    }

    [TestMethod]
    public void Added_property_lands_inside_the_class_body()
    {
        var path = WriteClass("Gadget");

        var ok = ClassPropertyService.AddProperty(_root, "Gadget", "Size", "int", out var message);

        Assert.IsTrue(ok, message);
        var members = ParseClass(path, "Gadget").Members.OfType<PropertyDeclarationSyntax>().ToList();
        Assert.HasCount(1, members);
        Assert.AreEqual("Size", members[0].Identifier.Text);
        AssertCompiles(path);
    }

    [TestMethod]
    public void Added_method_lands_inside_the_class_body()
    {
        var path = WriteClass("Gadget");

        var ok = ClassMethodService.AddMethod(_root, "Gadget", "Save", "void", [], false, out var message);

        Assert.IsTrue(ok, message);
        var members = ParseClass(path, "Gadget").Members.OfType<MethodDeclarationSyntax>().ToList();
        Assert.HasCount(1, members);
        Assert.AreEqual("Save", members[0].Identifier.Text);
        AssertCompiles(path);
    }

    /// <summary>
    /// The sequence that produced the malformed file in practice: several members
    /// added one after another, each rewriting what the previous one wrote.
    /// </summary>
    [TestMethod]
    public void Repeated_additions_keep_every_member_inside_the_class()
    {
        var path = WriteClass("Gadget");

        Assert.IsTrue(ClassPropertyService.AddProperty(_root, "Gadget", "Name", "string", out var m1), m1);
        Assert.IsTrue(ClassMethodService.AddMethod(_root, "Gadget", "Save", "void", [], false, out var m2), m2);
        Assert.IsTrue(ClassPropertyService.AddProperty(_root, "Gadget", "Age", "int", out var m3), m3);

        var cls = ParseClass(path, "Gadget");
        Assert.HasCount(2, cls.Members.OfType<PropertyDeclarationSyntax>().ToList());
        Assert.HasCount(1, cls.Members.OfType<MethodDeclarationSyntax>().ToList());
        AssertCompiles(path);
    }

    /// <summary>
    /// A file holding more than one type must have the member added to the named
    /// class, not to whichever one happens to come first or last.
    /// </summary>
    [TestMethod]
    public void Member_is_added_to_the_named_class_when_the_file_holds_several()
    {
        var path = Path.Combine(_root, "Gadget.cs");
        File.WriteAllText(path,
            "namespace GeneratedCode\n{\npublic class Other\n{\n}\n\npublic class Gadget\n{\n}\n}\n");

        var ok = ClassPropertyService.AddProperty(_root, "Gadget", "Size", "int", out var message);

        Assert.IsTrue(ok, message);
        Assert.HasCount(1, ParseClass(path, "Gadget").Members.OfType<PropertyDeclarationSyntax>().ToList());
        Assert.IsEmpty(ParseClass(path, "Other").Members.OfType<PropertyDeclarationSyntax>().ToList());
        AssertCompiles(path);
    }
}
