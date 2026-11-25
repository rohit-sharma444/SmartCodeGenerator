using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerator.Tests.Core;

/// <summary><see cref="MemberStubs.Missing"/> names the members a type lacks, as one-line stubs.</summary>
[TestClass]
public sealed class MemberStubsTests
{
    // References is internal to the engine, so the runtime's own assemblies are used.
    private static readonly MetadataReference[] References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p))];

    private static IReadOnlyList<MemberStub> MissingFor(string typeName, params string[] sources)
    {
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest))).ToList();
        var compilation = CSharpCompilation.Create("Probe", trees, References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            var decl = tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                .FirstOrDefault(d => d.Identifier.Text == typeName);
            if (decl is not null && model.GetDeclaredSymbol(decl) is INamedTypeSymbol type)
                return MemberStubs.Missing(type, model, decl.SpanStart);
        }

        throw new InvalidOperationException("type not found");
    }

    [TestMethod]
    public void Struct_implementing_an_interface_gets_a_method_stub()
    {
        var stubs = MissingFor("Sq", "interface IShape { double Area(); } struct Sq : IShape { }");

        Assert.HasCount(1, stubs);
        Assert.AreEqual("public double Area() => throw new System.NotImplementedException();", stubs[0].Code);
        Assert.AreEqual("Area", stubs[0].MemberName);
        Assert.AreEqual("IShape", stubs[0].From);
    }

    [TestMethod]
    public void Member_in_another_partial_part_is_not_stubbed()
    {
        var stubs = MissingFor("A",
            "interface I { int Id { get; set; } void Save(); } partial class A : I { }",
            "partial class A { public void Save() { } }");

        Assert.HasCount(1, stubs);
        Assert.AreEqual("Id", stubs[0].MemberName);
    }

    [TestMethod]
    public void Generic_interface_stub_uses_the_type_argument()
    {
        var stubs = MissingFor("O", "class O : System.IComparable<O> { }");

        Assert.HasCount(1, stubs);
        Assert.AreEqual("public int CompareTo(O? other) => throw new System.NotImplementedException();", stubs[0].Code);
    }
}
