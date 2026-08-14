using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// <see cref="CodeFormatter"/> — which did not format.
/// </summary>
/// <remarks>
/// It wrapped a class by string concatenation: <c>"namespace GeneratedCode" + "{" + code + "}"</c>.
/// The class body therefore stayed at column 0 inside its own namespace block, and the namespace was
/// hardcoded whatever project the file was written into.
/// <para>
/// <see cref="RoslynClassGenerator"/> has always done this correctly with
/// <c>NormalizeWhitespace()</c>; this path simply never used it.
/// </para>
/// </remarks>
[TestClass]
public class CodeFormatterTests
{
    private const string BareClass = "public class Order { public string Reference { get; set; } }";

    private static void AssertParses(string code)
    {
        var diagnostics = CSharpSyntaxTree.ParseText(code)
            .GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToList();

        Assert.IsEmpty(diagnostics, string.Join("; ", diagnostics.Select(d => d.GetMessage())));
    }

    [TestMethod]
    public void TheGivenNamespaceIsUsed()
    {
        var formatted = CodeFormatter.Format(BareClass, "CodisLtd.Shop");

        Assert.Contains("namespace CodisLtd.Shop", formatted);
        Assert.DoesNotContain("GeneratedCode", formatted);
    }

    [TestMethod]
    public void TheHistoricalNamespaceIsStillTheDefault()
    {
        // Kept so a class generated with no project selected behaves as it always did.
        Assert.Contains("namespace GeneratedCode", CodeFormatter.Format(BareClass));
    }

    [TestMethod]
    public void TheClassIsIndentedInsideTheNamespace()
    {
        // The actual defect. `public class Order` sat at column 0 inside the namespace block.
        var formatted = CodeFormatter.Format(BareClass, "Shop");

        var classLine = formatted
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .First(l => l.Contains("class Order"));

        Assert.AreNotEqual('p', classLine[0], $"not indented: '{classLine}'");
        Assert.IsGreaterThan(0, classLine.Length - classLine.TrimStart().Length,
            $"not indented: '{classLine}'");
    }

    [TestMethod]
    public void ThePropertyIsIndentedDeeperThanItsClass()
    {
        var lines = CodeFormatter.Format(BareClass, "Shop")
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .ToList();

        static int Indent(string line) => line.Length - line.TrimStart().Length;

        var classIndent = Indent(lines.First(l => l.Contains("class Order")));
        var propertyIndent = Indent(lines.First(l => l.Contains("Reference")));

        Assert.IsGreaterThan(classIndent, propertyIndent);
    }

    [TestMethod]
    public void TheResultIsValidCSharp()
    {
        // The strongest single check: whatever the layout, it has to parse.
        AssertParses(CodeFormatter.Format(BareClass, "Shop"));
        AssertParses(CodeFormatter.Format(BareClass));
    }

    [TestMethod]
    public void TheAnnotationsUsingIsAddedWhenAnAttributeNeedsIt()
    {
        var withAttribute =
            "public class Order { [Required] public string Reference { get; set; } }";

        var formatted = CodeFormatter.Format(withAttribute, "Shop");

        Assert.Contains("using System.ComponentModel.DataAnnotations;", formatted);
        AssertParses(formatted);
    }

    [TestMethod]
    public void CodeThatAlreadyDeclaresANamespaceKeepsIt()
    {
        // Left deliberately alone: this path was not broken, and re-wrapping or re-normalising it
        // would change output for callers that had already decided their own layout.
        var already = "namespace Existing.Thing" + Environment.NewLine + "{" + Environment.NewLine
                      + "    public class Order { }" + Environment.NewLine + "}";

        var formatted = CodeFormatter.Format(already, "Shop");

        Assert.Contains("namespace Existing.Thing", formatted);
        Assert.DoesNotContain("namespace Shop", formatted);
    }

    [TestMethod]
    public void UnparseableInputIsReturnedRatherThanLost()
    {
        // A generator bug must not also destroy the evidence — the user still needs to see what
        // was produced, even if it cannot be formatted.
        var broken = "public class Order { this is not C#";

        var formatted = CodeFormatter.Format(broken, "Shop");

        Assert.Contains("Order", formatted);
    }
}
