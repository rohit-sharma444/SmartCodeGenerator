namespace AppScaffolding;

/// <summary>The one generated file whose shape depends on the entity set.</summary>
public static class MainViewModelTemplate
{
    public static string Build(string appName, IReadOnlyList<string> entityNames)
    {
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
        Entities = new ObservableCollection<IEntityViewModel>
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
