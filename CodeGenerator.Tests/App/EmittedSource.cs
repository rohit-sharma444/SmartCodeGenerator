using System.Linq;
using AppScaffolding;
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
    /// <summary>
    /// Flattens several emitted Validation/ or Data/ files (and any other C# snippets) into one
    /// compilable unit: strips each file's own namespace declaration — a compilation unit may
    /// declare only one file-scoped namespace, so joining a Data/ file to a Validation/ one needs
    /// both gone — and its &lt;App&gt;.Models/&lt;App&gt;.Data/&lt;App&gt;.Validation
    /// using (the callers' stubs stand in for those types, or the type is being merged in from
    /// its own source and lands in the same flattened, namespace-less file), then hoists every
    /// remaining <c>using</c> directive to the front of the result. The fixture app is always
    /// named <c>Acme</c>.
    /// </summary>
    /// <remarks>
    /// A <c>using</c> directive may not follow a type declaration anywhere in a single
    /// compilation unit (CS1529), and <c>Compiler.Compile</c> parses the whole concatenated
    /// string as one syntax tree. Simply concatenating several files that each open with their
    /// own <c>using</c> would therefore not compile once more than one type-bearing fragment is
    /// joined -- this hoisting belongs here, in the test harness, precisely so the production
    /// templates can keep plain <c>using</c> directives and bare type names, matching every
    /// sibling template, when they are emitted stand-alone into a real generated app.
    /// </remarks>
    public static string Flatten(params string[] sources)
    {
        var usings = new List<string>();
        var bodies = new List<string>();

        foreach (var source in sources)
        {
            var stripped = source
                .Replace("using Acme.Models;", string.Empty)
                .Replace("using Acme.Data;", string.Empty)
                .Replace("using Acme.Validation;", string.Empty)
                .Replace("namespace Acme.Validation;", string.Empty)
                .Replace("namespace Acme.Data;", string.Empty);

            foreach (var line in stripped.Split('\n'))
            {
                if (line.TrimStart().StartsWith("using ", StringComparison.Ordinal))
                    usings.Add(line.Trim());
                else
                    bodies.Add(line);
            }
        }

        return string.Join("\n", usings.Distinct()) + "\n" + string.Join("\n", bodies);
    }

    /// <summary>
    /// The permission matrix a screen's delete button asks, and the operation enum it names.
    /// </summary>
    /// <remarks>
    /// The real templates rather than a stub: a stub matrix could agree with a fixture while
    /// disagreeing with the app the generator emits, which is the whole failure the one matrix
    /// exists to prevent. <c>ActingUser.cs</c> rides along for the same reason
    /// <c>AppScaffolder</c> emits it beside <c>Permissions.cs</c> unconditionally: every screen
    /// and store template now calls <c>ActingUser.Role()</c> regardless of login, so any fixture
    /// built on this helper needs the type to exist.
    /// </remarks>
    /// <param name="sessionAuth">
    /// Defaults false (the null-returning half, naming no <c>Session</c> member) because most
    /// callers' own ad hoc session stub has no <c>.Current</c> at all — only a bare
    /// <c>Session.IsAdmin</c> flag for the admin-pane tests. Pass true only when the fixture's own
    /// stub genuinely shapes a <c>Current</c> user with <c>Role</c> and <c>Id</c> for
    /// <c>ActingUser</c> to read, the way <c>MasterScreenTests.SessionStub</c> does.
    /// </param>
    /// <param name="masterEntityNames">
    /// The entities this build classified as masters. The default matrix refuses a plain user a
    /// delete on those alone, so a fixture naming none can never see a refusal.
    /// </param>
    public static string PermissionMatrix(bool sessionAuth = false, params string[] masterEntityNames)
        => string.Join(
            Environment.NewLine,
            ValidationContextTemplate.Source("Acme"),
            ValidatorTemplate.EntityLookupInterface("Acme"),
            PermissionsTemplate.Source("Acme", masterEntityNames, login: false),
            // Flatten strips Acme.Models/.Data/.Validation but not Acme.Auth -- the sessionAuth
            // true half names it, and this joined string is handed to Flatten as ONE opaque
            // source, so the using has to go here rather than rely on a caller-side .Replace.
            ActingUserTemplate.Source("Acme", sessionAuth).Replace("using Acme.Auth;", string.Empty));

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
