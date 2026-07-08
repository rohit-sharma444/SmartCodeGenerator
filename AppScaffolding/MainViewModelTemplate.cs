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
    }

    public ObservableCollection<IEntityViewModel> Entities { get; }
}
""";
    }
}
