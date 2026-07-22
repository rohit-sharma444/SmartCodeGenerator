namespace AppScaffolding;

/// <summary>Emits <c>Data/EfStore.cs</c>: the EF Core + SQLite implementation of
/// <see cref="IStore{T}"/>. Deliberately faithful to JsonStore's contract — client-assigned int
/// Ids and whole-store SaveAll semantics — so Repository and SeedData need no behavioral change.</summary>
public static class EfStoreTemplate
{
    /// <summary>Source of <c>Data/EfStore.cs</c> in <paramref name="projectName"/>.Data.</summary>
    public static string Source(string projectName) => $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace {{projectName}}.Data;

/// <summary>SQLite-backed store keyed by an int Id. One table per type. A fresh context per call
/// mirrors JsonStore's stateless per-call file access.</summary>
public sealed class EfStore<T> : IStore<T> where T : class, new()
{
    /// <summary>Every stored record.</summary>
    public List<T> GetAll()
    {
        using var context = new AppDbContext();
        return context.Set<T>().AsNoTracking().ToList();
    }

    /// <summary>Overwrites the store with <paramref name="all"/> (clear then insert), matching
    /// JsonStore.SaveAll. False if the write failed.</summary>
    public bool SaveAll(List<T> all)
    {
        try
        {
            using var context = new AppDbContext();
            var set = context.Set<T>();
            set.RemoveRange(set.ToList());
            set.AddRange(all);
            context.SaveChanges();
            return true;
        }
        catch (DbUpdateException)
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
