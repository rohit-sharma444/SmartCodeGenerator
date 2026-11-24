using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// A default value is checked against its type by compiling it, and the property modifiers
/// follow the C# rules.
/// </summary>
[TestClass]
public sealed class DefaultValuesTests
{
    private static readonly MetadataReference[] References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p))];

    private static bool Compiles(string type, string expression)
    {
        var tree = CSharpSyntaxTree.ParseText(
            $"using System; class C {{ {type} x = {expression}; }}");
        var compilation = CSharpCompilation.Create(
            "T", [tree], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return !compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    [TestMethod]
    [DataRow("int", "1", "1")]
    [DataRow("string", "New", "\"New\"")]
    [DataRow("string", "\"New\"", "\"New\"")]
    [DataRow("bool", "true", "true")]
    [DataRow("string", "a\"b", "\"a\\\"b\"")]
    [DataRow("string", "C:\\temp", "\"C:\\\\temp\"")]
    [DataRow("string", "null", "null")]
    [DataRow("string?", "null", "null")]
    [DataRow("String", "null", "null")]
    [DataRow("System.String", "x", "\"x\"")]
    [DataRow("String?", "x", "\"x\"")]
    [DataRow("string?", "x", "\"x\"")]
    public void TryExpression_AcceptsValidValues(string type, string raw, string expected)
    {
        Assert.IsTrue(DefaultValues.TryExpression(type, raw, null, out var expression, out var error), error);
        Assert.IsNull(error);
        Assert.AreEqual(expected, expression);
        Assert.IsTrue(Compiles(type, expression), expression);
    }

    [TestMethod]
    [DataRow("decimal", "9.5", "9.5m")]
    [DataRow("float", "1.5", "1.5f")]
    [DataRow("double", "2.5", "2.5")]
    public void TryExpression_NumbersCompileAndKeepTheValue(string type, string raw, string value)
    {
        Assert.IsTrue(DefaultValues.TryExpression(type, raw, null, out var expression, out var error), error);
        Assert.AreEqual(value, expression);
        Assert.IsTrue(Compiles(type, expression), expression);
    }

    [TestMethod]
    [DataRow("int", "abc")]
    [DataRow("bool", "maybe")]
    [DataRow("decimal", "x1")]
    [DataRow("int", "1; } class Y { int z = 2")]
    [DataRow("int", "1; int y")]
    [DataRow("int", "1, y = 2")]
    public void TryExpression_RefusesAValueThatDoesNotFit(string type, string raw)
    {
        Assert.IsFalse(DefaultValues.TryExpression(type, raw, null, out _, out var error));
        Assert.AreEqual($"'{raw}' is not a valid {type} value.", error);
    }

    [TestMethod]
    public void TryExpression_UsesTheProjectsOwnTypes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cg_dv_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "Shop.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(folder, "Status.cs"), "namespace Shop;\npublic enum Status { Active, Closed }\n");

            Assert.IsFalse(DefaultValues.TryExpression("Status", "Status.Active", null, out _, out _));
            Assert.IsTrue(DefaultValues.TryExpression("Status", "Status.Active", folder, out var expression, out var error), error);
            Assert.AreEqual("Status.Active", expression);
            Assert.IsFalse(DefaultValues.TryExpression("Status", "Status.Gone", folder, out _, out error));
            Assert.AreEqual("'Status.Gone' is not a valid Status value.", error);
            // Implicit usings are on in this project.
            Assert.IsTrue(DefaultValues.TryExpression("List<int>", "new List<int>()", folder, out _, out error), error);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public void TryExpression_SkipsBuildOutputAndWorksInAFolderWithNoProjectFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cg_dv_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, "obj"));
        Directory.CreateDirectory(Path.Combine(folder, "bin"));
        try
        {
            File.WriteAllText(Path.Combine(folder, "Status.cs"), "namespace Shop;\npublic enum Status { Active }\n");
            // Stale copies in build output would make Status ambiguous (CS0101) if they were read.
            File.WriteAllText(Path.Combine(folder, "obj", "Status.cs"), "namespace Shop;\npublic enum Status { Gone }\n");
            File.WriteAllText(Path.Combine(folder, "bin", "Status.cs"), "namespace Shop;\npublic enum Status { Gone }\n");

            Assert.IsTrue(DefaultValues.TryExpression("Status", "Status.Active", folder, out _, out var error), error);
            Assert.IsFalse(DefaultValues.TryExpression("Status", "Status.Gone", folder, out _, out _));
            Assert.IsTrue(DefaultValues.TryExpression("int", "1", folder, out _, out error), error);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public void Refusal_RequiredWithStaticOrPrivate()
    {
        Assert.AreEqual("'Id' cannot be both required and static.",
            PropertyModifierRules.Refusal(PropertyModifiers.Required | PropertyModifiers.Static, "Id"));
        Assert.AreEqual("'Id' cannot be both required and private.",
            PropertyModifierRules.Refusal(PropertyModifiers.Required | PropertyModifiers.Private, "Id"));
    }

    [TestMethod]
    [DataRow(PropertyModifiers.None)]
    [DataRow(PropertyModifiers.ReadOnly)]
    [DataRow(PropertyModifiers.ReadOnly | PropertyModifiers.Required)]
    [DataRow(PropertyModifiers.Private | PropertyModifiers.Static)]
    [DataRow(PropertyModifiers.ReadOnly | PropertyModifiers.Private | PropertyModifiers.Static)]
    public void Refusal_AllowsTheRest(PropertyModifiers modifiers)
        => Assert.IsNull(PropertyModifierRules.Refusal(modifiers, "Id"));

    [TestMethod]
    [DataRow(PropertyModifiers.None, "{ get; set; }")]
    [DataRow(PropertyModifiers.Private, "{ get; set; }")]
    [DataRow(PropertyModifiers.Required, "{ get; set; }")]
    [DataRow(PropertyModifiers.Static, "{ get; set; }")]
    [DataRow(PropertyModifiers.ReadOnly, "{ get; init; }")]
    [DataRow(PropertyModifiers.ReadOnly | PropertyModifiers.Required, "{ get; init; }")]
    [DataRow(PropertyModifiers.ReadOnly | PropertyModifiers.Private, "{ get; init; }")]
    [DataRow(PropertyModifiers.ReadOnly | PropertyModifiers.Static, "{ get; }")]
    public void Accessors_FollowReadOnlyAndStatic(PropertyModifiers modifiers, string expected)
        => Assert.AreEqual(expected, PropertyModifierRules.Accessors(modifiers));

    [TestMethod]
    [DataRow(PropertyModifiers.None, "public ")]
    [DataRow(PropertyModifiers.ReadOnly, "public ")]
    [DataRow(PropertyModifiers.Private, "private ")]
    [DataRow(PropertyModifiers.Static, "public static ")]
    [DataRow(PropertyModifiers.Required, "public required ")]
    [DataRow(PropertyModifiers.Private | PropertyModifiers.Static, "private static ")]
    [DataRow(PropertyModifiers.ReadOnly | PropertyModifiers.Required, "public required ")]
    public void Prefix_IsInCSharpOrder(PropertyModifiers modifiers, string expected)
        => Assert.AreEqual(expected, PropertyModifierRules.Prefix(modifiers));

    [TestMethod]
    public void Modifiers_DefaultToNone()
        => Assert.AreEqual(PropertyModifiers.None, new PropertyDefinition().Modifiers);
}
