using CodeGenerationEngine.Core.Validation;
using DomainDiscovery.Model;

namespace AppScaffolding.Incremental;

/// <summary>What an add did.</summary>
public sealed record IncrementalResult(
    bool Success,
    IReadOnlyList<string> Written,
    int UntouchedFiles,
    string Message)
{
    /// <summary>
    /// The write-once validator stubs that still declare the hand-written hook in the shape
    /// emitted before the validation pipeline, one sentence each.
    /// </summary>
    /// <remarks>
    /// Its own member rather than more text inside <see cref="Message"/>: an add's message is not
    /// printed at all on the success path (see <c>AddReport</c>), and this has to be read. Set
    /// through an initializer so the four positional arguments every existing caller passes are
    /// untouched.
    /// </remarks>
    public IReadOnlyList<string> OutdatedValidatorStubs { get; init; } = [];

    /// <summary>
    /// The files an add had changes for and could not safely make, one entry each, with the
    /// lines a user has to copy in by hand.
    /// </summary>
    /// <remarks>
    /// A file the user has edited where an add's lines go, or a file of theirs where an add would
    /// create one, is left exactly as it is; the version the add wanted is written beside it
    /// (<see cref="SetAside.NewVersionPath"/>). Its own member, like
    /// <see cref="OutdatedValidatorStubs"/>, because it is what needs doing after the add.
    /// </remarks>
    public IReadOnlyList<SetAside> NotMerged { get; init; } = [];
}

/// <summary>A file an add left as it was, the version it wanted written beside it, and the
/// changes to make by hand. Both paths are relative to the project folder.</summary>
public sealed record SetAside(
    string Path, string NewVersionPath, IReadOnlyList<LineChange> Changes);

/// <summary>
/// Writes only what an application is missing.
/// </summary>
/// <remarks>
/// Scaffolds the application twice into a temporary root - once as it is, once with the new
/// classes - and lets <see cref="IncrementalFiles.Decide"/> settle each file from the two versions
/// and the copy on disk. <see cref="AppScaffolder"/> is deliberately not modified: its write path
/// is shared by seven targets, and a filter honoured for one and missed for another is a silent
/// defect. Comparing two generates makes "every other file is untouched" true by construction.
/// <para>
/// The difference between the two generates is exactly what the new classes bring: their own
/// files, and their lines in the files that list the entities (seed data, the validator factory,
/// the DbContext, the screens list, Program.cs). Those lines are merged into the files on disk
/// (<see cref="LineMerge"/>) rather than the whole file being copied, so a hand edit elsewhere in
/// such a file survives - and a file whose two versions are the same, an existing class among
/// them, is never written at all, however much it differs from what is on disk.
/// </para>
/// <para>
/// Both generates use the settings the app was generated with (<see cref="BuildSettingsRecord"/>),
/// never the current run's. Generated content does not depend on the output folder
/// (<c>Generated_content_does_not_depend_on_the_output_folder</c> guards that), but it does depend
/// on the namespace and the features: an add under different ones wrote files into a namespace
/// the rest of the app did not use, and the app stopped compiling.
/// </para>
/// </remarks>
public static class IncrementalScaffolder
{
    /// <summary>
    /// Where the throwaway generates go.
    /// </summary>
    /// <remarks>
    /// Short on purpose. A GUID under the temp path, plus a project name, plus
    /// <c>Models\Whatever.cs</c>, blows MAX_PATH - and the tell is MSB3030 claiming a file is
    /// missing that is plainly there.
    /// </remarks>
    private const string StagingRoot = @"C:\cg-t";

    /// <summary>Appended to a file's name for the version an add could not merge into it. Not
    /// <c>.cs</c>, <c>.razor</c> or <c>.xaml</c>, so the project does not compile it.</summary>
    public const string NewVersionSuffix = ".new";

    public static IncrementalResult Add(
        ScaffoldRequest request, IReadOnlyList<string> newClassNames)
    {
        var destination = GeneratedProjectPath.Folder(
            request.WorkspaceRoot, request.AppName, request.Target);

        var before = CountFiles(destination);

        if (newClassNames.Count == 0)
            return new IncrementalResult(true, [], before, "Nothing new to add.");

        // The namespace, company, persistence and features the app already has.
        request = BuildSettingsRecord.For(destination, request);

        var staging = Path.Combine(StagingRoot, Guid.NewGuid().ToString("N")[..8]);

        try
        {
            var withRoot = Path.Combine(staging, "a");
            var withoutRoot = Path.Combine(staging, "b");
            Directory.CreateDirectory(withoutRoot);

            var baseline = new AppScaffolder().Scaffold(request with
            {
                WorkspaceRoot = withoutRoot,
                Model = Without(request.Model, newClassNames),
            });

            if (!baseline.Success)
                return new IncrementalResult(false, [], before, baseline.Message);

            // The second generate goes over a copy of the first, as a regenerate in place does.
            // Generated fresh, the files `dotnet new` writes (launchSettings.json, the .http file)
            // differ between the two in their random ports, and an add would then claim them.
            CopyFolder(withoutRoot, withRoot);

            var staged = new AppScaffolder().Scaffold(request with { WorkspaceRoot = withRoot });

            if (!staged.Success)
                return new IncrementalResult(false, [], before, staged.Message);

            var source = GeneratedProjectPath.Folder(withRoot, request.AppName, request.Target);
            var without = GeneratedProjectPath.Folder(withoutRoot, request.AppName, request.Target);

            var outdatedStubs = new List<string>();
            var notMerged = new List<SetAside>();
            var whole = new List<string>();
            var written = Copy(source, without, destination, outdatedStubs, notMerged, whole);

            // Added to the destination's record of generated files, never swept: an add writes
            // only part of the app, so what it did not write is not stale. Only the files written
            // whole: a merged file holds the user's lines too, and is not the generator's alone.
            GeneratedFilesManifest.Record(destination, whole);

            return new IncrementalResult(
                true,
                written,
                Math.Max(0, before - written.Count),
                $"Added {newClassNames.Count} class(es).")
            {
                OutdatedValidatorStubs = outdatedStubs,
                NotMerged = notMerged,
            };
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
    /// <paramref name="model"/> without the classes being added - the app as it is - and without
    /// any relationship pointing at them.
    /// </summary>
    /// <remarks>
    /// The entities are copied rather than edited: the model is the review's, and is still in use.
    /// <paramref name="newClassNames"/> are CLASS names, which the mapper can make differ from
    /// entity names ("Type" -> "TypeItem"), so they are matched through its own map.
    /// </remarks>
    internal static CandidateDomainModel Without(
        CandidateDomainModel model, IReadOnlyList<string> newClassNames)
    {
        DomainModelMapper.ToClassDefinitions(model, out var classNames);

        var leaving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in model.Entities)
        {
            var className = classNames.TryGetValue(entity.Name, out var mapped) ? mapped : entity.Name;

            if (newClassNames.Contains(className, StringComparer.OrdinalIgnoreCase))
                leaving.Add(entity.Name);
        }

        var result = new CandidateDomainModel();

        foreach (var entity in model.Entities.Where(e => !leaving.Contains(e.Name)))
        {
            var copy = new CandidateEntity
            {
                Name = entity.Name,
                ImportedAs = entity.ImportedAs,
                Provenance = entity.Provenance,
                Confidence = entity.Confidence,
                SourceTitle = entity.SourceTitle,
                Role = entity.Role,
            };

            copy.Fields.AddRange(entity.Fields);
            copy.Relationships.AddRange(entity.Relationships.Where(r => !leaving.Contains(r.Target)));
            copy.Proposals.AddRange(entity.Proposals);

            result.Entities.Add(copy);
        }

        return result;
    }

    /// <summary>
    /// Writes what this add changes, returns every file written, and collects a sentence into
    /// <paramref name="outdatedStubs"/> for every write-once validator stub kept whose
    /// hand-written hook predates the validation pipeline.
    /// </summary>
    /// <remarks>
    /// <c>obj</c> and <c>bin</c> are skipped: they are build output, they are enormous, and
    /// copying them over a folder that has already been built is meaningless.
    /// <para>
    /// The stub check sits on the LEAVE path on purpose. A kept stub is exactly the file that
    /// does not get the new hook, while the <c>.Generated.cs</c> beside it may change - which is
    /// what leaves the pair uncompilable. Reported, never rewritten: see
    /// <c>ValidatorTemplate.HasOutdatedCustomHook</c> for why a rewrite is the worse outcome.
    /// </para>
    /// </remarks>
    private static List<string> Copy(
        string source, string without, string destination, List<string> outdatedStubs,
        List<SetAside> notMerged, List<string> whole)
    {
        var written = new List<string>();

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);

            // The staging runs' own records describe the staging copy; the destination keeps its own.
            if (IsBuildOutput(relative) || IsGeneratedFilesRecord(relative))
                continue;

            var target = Path.Combine(destination, relative);
            var baselineFile = Path.Combine(without, relative);

            var after = SourceFileText.Read(file, out _, out _);
            var before = File.Exists(baselineFile)
                ? SourceFileText.Read(baselineFile, out _, out _)
                : null;

            var hasBom = false;
            var existing = File.Exists(target)
                ? SourceFileText.Read(target, out hasBom, out _)
                : null;

            switch (IncrementalFiles.Decide(relative, before, after, existing))
            {
                case FileAction.Leave:
                    if (existing is not null
                        && IncrementalFiles.IsWriteOnce(relative)
                        && ValidatorTemplate.HasOutdatedCustomHook(existing))
                    {
                        outdatedStubs.Add(ValidatorTemplate.OutdatedCustomHookReport(
                            relative, ModelNameOf(relative)));
                    }

                    break;

                case FileAction.Create:
                case FileAction.Replace:
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: true);
                    written.Add(target);
                    whole.Add(target);
                    break;

                case FileAction.Merge:
                    var merge = LineMerge.Apply(before!, after, existing!);

                    if (merge.Merged is { } merged)
                    {
                        SourceFileText.Write(target, merged, hasBom);
                        written.Add(target);
                    }
                    else
                    {
                        notMerged.Add(SetAsideNewVersion(file, target, relative, merge.Changes));
                    }

                    break;

                case FileAction.SetAside:
                    notMerged.Add(SetAsideNewVersion(
                        file, target, relative, [LineMerge.Whole(after)]));
                    break;
            }
        }

        return written;
    }

    /// <summary>
    /// Writes the version the add wanted beside <paramref name="target"/>, which stays as it is,
    /// under a name no build glob picks up.
    /// </summary>
    private static SetAside SetAsideNewVersion(
        string staged, string target, string relative, IReadOnlyList<LineChange> changes)
    {
        File.Copy(staged, target + NewVersionSuffix, overwrite: true);

        return new SetAside(relative, relative + NewVersionSuffix, changes);
    }

    /// <summary>The model a <c>&lt;Model&gt;Validator.Custom.cs</c> belongs to, so the reported
    /// replacement line names the real type rather than a placeholder.</summary>
    private static string ModelNameOf(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        var validator = name.IndexOf("Validator.", StringComparison.OrdinalIgnoreCase);

        return validator > 0 ? name[..validator] : "<Model>";
    }

    private static bool IsBuildOutput(string relativePath)
        => relativePath.StartsWith("obj", StringComparison.OrdinalIgnoreCase)
        || relativePath.StartsWith("bin", StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneratedFilesRecord(string relativePath)
        => relativePath.StartsWith(
            GeneratedFilesManifest.FolderName + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static int CountFiles(string folder)
        => Directory.Exists(folder)
            ? Directory
                .GetFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(folder, f))
                .Count(f => !IsBuildOutput(f) && !IsGeneratedFilesRecord(f))
            : 0;

    /// <summary>Copies <paramref name="from"/> into <paramref name="to"/>, without build output.</summary>
    private static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            var parts = relative.Split(Path.DirectorySeparatorChar);

            if (parts.Any(p => p.Equals("obj", StringComparison.OrdinalIgnoreCase)
                               || p.Equals("bin", StringComparison.OrdinalIgnoreCase)))
                continue;

            var target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

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
