namespace AppScaffolding.Layouts.Wpf;

/// <summary>The WPF layouts, keyed by <see cref="AppLayout"/>.</summary>
/// <remarks>Shaped like <c>TargetScaffolders</c>: a private array, the first element as the
/// default, and a resolve that cannot fail for a valid enum value.</remarks>
public static class WpfLayouts
{
    private static readonly IAppLayout[] All =
    [
        new WpfTabsLayout(),
        new WpfSidebarLayout(),
    ];

    /// <summary>The historical default: the tabbed window.</summary>
    public static IAppLayout Default { get; } = All[0];

    /// <summary>
    /// The layout for <paramref name="layout"/>, or <see cref="Default"/> while a layout is still
    /// unimplemented.
    /// </summary>
    /// <remarks>
    /// Falling back rather than throwing keeps a half-built registry from crashing a build during
    /// this feature's own implementation; by the end of it every enum value has an entry, and the
    /// registry-completeness test in <c>WpfLayoutRegistryTests</c> pins that.
    /// </remarks>
    public static IAppLayout Resolve(AppLayout layout)
    {
        var key = AppLayoutCatalog.KeyFor(layout);

        return All.FirstOrDefault(l => l.Key == key) ?? Default;
    }
}
