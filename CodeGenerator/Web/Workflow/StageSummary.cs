using AppScaffolding;
using AppScaffolding.Layouts;
using CodeGenerator.App;

namespace CodeGenerator.Web.Workflow;

/// <summary>
/// The answer a node holds, in as few words as fit in a box.
/// </summary>
/// <remarks>
/// The diagram is the summary of every decision as well as the navigation: a box that says only
/// "Application Type" makes the user open it to remember what they chose, and going back to look is the
/// thing this feature exists to make cheap.
/// <para>
/// Pure, so every line of it is asserted directly rather than read off a rendered page.
/// </para>
/// </remarks>
public static class StageSummary
{
    /// <summary>What <paramref name="stage"/> has been answered, or null when it has nothing to
    /// show yet.</summary>
    public static string? For(BuildSession session, BuildStage stage)
        => stage switch
        {
            BuildStage.Describe => Text(session.Description),
            BuildStage.AppType => session.Target is null
                ? null
                : ApplicationTypeMenu.LabelFor(session.Target),
            BuildStage.Data => session.HoldsAnAnswer(stage) ? DataAnswer(session) : null,

            // Guarded like the data node: the customization always HAS a value, so a box nobody
            // has answered would otherwise announce "defaults" as though it had been decided.
            BuildStage.Look => session.HoldsAnAnswer(stage) ? Look(session.Customization) : null,
            BuildStage.Discover => session.Model is null
                ? null
                : Classes(session.Model.Entities.Count),
            BuildStage.Review => session.Model is null
                ? null
                : Classes(session.Model.Entities.Count),

            // The links, not the classes: the node before it already counts those, and two boxes
            // both saying "9 classes" would tell the user nothing about what the second holds.
            BuildStage.Relationships => session.Model is null
                ? null
                : Links(session.Model.Entities.Sum(e => e.Relationships.Count)),
            BuildStage.Generate => session.Generated is null
                ? null
                : Path.GetFileName(session.Generated.Scaffold.ProjectPath),
            BuildStage.Run => session.Started
                ? "started"
                : session.Generated is null
                    ? null
                    : session.Generated.BuildSucceeded ? "ready to start" : "did not build",
            _ => null,
        };

    /// <summary>
    /// The data answer. "No data" is an answer and says so, and a source named inline is an answer
    /// before the node is opened at all.
    /// </summary>
    private static string? DataAnswer(BuildSession session)
        => session.Source is not null
            ? Path.GetFileName(session.Source.Location)
            : "discovered from the web";

    /// <summary>
    /// What was customized, or "defaults" when nothing was. Named settings rather than a count:
    /// "3 changes" tells the user nothing they wanted to know.
    /// </summary>
    private static string? Look(AppCustomization customization)
    {
        if (customization == AppCustomization.Default)
            return "defaults";

        var parts = new List<string>();

        if (customization.HasBranding)
            parts.Add(customization.Company);

        if (customization.Theme != AppTheme.Default)
            parts.Add(customization.Theme.ToString().ToLowerInvariant());

        if (customization.Layout is not null)
            parts.Add(AppLayoutCatalog.KeyFor(customization.EffectiveLayout));

        if (customization.NamespacePrefix.Length > 0)
            parts.Add(customization.NamespacePrefix);

        // Something was changed that none of the above names — a flag, an icon, an accent. Better
        // to say so than to look like the defaults.
        return parts.Count == 0 ? "customized" : string.Join(", ", parts);
    }

    private static string Classes(int count) => count == 1 ? "1 class" : $"{count} classes";

    private static string Links(int count)
        => count == 1 ? "1 relationship" : $"{count} relationships";

    /// <summary>Trimmed to a box's worth. A description can be a paragraph.</summary>
    private static string? Text(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Length == 0)
            return null;

        return trimmed.Length <= 40 ? trimmed : trimmed[..39].TrimEnd() + "…";
    }
}
