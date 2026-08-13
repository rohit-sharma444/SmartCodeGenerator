namespace AppScaffolding.Layouts.Wpf;

/// <summary>
/// The original WPF shape: a TabControl over IEntityViewModel, one tab per entity.
/// </summary>
/// <remarks>
/// Reimplements NOTHING. Every byte comes from <see cref="MainWindowTemplate"/>, which is
/// untouched, because this layout's output is the output every pre-layout app has — and the
/// regression proof is equality against that template, not a substring assertion.
/// </remarks>
public sealed class WpfTabsLayout : IAppLayout
{
    public string Key => AppLayoutCatalog.KeyFor(AppLayout.Tabs);

    public string Description => AppLayoutCatalog.Descriptions[AppLayout.Tabs];

    public IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context) =>
    [
        new("Views", "MainWindow.xaml",
            MainWindowTemplate.Xaml(
                context.RootNamespace, context.TitleAttribute, context.Themed, context.IconFileName)),
        new("Views", "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(context.RootNamespace)),
    ];
}
