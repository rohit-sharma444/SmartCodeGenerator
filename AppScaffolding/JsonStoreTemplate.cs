namespace AppScaffolding;

/// <summary>Emits the source of the generated app's generic JSON store.</summary>
public static class JsonStoreTemplate
{
    /// <summary>Source of <c>Data/JsonStore.cs</c> in <paramref name="namespaceName"/>.</summary>
    public static string Source(string namespaceName) => $$"""
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

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

    /// <summary>Overwrites the store with <paramref name="all"/>. False if it could not be written.</summary>
    public bool SaveAll(List<T> all)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(all, Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

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
        return item;
    }

    private static PropertyInfo IdProperty =>
        typeof(T).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(typeof(T).Name + " has no Id property.");

    private static int GetId(T item) => (int)(IdProperty.GetValue(item) ?? 0);
}
""";
}
