namespace AppScaffolding;

/// <summary>
/// Emits the generated app's typed CRUD repositories: one generic base plus one thin
/// subclass per entity.
/// </summary>
/// <remarks>
/// Standalone by design. The running app persists through EntityViewModel and
/// JsonStore.SaveAll; these repositories are an additional, typed API to code against and
/// touch none of that. They are built only on JsonStore's existing public surface
/// (GetAll/SaveAll/NextId), so JsonStore itself is unchanged.
/// </remarks>
public static class RepositoryTemplate
{
    /// <summary>Source of <c>Data/Repository.cs</c> for <paramref name="appName"/>.</summary>
    public static string Base(string appName) => $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace {{appName}}.Data;

/// <summary>Typed CRUD over one entity's JSON store, keyed by an int Id.</summary>
/// <remarks>
/// A separate store from the app's UI path (EntityViewModel/JsonStore): both persist to the
/// same per-type JSON file, so mixing them at runtime over one entity is last-writer-wins.
/// </remarks>
public abstract class Repository<T> where T : class, new()
{
    private readonly JsonStore<T> _store = new();

    /// <summary>Every stored record.</summary>
    public List<T> GetAll() => _store.GetAll();

    /// <summary>The record with this Id, or null when there is none.</summary>
    public T? GetById(int id) => _store.GetAll().FirstOrDefault(item => GetId(item) == id);

    /// <summary>Assigns the next Id, appends the record, and persists it.</summary>
    public T Create(T item)
    {
        var all = _store.GetAll();
        SetId(item, _store.NextId(all));
        all.Add(item);
        _store.SaveAll(all);
        return item;
    }

    /// <summary>Replaces the record sharing this item's Id. False when there is none.</summary>
    public bool Update(T item)
    {
        var all = _store.GetAll();
        var index = all.FindIndex(existing => GetId(existing) == GetId(item));
        if (index < 0)
            return false;

        all[index] = item;
        return _store.SaveAll(all);
    }

    /// <summary>Removes the record with this Id. False when there is none.</summary>
    public bool Delete(int id)
    {
        var all = _store.GetAll();
        if (all.RemoveAll(item => GetId(item) == id) == 0)
            return false;

        return _store.SaveAll(all);
    }

    private static PropertyInfo IdProperty =>
        typeof(T).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(typeof(T).Name + " has no Id property.");

    private static int GetId(T item) => (int)(IdProperty.GetValue(item) ?? 0);

    private static void SetId(T item, int id) => IdProperty.SetValue(item, id);
}
""";

    /// <summary>Source of <c>Data/<paramref name="className"/>Repository.cs</c>.</summary>
    public static string Typed(string appName, string className) => $$"""
using {{appName}}.Models;

namespace {{appName}}.Data;

/// <summary>Typed CRUD for {{className}} records.</summary>
public sealed class {{className}}Repository : Repository<{{className}}>
{
}
""";
}
