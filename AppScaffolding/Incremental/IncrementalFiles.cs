namespace AppScaffolding.Incremental;

/// <summary>What an add does with one generated file.</summary>
public enum FileAction
{
    /// <summary>Nothing. The add does not change this file, or must not.</summary>
    Leave,

    /// <summary>Write it: it is new with this add and nothing is in its place.</summary>
    Create,

    /// <summary>Write it over the copy on disk, which is exactly what the generator wrote.</summary>
    Replace,

    /// <summary>Put only the add's lines into the copy on disk, which has been edited.</summary>
    Merge,

    /// <summary>Leave the copy on disk, which is not the generator's, and write the add's version
    /// beside it.</summary>
    SetAside
}

/// <summary>
/// What an incremental add does with one generated file.
/// </summary>
/// <remarks>
/// Decided by comparing what the generator writes for the app as it is (<c>before</c>), what it
/// writes with the new classes (<c>after</c>), and what is on disk, rather than by a list of
/// file-name patterns. A list was tried first and generating a real application disproved it
/// twice: there is no <c>&lt;Name&gt;Screen.xaml</c>, and the files that enumerate the entities
/// are three or more per target, not one.
/// <para>
/// Then by whether a file NAMED a new class, which was wrong in the other direction: the test
/// matched any identifier starting with the name, so adding Author rewrote Book.cs (it has an
/// <c>Author</c> property), and Task or Date would match <c>Task&lt;</c> or <c>DateTime</c> almost
/// everywhere. Comparing two generates needs no name at all: a file the add does not change has
/// the same two versions, and is never written.
/// </para>
/// </remarks>
public static class IncrementalFiles
{
    /// <summary>The half of a validator the user owns. Written once and never again.</summary>
    private const string WriteOnceSuffix = ".Custom.cs";

    /// <summary>
    /// What to do with <paramref name="relativePath"/>.
    /// </summary>
    /// <param name="relativePath">The file, relative to the project folder.</param>
    /// <param name="before">What the generator writes for the app as it is, or null when it
    /// writes no such file.</param>
    /// <param name="after">What it writes with the new classes.</param>
    /// <param name="existing">What is on disk, or null when nothing is.</param>
    public static FileAction Decide(
        string relativePath, string? before, string after, string? existing)
    {
        if (existing is null)
        {
            // Generated for the app as it is, and not there: the user removed it, or the app
            // was generated without the feature it belongs to. Either way it is not this add's.
            return before is null ? FileAction.Create : FileAction.Leave;
        }

        // OverwritePrompt promises in as many words that .Custom.cs rules are kept, and the
        // staged copy is always an empty stub - so anything below would overwrite a user's rules.
        if (IsWriteOnce(relativePath))
            return FileAction.Leave;

        if (before is not null && string.Equals(before, after, StringComparison.Ordinal))
            return FileAction.Leave;

        if (string.Equals(existing, after, StringComparison.Ordinal))
            return FileAction.Leave;

        // The add would create this file, and one is already there: the user's, not ours.
        if (before is null)
            return FileAction.SetAside;

        return string.Equals(existing, before, StringComparison.Ordinal)
            ? FileAction.Replace
            : FileAction.Merge;
    }

    /// <summary>Whether this file is the half the user owns. Internal rather than private so
    /// <see cref="IncrementalScaffolder"/> can ask the same question when it decides whether a
    /// skipped file is a stub worth reporting on.</summary>
    internal static bool IsWriteOnce(string relativePath)
        => relativePath.EndsWith(WriteOnceSuffix, StringComparison.OrdinalIgnoreCase);
}
