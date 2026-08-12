using DomainDiscovery.Model;

namespace AppScaffolding;

/// <summary>
/// Everything one <see cref="AppScaffolder.Scaffold(ScaffoldRequest)"/> run needs.
/// </summary>
/// <remarks>
/// A parameter object rather than a seventh and eighth argument: the overload chain already
/// stood at four overloads and six arguments before customization, and the older overloads are
/// kept only as thin wrappers over this.
/// <para>
/// <paramref name="Year"/> is supplied rather than read from the clock inside a template,
/// so the copyright banner cannot turn golden-output tests red on 1 January.
/// </para>
/// </remarks>
public sealed record ScaffoldRequest(
    CandidateDomainModel Model,
    string AppName,
    string WorkspaceRoot,
    ITargetScaffolder Target,
    PersistenceKind Persistence,
    ImportedData? ImportedData,
    AppCustomization Customization,
    int Year)
{
    /// <summary>A request with today's defaults: JSON persistence, no imported data, neutral
    /// customization, and the current year for the copyright banner.</summary>
    public static ScaffoldRequest For(
        CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target)
        => new(model, appName, workspaceRoot, target, PersistenceKind.Json,
               ImportedData: null, AppCustomization.Default, DateTime.Now.Year);
}
