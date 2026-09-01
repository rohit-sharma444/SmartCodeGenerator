namespace AppScaffolding.Incremental;

/// <summary>What an add did.</summary>
public sealed record IncrementalResult(
    bool Success,
    IReadOnlyList<string> Written,
    int UntouchedFiles,
    string Message);

/// <summary>
/// Writes only what an application is missing.
/// </summary>
/// <remarks>
/// Scaffolds the whole application into a temporary root and copies across only the files
/// <see cref="IncrementalFiles.ShouldCopy"/> allows. <see cref="AppScaffolder"/> is deliberately
/// not modified: its write path is shared by seven targets, and a filter honoured for one and
/// missed for another is a silent defect — a file not written is a compile error at best and a
/// missing screen at worst. Copying instead makes "every other file is untouched" true by
/// construction rather than by getting a filter right seven times.
/// <para>
/// This relies on generated CONTENT being independent of the output folder, which it is:
/// <c>projectName</c> comes from the app name and <c>rootNamespace</c> from that, never from the
/// path. <c>Generated_content_does_not_depend_on_the_output_folder</c> is a test of exactly that
/// assumption — a guard on this approach, not scaffolding for it.
/// </para>
/// </remarks>
public static class IncrementalScaffolder
{
    /// <summary>
    /// Where the throwaway generate goes.
    /// </summary>
    /// <remarks>
    /// Short on purpose. A GUID under the temp path, plus a project name, plus
    /// <c>Models\Whatever.cs</c>, blows MAX_PATH — and the tell is MSB3030 claiming a file is
    /// missing that is plainly there.
    /// </remarks>
    private const string StagingRoot = @"C:\cg-t";

    public static IncrementalResult Add(
        ScaffoldRequest request, IReadOnlyList<string> newClassNames)
    {
        var destination = GeneratedProjectPath.Folder(
            request.WorkspaceRoot, request.AppName, request.Target);

        var before = CountFiles(destination);

        if (newClassNames.Count == 0)
            return new IncrementalResult(true, [], before, "Nothing new to add.");

        var staging = Path.Combine(StagingRoot, Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(staging);

            var staged = new AppScaffolder().Scaffold(request with { WorkspaceRoot = staging });

            if (!staged.Success)
                return new IncrementalResult(false, [], before, staged.Message);

            var source = GeneratedProjectPath.Folder(staging, request.AppName, request.Target);

            var written = Copy(source, destination);

            return new IncrementalResult(
                true,
                written,
                Math.Max(0, before - written.Count),
                $"Added {newClassNames.Count} class(es).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new IncrementalResult(false, [], before, ex.Message);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>
    /// Copies the files that belong to this add, and returns what was written.
    /// </summary>
    /// <remarks>
    /// <c>obj</c> and <c>bin</c> are skipped: they are build output, they are enormous, and
    /// copying them over a folder that has already been built is meaningless.
    /// </remarks>
    private static List<string> Copy(string source, string destination)
    {
        var written = new List<string>();

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);

            if (IsBuildOutput(relative))
                continue;

            var target = Path.Combine(destination, relative);
            var existing = File.Exists(target) ? File.ReadAllText(target) : null;

            if (!IncrementalFiles.ShouldCopy(relative, File.ReadAllText(file), existing))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);

            written.Add(target);
        }

        return written;
    }

    private static bool IsBuildOutput(string relativePath)
        => relativePath.StartsWith("obj", StringComparison.OrdinalIgnoreCase)
        || relativePath.StartsWith("bin", StringComparison.OrdinalIgnoreCase);

    private static int CountFiles(string folder)
        => Directory.Exists(folder)
            ? Directory
                .GetFiles(folder, "*", SearchOption.AllDirectories)
                .Count(f => !IsBuildOutput(Path.GetRelativePath(folder, f)))
            : 0;

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A generated project's obj folder can hold a lock. A leftover staging folder is not
            // worth failing an add that has already written its files.
        }
    }
}
