namespace AppScaffolding;

/// <summary>Emits the source of the generated app's generic JSON store.</summary>
public static class JsonStoreTemplate
{
    /// <summary>Source of <c>Data/JsonStore.cs</c> in <paramref name="namespaceName"/>, which every
    /// caller passes as the FULL data namespace (<c>{root}.Data</c>), not the root.</summary>
    /// <param name="auth">
    /// Unused: kept so every existing caller of this overload compiles unchanged.
    /// <see cref="ActingUserTemplate"/> now decides, from its own <c>sessionAuth</c>, whether
    /// <c>ActingUser.Role()</c> reads a real session or returns null — this template no longer
    /// needs to ask.
    /// </param>
    public static string Source(string namespaceName, bool auth = false)
    {
        // namespaceName is "{root}.Data", not the root — trimming its trailing ".Data" reaches
        // {root}.Validation, where Permissions, ValidationOperation and ActingUser live.
        // Redefining this parameter to mean the root instead would silently emit
        // "namespace Acme.Data.Data".
        var rootNamespace = namespaceName.EndsWith(".Data", StringComparison.Ordinal)
            ? namespaceName[..^".Data".Length]
            : namespaceName;

        return $$"""
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using {{rootNamespace}}.Validation;

namespace {{namespaceName}};

/// <summary>JSON-backed list store keyed by an int Id. One file per type.</summary>
public sealed class JsonStore<T> : IStore<T> where T : new()
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path =
        Path.Combine(AppContext.BaseDirectory, typeof(T).Name + ".json");

    /// <summary>All stored records. An absent, unreadable, or corrupt file reads as empty.</summary>
    public List<T> GetAll()
    {
        if (!File.Exists(_path))
            return new List<T>();

        try
        {
            return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(_path)) ?? new List<T>();
        }
        catch (Exception ex)
            when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A GUI has nowhere to print a stack trace: start empty rather than
            // die before the window appears.
            return new List<T>();
        }
    }

    /// <summary>Overwrites the store with <paramref name="all"/>.</summary>
    /// <remarks>
    /// The coarse enforcement point, and the only one no front end can go round. SaveAll is handed
    /// a whole list, so it cannot tell an insert from a delete and can only ask whether this user
    /// may write this type at all. The operation-aware checks live in Repository and at the
    /// grids' delete buttons. Under the default matrix this refuses nothing except the app's own
    /// User records.
    /// </remarks>
    public StoreWrite SaveAll(List<T> all)
    {
        if (!Permissions.Allows(ValidationOperation.Update, typeof(T).Name, ActingUser.Role()))
            return new StoreWrite(false, Permissions.Refusal(ValidationOperation.Update, typeof(T).Name));

        return WriteToDisk(all);
    }

    /// <summary>
    /// Overwrites the store with <paramref name="all"/> without asking <see cref="Permissions"/>.
    /// </summary>
    /// <remarks>
    /// The legitimate exception to SaveAll's gate: UserSeed.EnsureAdminSeeded and SeedData.Seed
    /// both run before anyone COULD be signed in — the former before the first account exists,
    /// the latter seeding business data on the same first run. A signed-out caller of a login app
    /// is correctly refused by SaveAll — that refusal, applied here too, would mean no freshly
    /// generated login app could ever create its first account or its starting data, because
    /// both are exactly the operation an empty store has nobody authorised to perform. This
    /// method exists so those call sites can be exempt, by name, rather than by a null check
    /// anyone could quietly rely on.
    /// </remarks>
    public StoreWrite SaveAllUnchecked(List<T> all) => WriteToDisk(all);

    private StoreWrite WriteToDisk(List<T> all)
    {
        StampAuditFields(all, GetAll());

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(all, Options));
            return StoreWrite.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StoreWrite(false, null);
        }
    }

{{AuditStampTemplate.Members()}}

    /// <summary>One past the highest Id in <paramref name="existing"/>, or 1 when there are none.</summary>
    public int NextId(IEnumerable<T> existing)
    {
        var ids = existing.Select(GetId).ToList();
        return ids.Count == 0 ? 1 : ids.Max() + 1;
    }

    /// <summary>A new record with its Id already assigned.</summary>
    public T NewItem(IEnumerable<T> existing)
    {
        var item = new T();
        IdProperty.SetValue(item, NextId(existing));
        StartDatesAtToday(item);
        return item;
    }

{{NewRecordTemplate.StoreMember()}}

    private static PropertyInfo IdProperty =>
        typeof(T).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(typeof(T).Name + " has no Id property.");

    private static int GetId(T item) => (int)(IdProperty.GetValue(item) ?? 0);
}
""";
    }
}
