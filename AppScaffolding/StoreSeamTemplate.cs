namespace AppScaffolding;

/// <summary>Emits the persistence seam shared by the JSON and EF Core stores.</summary>
public static class StoreSeamTemplate
{
    /// <summary>Source of <c>Data/IStore.cs</c> in <paramref name="rootNamespace"/>.Data.</summary>
    public static string Source(string rootNamespace) => $$"""
using System.Collections.Generic;

namespace {{rootNamespace}}.Data;

/// <summary>The outcome of a whole-store write.</summary>
/// <remarks>
/// A bool could not distinguish "the file would not write" from "this user may not write this
/// type", and the second surfaced as the first — a message naming a file that was perfectly fine.
/// </remarks>
public sealed record StoreWrite(bool Success, string? Refusal)
{
    /// <summary>A write that went through.</summary>
    public static StoreWrite Ok { get; } = new(true, null);
}

/// <summary>Per-type list store keyed by an int Id. Implemented by JsonStore and EfStore, so the
/// front-end persistence code is identical whichever backend a build selects.</summary>
public interface IStore<T> where T : new()
{
    /// <summary>Every stored record.</summary>
    List<T> GetAll();

    /// <summary>Overwrites the store with <paramref name="all"/>.</summary>
    StoreWrite SaveAll(List<T> all);

    /// <summary>
    /// As <see cref="SaveAll"/>, but exempt from the permission check. The legitimate callers are
    /// UserSeed.EnsureAdminSeeded and SeedData.Seed, both of which write before anyone could
    /// possibly be signed in to authorise it -- the app's own startup seeding, not a caller
    /// acting on a signed-out user's behalf.
    /// </summary>
    StoreWrite SaveAllUnchecked(List<T> all);

    /// <summary>One past the highest Id in <paramref name="existing"/>, or 1 when there are none.</summary>
    int NextId(IEnumerable<T> existing);

    /// <summary>A new record with its Id already assigned.</summary>
    T NewItem(IEnumerable<T> existing);
}
""";
}
