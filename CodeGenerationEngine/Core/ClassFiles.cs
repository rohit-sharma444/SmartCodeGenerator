namespace CodeGenerationEngine.Core;

/// <summary>
/// The <c>.cs</c> files under a workspace that count as its classes.
/// </summary>
/// <remarks>
/// One definition, because there were three. Build output is skipped, or a stale copy under
/// <c>obj</c> wins the race against the source and the reader is shown yesterday's members;
/// <c>DeletedClasses</c> is skipped because <c>ProjectManager.ClassNamesIn</c> skips it too, so a
/// name that lives only in there is never asked about — while a live class with a backup of an
/// older namesake beside it was resolving to the backup, that folder sorting before nearly every
/// folder a project has.
/// </remarks>
public static class ClassFiles
{
    public static IReadOnlyList<string> Under(string searchRoot)
    {
        if (string.IsNullOrWhiteSpace(searchRoot) || !Directory.Exists(searchRoot))
            return [];

        return
        [
            .. Directory
                .GetFiles(searchRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f =>
                    !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    && !f.Contains("DeletedClasses", StringComparison.OrdinalIgnoreCase))
        ];
    }

    /// <summary>
    /// The file under <paramref name="searchRoot"/> that declares a type called
    /// <paramref name="typeName"/> - a class, record, struct, interface or enum, not nested in
    /// another type - or null.
    /// </summary>
    /// <remarks>
    /// By declaration, not by file name: <c>create class OrderLine</c> made a second OrderLine
    /// beside the record declared in Order.cs, and <c>create class IAuditable</c> a second
    /// IAuditable beside the interface in EntityBase.cs.
    /// </remarks>
    public static string? DeclaringFile(string searchRoot, string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        foreach (var file in Under(searchRoot))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!text.Contains(typeName, StringComparison.Ordinal))
                continue;

            var declares = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(text).GetRoot()
                .DescendantNodes(n => n is not Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax)
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax>()
                .Any(t => t.Identifier.Text.Equals(typeName, StringComparison.Ordinal));

            if (declares)
                return file;
        }

        return null;
    }

    /// <summary>The file a class lives in, or null. Matched by filename, which is the convention
    /// the whole tool works on: a class is a <c>.cs</c> file with its name.</summary>
    public static string? FileOf(string searchRoot, string className) =>
        Under(searchRoot).FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
            .Equals(className, StringComparison.OrdinalIgnoreCase));
}
