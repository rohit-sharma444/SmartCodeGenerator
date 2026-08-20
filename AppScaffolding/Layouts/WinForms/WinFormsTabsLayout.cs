namespace AppScaffolding.Layouts.WinForms;

/// <summary>The original WinForms shape: a TabControl over IEntityViewModel.</summary>
/// <remarks>Delegates entirely to <see cref="WinFormsMainFormTemplate"/>, for the reason
/// <see cref="Wpf.WpfTabsLayout"/> gives: this layout's output is what every pre-layout app
/// already has.</remarks>
public sealed class WinFormsTabsLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.Tabs);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.Tabs];

    public IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context) =>
    [
        new("Views", "MainForm.cs",
            WinFormsMainFormTemplate.Source(
                context.RootNamespace, context.TitleLiteral, context.Palette, context.IconFileName,
                context.Customization.Login)),
    ];
}
