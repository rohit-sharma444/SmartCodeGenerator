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

    /// <summary>The file a class lives in, or null. Matched by filename, which is the convention
    /// the whole tool works on: a class is a <c>.cs</c> file with its name.</summary>
    public static string? FileOf(string searchRoot, string className) =>
        Under(searchRoot).FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
            .Equals(className, StringComparison.OrdinalIgnoreCase));
}
