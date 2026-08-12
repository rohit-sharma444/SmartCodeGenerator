namespace AppScaffolding;

/// <summary>The one generated file whose shape depends on the entity set.</summary>
public static class MainViewModelTemplate
{
    public static string Build(string appName, IReadOnlyList<string> entityNames)
        => Build(appName, entityNames, seed: true);

    /// <summary>As <see cref="Build(string, IReadOnlyList{string})"/>, seeding before the view
    /// models load only when <paramref name="seed"/> — with sample data off there is no SeedData.cs
    /// to call.</summary>
    public static string Build(string appName, IReadOnlyList<string> entityNames, bool seed)
    {
        // Its own fragment rather than SeedDataTemplate.EnsureSeededCall: this call site is inside
        // a constructor, so it is indented and carries the comment explaining its position.
        var seedCall = seed
            ? """
                      // Seed sample data before the view-models load: each EntityViewModel<T> reads its own
                      // store in its constructor, so seeding after this point would leave the first run empty.
                      SeedData.EnsureSeeded();


              """
            : string.Empty;

        var registrations = string.Join("\n",
            entityNames.Select(name =>
                $"            new EntityViewModel<{name}>(\"{name}\"),"));

        return $$"""
using System.Collections.ObjectModel;
using {{appName}}.Data;
using {{appName}}.Models;

namespace {{appName}}.ViewModels;

/// <summary>One tab per discovered entity.</summary>
public sealed class MainViewModel
{
    public MainViewModel()
    {
{{seedCall}}        Entities = new ObservableCollection<IEntityViewModel>
        {
{{registrations}}
        };

        // Each EntityViewModel loaded its own store in its constructor, so the foreign keys are
        // in but the navigation properties are still null -- they are [JsonIgnore] and never
        // persisted. This is the earliest point at which every store is loaded, and so the
        // earliest point at which they can be joined back up.
        RelationshipResolver.Resolve(Entities);
    }

    public ObservableCollection<IEntityViewModel> Entities { get; }
}
""";
    }
}
