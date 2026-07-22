namespace AppScaffolding;

/// <summary>
/// Emits the generated app's typed CRUD repositories: one generic base plus one thin
/// subclass per entity.
/// </summary>
/// <remarks>
/// These repositories are a typed CRUD API (GetAll/GetById/Create/Update/Delete) built entirely
/// against the <c>IStore&lt;T&gt;</c> seam obtained from <c>StoreFactory</c>, so the same
/// generated code works unchanged whether that seam resolves to JsonStore or, when the app was
/// generated with the SQLite backend, EfStore.
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
/// Persists through the store returned by <c>StoreFactory.Create&lt;T&gt;()</c> — JSON files by
/// default, or SQLite via EF Core when the app was generated with that backend. In the JSON
/// backend, this store shares its per-type JSON file with the UI path's <c>EntityViewModel</c>,
/// so mixing the two at runtime is last-writer-wins.
/// </remarks>
public abstract class Repository<T> where T : class, new()
{
    private readonly IStore<T> _store = StoreFactory.Create<T>();

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
