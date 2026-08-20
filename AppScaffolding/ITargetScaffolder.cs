using CodeGenerationEngine.Models;

namespace AppScaffolding;

/// <summary>
/// A pluggable output target for <see cref="AppScaffolder"/>. The scaffolder writes the
/// shared core (Models + JsonStore + typed Repositories); a target supplies the project
/// type, the entry point / front-end files, and any post-create project fixup.
/// </summary>
public interface ITargetScaffolder
{
    /// <summary>Stable lower-case key used in the CLI prompt and the folder suffix, e.g. "console".</summary>
    string Key { get; }

    /// <summary>Human phrase completing "Generated N class(es) + &lt;Description&gt;.".</summary>
    string Description { get; }

    /// <summary>The .NET project type this target creates.</summary>
    DotNetProjectType ProjectType { get; }

    /// <summary>Suffix appended after the app name for the project folder and name.
    /// Empty for WPF (so its output is unchanged), ".Console"/".Api"/".Lib"/".Worker" otherwise.</summary>
    string FolderSuffix { get; }

    /// <summary>Target-specific files, given the project name (already suffixed, used as the
    /// namespace root) and the mapped entity class names. The shared Models/ and Data/ files
    /// are written by <see cref="AppScaffolder"/> before this runs.</summary>
    IReadOnlyList<GeneratedFile> FrontEndFiles(string projectName, IReadOnlyList<string> entityNames);

    /// <summary>
    /// As <see cref="FrontEndFiles(string,IReadOnlyList{string})"/>, for a target that varies its
    /// front end with <paramref name="customization"/>.
    /// </summary>
    /// <remarks>
    /// A default interface method, like <see cref="FixupProject"/> above it: only the targets
    /// with a window, a page or a banner override this, and the rest are untouched.
    /// </remarks>
    IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization)
        => FrontEndFiles(projectName, entityNames);

    /// <summary>
    /// As the overloads above, for a target whose front end varies with the entities' roles.
    /// </summary>
    /// <remarks>
    /// A default interface method like its neighbours: a target that does not care about roles
    /// needs no change, and the four screenless ones never will.
    /// </remarks>
    IReadOnlyList<GeneratedFile> FrontEndFiles(FrontEndContext context)
        => FrontEndFiles(context.ProjectName, context.EntityNames, context.Customization);

    /// <summary>
    /// Whether this target produces a user interface that a login could stand in front of.
    /// </summary>
    /// <remarks>
    /// False by default, so the console, Web API, worker and class-library targets are
    /// untouched by the auth core: they have no screens to gate, and emitting accounts into
    /// them would be dead code. Only the three targets with a window or a page override it.
    /// </remarks>
    bool HasScreens => false;

    /// <summary>Optional post-create reconciliation of the files `dotnet new` emitted.
    /// Runs only against a project the scaffolder just created. No-op by default.</summary>
    void FixupProject(string outputFolder) { }
}
