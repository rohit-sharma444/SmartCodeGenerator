namespace AppScaffolding;

/// <summary>Emits <c>Data/StoreFactory.cs</c>: the single place that decides which
/// <see cref="IStore{T}"/> implementation the generated app persists through. Only its body
/// differs by <see cref="PersistenceKind"/>, so Repository and SeedData stay backend-agnostic.</summary>
public static class StoreFactoryTemplate
{
    /// <summary>Source of <c>Data/StoreFactory.cs</c> in <paramref name="rootNamespace"/>.Data for
    /// the chosen <paramref name="persistence"/> backend.</summary>
    public static string Source(string rootNamespace, PersistenceKind persistence)
    {
        var (backend, note) = persistence switch
        {
            PersistenceKind.Sqlite => ("new EfStore<T>()", "a SQLite database via EF Core"),
            _ => ("new JsonStore<T>()", "per-type JSON files"),
        };

        return $$"""
namespace {{rootNamespace}}.Data;

/// <summary>Constructs the configured store for an entity type. This build persists to {{note}}.</summary>
public static class StoreFactory
{
    /// <summary>The store for entity type <typeparamref name="T"/>.</summary>
    public static IStore<T> Create<T>() where T : class, new() => {{backend}};
}
""";
    }
}
