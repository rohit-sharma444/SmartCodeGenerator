using System.Linq;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>Compiles emitted C# and fails with the compiler's own diagnostics.</summary>
/// <remarks>
/// Substring assertions cannot see the defects that matter in emitted code — Roslyn's
/// error-recovery makes a malformed member pass a syntax-tree assertion, and an interpolated
/// literal that breaks a string is invisible until the compiler reads it.
/// </remarks>
internal static class EmittedSource
{
    /// <summary>Asserts <paramref name="source"/> compiles with no errors.</summary>
    public static void AssertCompiles(string source)
    {
        var result = Compiler.Compile(source);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var message = string.Join("\n", errors.Select(e => e.ToString()));
        Assert.IsTrue(result.Success, $"Compilation failed:\n{message}");
        Assert.IsEmpty(errors, $"Compilation errors:\n{message}");
    }
}
