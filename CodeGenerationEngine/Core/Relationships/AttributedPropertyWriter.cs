using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core.Relationships;

/// <summary>
/// Adds a property with attributes, and the usings those attributes need, to a file that already
/// exists.
/// </summary>
/// <remarks>
/// <see cref="ClassPropertyService.AddProperty"/> writes
/// <c>public T Name { get; set; }</c> and nothing else — no attributes, and no usings — so a
/// navigation property written through it would not compile once <c>[JsonIgnore]</c> was on it.
/// <c>AddProperty</c> is deliberately left alone: it is on the <c>add property</c> command's path
/// and nothing about that command is changing.
/// </remarks>
public static class AttributedPropertyWriter
{
    /// <summary>
    /// Writes the property and every using it needs, or reports why it could not.
    /// </summary>
    /// <returns>
    /// <c>true</c> only when the file on disk afterwards holds both the property and all of
    /// <paramref name="usings"/>. A property written without its usings is a file that does not
    /// compile, so reporting that as success would hand the caller a broken file and no reason —
    /// it is a failure with an <paramref name="error"/> instead. The half-written file is left
    /// where it is on failure: this writer does not own the backup, its caller does, and the
    /// relationship writer built on top of it restores the originals it captured before calling
    /// in.
    /// </returns>
    public static bool TryAdd(
        string filePath,
        string className,
        PropertyDefinition property,
        IEnumerable<string> usings,
        out string error)
    {
        var attributes = property.Attributes.Count == 0
            ? string.Empty
            : string.Concat(property.Attributes.Select(a => $"[{a}]{Environment.NewLine}"));

        var member = $"{attributes}public {property.Type} {property.Name} {{ get; set; }}";

        if (!ClassMemberEditor.TryAddMember(filePath, className, member, out error))
            return false;

        return EnsureUsings(filePath, usings, out error);
    }

    /// <summary>
    /// Adds any missing using directives to the top of the file, and reports whether the file
    /// afterwards actually holds them.
    /// </summary>
    /// <remarks>
    /// Written on the compilation unit rather than inside the namespace, so it works the same for a
    /// file-scoped namespace and a block-scoped one. A directive already present — at either
    /// level — is left alone rather than duplicated, which would be CS0105 under
    /// <c>-warnaserror</c>.
    /// <para>
    /// The final re-read is the point of the method returning anything at all. Every other step
    /// here can fail without throwing — a root that is not a compilation unit is skipped, and a
    /// using string that does not parse as a name yields a directive that is not the one asked
    /// for — and each of those leaves a file carrying an attributed property whose attribute has
    /// no using: a file that will not compile. Trusting the write and returning success is what
    /// makes that failure silent, so success is decided by reading the file back and seeing every
    /// wanted directive in it, not by having reached the end of the method.
    /// </para>
    /// </remarks>
    private static bool EnsureUsings(string filePath, IEnumerable<string> usings, out string error)
    {
        error = string.Empty;

        var wanted = usings
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (wanted.Length == 0)
            return true;

        try
        {
            if (Parse(filePath) is not { } unit)
            {
                error = $"{Path.GetFileName(filePath)} could not be read as a C# file, so the "
                      + $"using directives it needs ({string.Join(", ", wanted)}) were not added.";
                return false;
            }

            var declared = Declared(unit);

            var missing = wanted
                .Where(u => !declared.Contains(u))
                .Select(u => SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(u)).NormalizeWhitespace().ToFullString())
                .ToArray();

            // Put in as lines of text, after the last using there is (or at the top, after any
            // header comment), so nothing else in the file is reformatted: the whole file used to
            // go through NormalizeWhitespace.
            if (missing.Length > 0)
            {
                var source = unit.ToFullString();
                var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var lines = string.Concat(missing.Select(m => m + newline));

                string text;
                if (unit.Usings.Count > 0)
                {
                    var after = unit.Usings.Last().FullSpan.End;
                    text = source[..after] + lines + source[after..];
                }
                else
                {
                    var at = unit.Members.Count > 0 ? unit.Members[0].SpanStart
                        : unit.AttributeLists.Count > 0 ? unit.AttributeLists[0].SpanStart
                        : source.Length;
                    text = source[..at] + lines + newline + source[at..];
                }

                global::CodeGenerationEngine.Core.Validation.SourceFileText.Rewrite(filePath, text);
            }

            HashSet<string?> onDisk = Parse(filePath) is { } written ? Declared(written) : [];

            var absent = wanted.Where(u => !onDisk.Contains(u)).ToArray();

            if (absent.Length == 0)
                return true;

            error = $"{Path.GetFileName(filePath)} was written without the using "
                  + $"{(absent.Length == 1 ? "directive" : "directives")} it needs: "
                  + $"{string.Join(", ", absent)}.";
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"{Path.GetFileName(filePath)} could not be written: {exception.Message}";
            return false;
        }
    }

    private static CompilationUnitSyntax? Parse(string filePath) =>
        CSharpSyntaxTree.ParseText(global::CodeGenerationEngine.Core.Validation.SourceFileText.Load(filePath))
            .GetRoot() as CompilationUnitSyntax;

    private static HashSet<string?> Declared(CompilationUnitSyntax unit) =>
        unit.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(u => u.Name?.ToString())
            .Where(n => n is not null)
            .ToHashSet(StringComparer.Ordinal);
}
