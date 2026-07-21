namespace AppScaffolding;

/// <summary>Which storage backend the generated Data core persists through.</summary>
public enum PersistenceKind
{
    /// <summary>Per-type JSON files (the default).</summary>
    Json,

    /// <summary>A SQLite database via EF Core.</summary>
    Sqlite,
}
