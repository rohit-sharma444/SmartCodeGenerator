namespace AppScaffolding;

/// <summary>The one generated file whose shape depends on the entity set.</summary>
public static class MainViewModelTemplate
{
    public static string Build(string rootNamespace, IReadOnlyList<string> entityNames)
        => Build(rootNamespace, entityNames, seed: true);

    /// <summary>As <see cref="Build(string, IReadOnlyList{string})"/>, seeding before the view
    /// models load only when <paramref name="seed"/> — with sample data off there is no SeedData.cs
    /// to call.</summary>
    public static string Build(string rootNamespace, IReadOnlyList<string> entityNames, bool seed)
        => Build(rootNamespace, entityNames, seed, login: false);

    /// <summary>
    /// As above; with <paramref name="login"/> an administrator also gets the Users pane.
    /// </summary>
    /// <remarks>
    /// Appended after the collection initialiser rather than inside it, so the output for an
    /// app with no login stays byte-identical to what this template has always produced.
    /// </remarks>
    public static string Build(
        string rootNamespace, IReadOnlyList<string> entityNames, bool seed, bool login)
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

        // The pane an administrator gets on top of the discovered entities. The business
        // shell opens this same view model from its Admin group; until then it is an ordinary
        // pane, which is what makes the four older layouts usable with a login.
        var usersPane = login
            ? """


        // The Users pane, for an administrator only.
        if (Session.IsAdmin)
            Entities.Add(new UsersViewModel());
"""
            : string.Empty;

        var authUsing = login ? $"using {rootNamespace}.Auth;\n" : string.Empty;

        return $$"""
using System.Collections.ObjectModel;
{{authUsing}}using {{rootNamespace}}.Data;
using {{rootNamespace}}.Models;

namespace {{rootNamespace}}.ViewModels;

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
        RelationshipResolver.Resolve(Entities);{{usersPane}}
    }

    public ObservableCollection<IEntityViewModel> Entities { get; }
}
""";
    }
}
