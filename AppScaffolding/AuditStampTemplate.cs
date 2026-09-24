namespace AppScaffolding;

/// <summary>
/// The members both stores splice in to fill in CreatedOn and ModifiedOn, which
/// <c>DomainModelMapper.AddAuditFields</c> gives every generated model.
/// </summary>
/// <remarks>
/// <para>
/// Nothing used to fill them in: a seeded row, and a record added or changed through the Web API,
/// all said 0001-01-01. Only the login's own User records were stamped, by hand, where they are
/// made.
/// </para>
/// <para>
/// In the store rather than in Repository, because the store is the one place every write goes
/// through: the Web API, console and worker reach it through Repository, but the Blazor and
/// desktop screens save through the store directly, and so do both seeders. One fragment spliced
/// into both stores, rather than a file of its own, because a dozen tests compile a store on its
/// own and a generated app is not the only thing that has to keep compiling.
/// </para>
/// </remarks>
public static class AuditStampTemplate
{
    /// <summary>
    /// Members of a store class over <c>T</c> that declares <c>GetId(T)</c> and imports
    /// System, System.Collections.Generic, System.Reflection and System.Text.Json. Indented for a
    /// class body, and spliced in at the margin.
    /// </summary>
    public static string Members() => """
    /// <summary>The model's CreatedOn, or null when it has none: a model is not obliged to.</summary>
    private static readonly PropertyInfo? CreatedOnProperty = AuditProperty("CreatedOn");

    /// <summary>The model's ModifiedOn, or null when it has none.</summary>
    private static readonly PropertyInfo? ModifiedOnProperty = AuditProperty("ModifiedOn");

    /// <summary>Stamps every record in <paramref name="all"/> that is new or changed since
    /// <paramref name="stored"/>: ModifiedOn with now, and CreatedOn, when it is not set, with
    /// what is on disk — or now, for a record that is new.</summary>
    /// <remarks>
    /// A store is handed the whole list, so it tells a new or changed record from an untouched one
    /// by comparing each with what is on disk: a record whose Id is not there is new, and one that
    /// no longer serialises the same way has changed. An untouched row keeps its ModifiedOn when a
    /// screen saves the lot, and a CreatedOn a caller left out — a PUT whose body has none — is
    /// put back rather than lost. A CreatedOn a caller did set, an imported one say, is kept.
    /// </remarks>
    private static void StampAuditFields(List<T> all, List<T> stored)
    {
        if (CreatedOnProperty is null && ModifiedOnProperty is null)
            return;

        var before = new Dictionary<int, T>();
        foreach (var record in stored)
            before.TryAdd(GetId(record), record);

        // Local time with no offset, the way every other date in this store is written: a Kind
        // does not survive SQLite, and a date that reads back different would look changed.
        var now = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);

        foreach (var record in all)
        {
            before.TryGetValue(GetId(record), out var previous);

            if (previous is not null
                && JsonSerializer.Serialize(record) == JsonSerializer.Serialize(previous))
                continue;

            if (CreatedOnProperty is { } created && IsUnset(created.GetValue(record)))
            {
                var original = previous is null ? null : created.GetValue(previous);
                created.SetValue(record, IsUnset(original) ? now : original);
            }

            ModifiedOnProperty?.SetValue(record, now);
        }
    }

    private static PropertyInfo? AuditProperty(string name)
    {
        var property = typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

        return property is { CanWrite: true }
               && (property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?))
            ? property
            : null;
    }

    private static bool IsUnset(object? value) => value is null or DateTime { Ticks: 0 };
""";
}
