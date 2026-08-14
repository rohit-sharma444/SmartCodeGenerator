namespace AppScaffolding;

/// <summary>
/// Where a generated project lands, given the app name and the target.
/// </summary>
/// <remarks>
/// Exists so there is exactly one definition. <see cref="AppScaffolder"/> writes here, and the
/// CLI asks before overwriting what is here — and a guard that computed the folder even slightly
/// differently from the writer would not fail loudly, it would just quietly stop firing and let
/// the overwrite through unannounced.
/// </remarks>
public static class GeneratedProjectPath
{
    /// <summary>The project (and assembly) name: the app name plus the target's suffix.</summary>
    public static string ProjectName(string appName, ITargetScaffolder target)
        => appName + target.FolderSuffix;

    /// <summary>The project folder, directly under the workspace root.</summary>
    public static string Folder(string workspaceRoot, string appName, ITargetScaffolder target)
        => Path.Combine(workspaceRoot, ProjectName(appName, target));

    /// <summary>The project file inside that folder, named after it.</summary>
    public static string CsprojPath(string workspaceRoot, string appName, ITargetScaffolder target)
    {
        var projectName = ProjectName(appName, target);

        return Path.Combine(workspaceRoot, projectName, projectName + ".csproj");
    }

    /// <summary>
    /// Whether a generated project is already there.
    /// </summary>
    /// <remarks>
    /// Keyed on the project file rather than the folder, because that is exactly what
    /// <see cref="AppScaffolder"/> tests to decide whether to reuse: an empty folder left behind
    /// by a failed run is not a project, and treating it as one would ask the user to confirm an
    /// overwrite of nothing.
    /// </remarks>
    public static bool Exists(string workspaceRoot, string appName, ITargetScaffolder target)
        => File.Exists(CsprojPath(workspaceRoot, appName, target));
}
