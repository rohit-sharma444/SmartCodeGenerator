using AppScaffolding;
using AppScaffolding.Screens;

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
    /// <param name="target">
    /// The target the build will use, if chosen yet. Gates the "Entry template" line: an
    /// EntryTemplate can be set via the inline CLI token on any target, bypassing the wizard's own
    /// gate, but nothing downstream ever reads it unless the target supports the feature
    /// (<see cref="EntryTemplates.Supports"/>) and the effective layout is
    /// <see cref="AppLayout.Business"/> — the same gate the wizard and menu already use to decide
    /// whether to ask. Echoing the line for a build the choice will not affect would tell the user
    /// something untrue.
    /// </param>
    public static IReadOnlyList<string> Lines(AppCustomization customization, ITargetScaffolder? target)
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

        if (customization.EntryTemplate is not null
            && target is not null && EntryTemplates.Supports(target.Key)
            && customization.EffectiveLayout == AppLayout.Business)
        {
            lines.Add($"  Entry template: {EntryTemplateCatalog.KeyFor(customization.EntryTemplate.Value)}");
        }

        // Named, not counted: the point of typing them is to check the generator agreed.
        if (customization.Transactions.Length > 0)
            lines.Add($"  Transactions: {customization.Transactions}");

        if (customization.Masters.Length > 0)
            lines.Add($"  Masters: {customization.Masters}");

        var features = new List<string>();

        if (!customization.Validation) features.Add("no validation layer");
        if (customization.Logging) features.Add("logging");
        if (!customization.SampleSeedData) features.Add("no sample data");

        // Stated only when off. Login is the one setting whose default is NOT the pre-feature
        // behaviour, so "login" on every summary would be noise; "no login" is the decision.
        if (!customization.Login) features.Add("no login");

        if (features.Count > 0)
            lines.Add("  Features: " + string.Join(", ", features));

        return lines;
    }
}
