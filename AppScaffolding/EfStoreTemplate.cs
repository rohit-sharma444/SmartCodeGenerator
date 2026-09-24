namespace AppScaffolding;

/// <summary>Emits <c>Data/EfStore.cs</c>: the EF Core + SQLite implementation of
/// <see cref="IStore{T}"/>. Deliberately faithful to JsonStore's contract — client-assigned int
/// Ids and whole-store SaveAll semantics — so Repository and SeedData need no behavioral change.</summary>
/// <remarks>
/// The permission guard this emits into <c>SaveAll</c> cannot refuse anything as the tool stands,
/// and a reader should not assume otherwise: sqlite is refused for the WinForms and WPF targets,
/// so <c>AppScaffolder</c>'s <c>sessionAuth</c>, which needs <c>HasDesktopSession</c>, is false
/// for every EF build. <see cref="ActingUserTemplate"/>'s emitted <c>ActingUser.Role()</c> is
/// therefore always the null-returning half, and a null role is allowed. The guard is emitted
/// anyway so that a target which gains both a user and sqlite is already covered on this path.
/// </remarks>
public static class EfStoreTemplate
{
    /// <summary>Source of <c>Data/EfStore.cs</c> in <paramref name="rootNamespace"/>.Data. Unlike
    /// <see cref="JsonStoreTemplate.Source"/>, this parameter already IS the root: EfStore lives
    /// directly in <c>{root}.Data</c>, never behind a caller-supplied full namespace.</summary>
    /// <param name="auth">
    /// Unused: kept so every existing caller of this overload compiles unchanged.
    /// <see cref="ActingUserTemplate"/> now decides, from its own <c>sessionAuth</c>, whether
    /// <c>ActingUser.Role()</c> reads a real session or returns null — this template no longer
    /// needs to ask.
    /// </param>
    public static string Source(string rootNamespace, bool auth = false)
    {
        return $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
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
        if (!Permissions.Allows(ValidationOperation.Update, typeof(T).Name, ActingUser.Role()))
            return new StoreWrite(false, Permissions.Refusal(ValidationOperation.Update, typeof(T).Name));

        return WriteToDatabase(all);
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
    public StoreWrite SaveAllUnchecked(List<T> all) => WriteToDatabase(all);

    private static StoreWrite WriteToDatabase(List<T> all)
    {
        try
        {
            using var context = new AppDbContext();
            var set = context.Set<T>();
            var existing = set.ToList();

            StampAuditFields(all, existing);

            set.RemoveRange(existing);
            set.AddRange(all);
            context.SaveChanges();
            return StoreWrite.Ok;
        }
        catch (DbUpdateException)
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
}
