namespace AppScaffolding.Layouts.WinForms;

/// <summary>The Windows Forms layouts, keyed by <see cref="AppLayout"/>.</summary>
public static class WinFormsLayouts
{
    private static readonly IAppLayout[] All =
    [
        new WinFormsTabsLayout(),
        new WinFormsSidebarLayout(),
    ];

    public static IAppLayout Default { get; } = All[0];

    /// <summary>The layout for <paramref name="layout"/>, or <see cref="Default"/> while one is
    /// still unimplemented — see the note on <c>WpfLayouts.Resolve</c>.</summary>
    public static IAppLayout Resolve(AppLayout layout)
    {
        var key = AppLayoutCatalog.KeyFor(layout);

        return All.FirstOrDefault(l => l.Key == key) ?? Default;
    }
}
