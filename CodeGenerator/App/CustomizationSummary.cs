using AppScaffolding;

namespace CodeGenerator.App;

/// <summary>
/// What the review loop prints about the customization a build will apply.
/// </summary>
/// <remarks>
/// Pure, and separated from the loop for the reason <see cref="ReviewActions"/> gives: the loop
/// reads the console and cannot be driven from a test, so anything decidable is lifted out of it.
/// <para>
/// Only differences from the default are listed. The customization was settled before discovery
/// ran, like the target, so what <c>approve</c> will produce has to be visible here rather than
/// being a surprise — but a run that customized nothing has nothing to say.
/// </para>
/// </remarks>
internal static class CustomizationSummary
{
    /// <summary>One line per setting the user changed. Empty for
    /// <see cref="AppCustomization.Default"/>.</summary>
    public static IReadOnlyList<string> Lines(AppCustomization customization)
    {
        var defaults = AppCustomization.Default;
        var lines = new List<string>();

        if (customization.HasBranding)
            lines.Add($"  Company: {customization.Company}");

        if (customization.Theme != defaults.Theme)
            lines.Add($"  Theme: {customization.Theme.ToString().ToLowerInvariant()}");

        if (customization.Accent is not null)
            lines.Add($"  Accent: {customization.Accent}");

        if (customization.IconPath is not null)
            lines.Add($"  Icon: {customization.IconPath}");

        // The rendered title, not the pattern: "{company} - {app}" is the instruction, and what the
        // user wants to check is the window caption it produces.
        if (customization.TitlePattern != defaults.TitlePattern)
            lines.Add($"  Title: {customization.TitlePattern}");

        // The bare prefix, not the resolved root: the root ends in the target's folder suffix
        // (Shop.Console, Shop.WinForms), and the target can still be changed at `approve`.
        if (customization.NamespacePrefix.Length > 0)
            lines.Add($"  Namespace prefix: {customization.NamespacePrefix}");

        // Named, not "differs from tabs": `layout tabs` typed against a remembered dashboard is a
        // deliberate choice, and the one place the user can check that the override took effect.
        if (customization.Layout is not null)
            lines.Add($"  Layout: {AppLayoutCatalog.KeyFor(customization.Layout.Value)}");

        var features = new List<string>();

        if (!customization.Validation) features.Add("no validation layer");
        if (customization.Logging) features.Add("logging");
        if (!customization.SampleSeedData) features.Add("no sample data");

        if (features.Count > 0)
            lines.Add("  Features: " + string.Join(", ", features));

        return lines;
    }
}
