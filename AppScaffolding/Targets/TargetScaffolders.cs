namespace AppScaffolding.Targets;

/// <summary>The available output targets, keyed by their CLI key.</summary>
public static class TargetScaffolders
{
    private static readonly ITargetScaffolder[] All =
    {
        new WpfTarget(),
        new ConsoleTarget(),
        new WebApiTarget(),
        new ClassLibraryTarget(),
        new WorkerTarget(),
    };

    /// <summary>Target keys in display order; the first is the default.</summary>
    public static IReadOnlyList<string> Keys { get; } = All.Select(t => t.Key).ToList();

    public static IReadOnlyDictionary<string, ITargetScaffolder> ByKey { get; } =
        All.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The historical default: the WPF app.</summary>
    public static ITargetScaffolder Default { get; } = All[0];

    /// <summary>Resolves a key (case-insensitively) to a target.</summary>
    public static bool TryResolve(string key, out ITargetScaffolder target)
        => ByKey.TryGetValue(key, out target!);
}
