namespace AppScaffolding;

/// <summary>Emits <c>Data/EfStore.cs</c>: the EF Core + SQLite implementation of
/// <see cref="IStore{T}"/>. Deliberately faithful to JsonStore's contract — client-assigned int
/// Ids and whole-store SaveAll semantics — so Repository and SeedData need no behavioral change.</summary>
public static class EfStoreTemplate
{
    /// <summary>Source of <c>Data/EfStore.cs</c> in <paramref name="rootNamespace"/>.Data. Unlike
    /// <see cref="JsonStoreTemplate.Source"/>, this parameter already IS the root: EfStore lives
    /// directly in <c>{root}.Data</c>, never behind a caller-supplied full namespace.</summary>
    public static string Source(string rootNamespace, bool auth = false)
    {
        // Task 8 adds the same pair, plus CurrentUserId(), to Repository<T>. Duplicated rather
        // than shared because the store and the repository are emitted into different folders and
        // neither may depend on the other.
        var currentUser = auth
            ? $$"""
                private static string? CurrentRole() => {{rootNamespace}}.Auth.Session.Current?.Role.ToString();

            """
            : """
                // No login in this app, so there is nobody to attribute a write to. Not a
                // stub to fill in later: a target without screens emits no auth core.
                private static string? CurrentRole() => null;

            """;

        return $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using {{rootNamespace}}.Validation;

namespace {{rootNamespace}}.Data;

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
    /// JsonStore.SaveAll.</summary>
    /// <remarks>
    /// The coarse enforcement point, and the only one no front end can go round. SaveAll is handed
    /// a whole list, so it cannot tell an insert from a delete and can only ask whether this user
    /// may write this type at all. The operation-aware checks live in Repository and at the
    /// grids' delete buttons. Under the default matrix this refuses nothing except the app's own
    /// User records.
    /// </remarks>
    public StoreWrite SaveAll(List<T> all)
    {
        if (!Permissions.Allows(ValidationOperation.Update, typeof(T).Name, CurrentRole()))
            return new StoreWrite(false, Permissions.Refusal(ValidationOperation.Update, typeof(T).Name));

        try
        {
            using var context = new AppDbContext();
            var set = context.Set<T>();
            set.RemoveRange(set.ToList());
            set.AddRange(all);
            context.SaveChanges();
            return StoreWrite.Ok;
        }
        catch (DbUpdateException)
        {
            return new StoreWrite(false, null);
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

{{currentUser}}}
""";
    }
}
