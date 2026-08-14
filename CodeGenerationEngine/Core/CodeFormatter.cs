using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Wraps generated code in a namespace and lays it out properly.
/// </summary>
/// <remarks>
/// This used to do the wrapping by string concatenation — <c>"namespace GeneratedCode" + "{" + code
/// + "}"</c> — which left the class body at column 0 inside its own namespace block, and hardcoded
/// the namespace whatever project the file was written into. Despite the name, it did not format.
/// <para>
/// It now goes through Roslyn, which is what <see cref="RoslynClassGenerator"/> has always done:
/// build the namespace around the parsed members and call <c>NormalizeWhitespace()</c>. Indentation
/// then comes from the compiler's own idea of layout rather than from string handling.
/// </para>
/// </remarks>
public static class CodeFormatter
{
    private const string AnnotationsUsing = "using System.ComponentModel.DataAnnotations;";

    /// <summary>Attributes whose presence requires the DataAnnotations using.</summary>
    private static readonly string[] AnnotationAttributes =
        ["[Required]", "[StringLength(", "[MinLength(", "[Range("];

    /// <param name="namespaceName">
    /// The namespace to declare. Defaults to <see cref="ProjectNamespace.Fallback"/> so a caller
    /// with no project behaves as it always did; <c>GeneratedCodeService</c> passes the project's own.
    /// </param>
    public static string Format(string code, string? namespaceName = null)
    {
        var needsAnnotations = AnnotationAttributes.Any(code.Contains);

        if (code.Contains("namespace "))
        {
            // Already namespaced, and this path was not broken — re-wrapping or re-normalising it
            // would change output for a caller that had already settled its own layout.
            return needsAnnotations && !code.Contains(AnnotationsUsing)
                ? AnnotationsUsing + Environment.NewLine + Environment.NewLine + code
                : code;
        }

        var target = string.IsNullOrWhiteSpace(namespaceName)
            ? ProjectNamespace.Fallback
            : namespaceName;

        return Wrap(code, target, needsAnnotations) ?? Concatenate(code, target, needsAnnotations);
    }

    /// <summary>
    /// The code inside <paramref name="namespaceName"/>, laid out by Roslyn — or null when the code
    /// could not be parsed.
    /// </summary>
    private static string? Wrap(string code, string namespaceName, bool needsAnnotations)
    {
        var members = CSharpSyntaxTree.ParseText(code).GetRoot() as CompilationUnitSyntax;

        if (members is null || members.Members.Count == 0)
            return null;

        // A parse error means the generator produced something invalid. Wrapping it anyway would
        // bury the evidence in a namespace; the caller falls back to plain concatenation so the
        // user still sees exactly what was generated.
        if (members.GetDiagnostics().Any(d =>
                d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
            return null;

        var unit = CompilationUnit()
            .AddMembers(NamespaceDeclaration(ParseName(namespaceName))
                .AddMembers([.. members.Members]));

        if (needsAnnotations)
        {
            unit = unit.AddUsings(
                UsingDirective(ParseName("System.ComponentModel.DataAnnotations")));
        }

        return unit.NormalizeWhitespace().ToFullString();
    }

    /// <summary>
    /// The old string-concatenating behaviour, kept only for code Roslyn cannot parse.
    /// </summary>
    /// <remarks>
    /// Badly laid out, but it preserves the generated text verbatim — which is what someone
    /// diagnosing a generator bug needs.
    /// </remarks>
    private static string Concatenate(string code, string namespaceName, bool needsAnnotations)
    {
        var header = needsAnnotations
            ? AnnotationsUsing + Environment.NewLine + Environment.NewLine
            : string.Empty;

        return header + "namespace " + namespaceName +
               Environment.NewLine + "{" + Environment.NewLine +
               code + Environment.NewLine + "}";
    }
}
