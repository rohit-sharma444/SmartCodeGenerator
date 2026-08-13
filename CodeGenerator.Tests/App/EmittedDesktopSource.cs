using System.Linq;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Compiles emitted WPF / WinForms C# — the code <see cref="EmittedSource"/> cannot reach.
/// </summary>
/// <remarks>
/// This test project targets net10.0, not net10.0-windows, so PresentationFramework, WindowsBase
/// and System.Windows.Forms are not among the running process's TRUSTED_PLATFORM_ASSEMBLIES and
/// <see cref="Compiler.Compile(string, IEnumerable{string})"/> cannot resolve a Window, a
/// ContentControl or a Form. The WindowsDesktop REFERENCE pack shipped with the SDK supplies
/// exactly those assemblies, and mixing them with the runtime's own platform assemblies produces
/// no duplicate-assembly conflict — verified by probe, 0 errors and 0 warnings.
/// <para>
/// Retargeting the test project to net10.0-windows would be the alternative. It was rejected: it
/// would make the whole suite Windows-only for the sake of a handful of tests, and the emitted
/// desktop code is only ever compiled for real by the generated app's own build.
/// </para>
/// </remarks>
internal static class EmittedDesktopSource
{
    private const string PackRoot =
        @"C:\Program Files\dotnet\packs\Microsoft.WindowsDesktop.App.Ref";

    /// <summary>
    /// Every reference assembly in the newest installed WindowsDesktop ref pack, or empty when the
    /// pack is missing.
    /// </summary>
    /// <remarks>
    /// Empty rather than throwing, so the dedicated guard test in
    /// <c>EmittedDesktopSourceTests</c> reports the cause once instead of every desktop test
    /// failing with the same path error.
    /// </remarks>
    public static IReadOnlyList<string> DesktopReferences { get; } = LoadDesktopReferences();

    public static void AssertCompiles(string source)
    {
        GuardReferences();

        AssertNoErrors(Compiler.Compile(source, DesktopReferences));
    }

    /// <summary>
    /// Asserts several emitted files compile as ONE assembly, each keeping its own file-scoped
    /// namespace and <c>using</c> directives.
    /// </summary>
    /// <remarks>
    /// <see cref="EmittedSource.Flatten"/> is the wrong tool for emitted views: it strips only the
    /// <c>Acme.Models</c> / <c>Acme.Data</c> / <c>Acme.Validation</c> names its own callers emit, so
    /// a <c>Views</c> and a <c>ViewModels</c> fragment concatenated by it declare two file-scoped
    /// namespaces in one compilation unit. Compiling them as separate syntax trees also keeps the
    /// test faithful: the emitted code-behind really does live in another namespace than the view
    /// models it imports, and a missing <c>using</c> would be invisible if both were flattened into
    /// the global namespace.
    /// </remarks>
    public static void AssertCompilesTogether(params (string Path, string Code)[] files)
    {
        GuardReferences();

        Assert.IsNotEmpty(files, "nothing was passed to compile");

        AssertNoErrors(Compiler.CompileSources(files, DesktopReferences));
    }

    private static void GuardReferences()
        => Assert.IsNotEmpty(
            DesktopReferences,
            "the Microsoft.WindowsDesktop.App.Ref pack was not found, so this assertion would be "
            + "vacuous; see EmittedDesktopSourceTests");

    private static void AssertNoErrors(CodeGenerationEngine.Models.CompilationResult result)
    {
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var message = string.Join("\n", errors.Select(e => e.ToString()));

        Assert.IsTrue(result.Success, $"Compilation failed:\n{message}");
        Assert.IsEmpty(errors, $"Compilation errors:\n{message}");
    }

    private static IReadOnlyList<string> LoadDesktopReferences()
    {
        try
        {
            if (!Directory.Exists(PackRoot))
                return [];

            // Ordered by the PARSED version, not by folder-name text: sorting the names as
            // strings would put "9.0.19" above "10.0.11" and pick a stale pack.
            var newest = Directory.GetDirectories(PackRoot)
                .Select(d => (Dir: d, Parsed: Version.TryParse(Path.GetFileName(d), out var v) ? v : null))
                .Where(x => x.Parsed is not null)
                .OrderByDescending(x => x.Parsed)
                .Select(x => Path.Combine(x.Dir, "ref", "net10.0"))
                .FirstOrDefault(Directory.Exists);

            return newest is null ? [] : Directory.GetFiles(newest, "*.dll");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
