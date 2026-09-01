namespace AppScaffolding.Incremental;

/// <summary>
/// Whether one generated file belongs to an incremental add.
/// </summary>
/// <remarks>
/// Decided by comparing what a full generate WOULD produce against what is already on disk, rather
/// than by a list of file-name patterns. A list was tried first and generating a real application
/// disproved it twice.
/// <para>
/// It was wrong about which files a class owns: there is no <c>&lt;Name&gt;Screen.xaml</c> and no
/// <c>&lt;Name&gt;ViewModel.cs</c>, because the screens are generic and data-driven. Only
/// <c>Models/&lt;Name&gt;.cs</c>, <c>Data/&lt;Name&gt;Repository.cs</c> and the two validator
/// halves carry a class name.
/// </para>
/// <para>
/// And it named one composition file where there are three — <c>MainViewModel.cs</c>,
/// <c>SeedData.cs</c> and <c>ValidatorFactory.cs</c> all enumerate the entities. Copying only the
/// first would have left a new class with no seed data and an unregistered validator, both of
/// which compile perfectly and fail in silence.
/// </para>
/// <para>
/// Comparing content is target-independent, has nothing to maintain, and makes "an unchanged file
/// is byte-identical" true by construction: such a file is never written at all.
/// </para>
/// </remarks>
public static class IncrementalFiles
{
    /// <summary>The half of a validator the user owns. Written once and never again.</summary>
    private const string WriteOnceSuffix = ".Custom.cs";

    /// <summary>
    /// Whether <paramref name="relativePath"/> should be written.
    /// </summary>
    /// <param name="relativePath">The file, relative to the project folder.</param>
    /// <param name="stagedContent">What a full generate produces for it now.</param>
    /// <param name="existingContent">What is on disk, or null when nothing is.</param>
    public static bool ShouldCopy(
        string relativePath, string? stagedContent, string? existingContent)
    {
        // First, so nothing below can reach it. OverwritePrompt promises in as many words that
        // .Custom.cs rules are kept, and the staged copy is always an empty stub — so a plain
        // "differs" test would overwrite a user's rules every single time.
        if (existingContent is not null && IsWriteOnce(relativePath))
            return false;

        if (existingContent is null)
            return true;

        return !string.Equals(stagedContent, existingContent, StringComparison.Ordinal);
    }

    private static bool IsWriteOnce(string relativePath)
        => relativePath.EndsWith(WriteOnceSuffix, StringComparison.OrdinalIgnoreCase);
}
