namespace AppScaffolding;

/// <summary>Emits the source of the generated app's generic JSON store.</summary>
public static class JsonStoreTemplate
{
    public static string Source() => """
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace GeneratedCode;

/// <summary>JSON-backed list store keyed by an int Id. One file per type.</summary>
public sealed class JsonStore<T> where T : new()
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path =
        Path.Combine(AppContext.BaseDirectory, typeof(T).Name + ".json");

    public List<T> GetAll()
    {
        if (!File.Exists(_path))
            return new List<T>();
        return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(_path)) ?? new List<T>();
    }

    public T Add(T item)
    {
        var all = GetAll();
        SetId(item, all.Count == 0 ? 1 : all.Max(GetId) + 1);
        all.Add(item);
        Save(all);
        return item;
    }

    public bool Update(T item)
    {
        var all = GetAll();
        var id = GetId(item);
        var index = all.FindIndex(x => GetId(x) == id);
        if (index < 0)
            return false;
        all[index] = item;
        Save(all);
        return true;
    }

    public bool Delete(int id)
    {
        var all = GetAll();
        if (all.RemoveAll(x => GetId(x) == id) == 0)
            return false;
        Save(all);
        return true;
    }

    private void Save(List<T> all) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(all, Options));

    private static PropertyInfo IdProperty =>
        typeof(T).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(typeof(T).Name + " has no Id property.");

    private static int GetId(T item) => (int)(IdProperty.GetValue(item) ?? 0);

    private static void SetId(T item, int id) => IdProperty.SetValue(item, id);
}
""";
}
