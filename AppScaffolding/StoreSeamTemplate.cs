namespace AppScaffolding;

/// <summary>Emits the persistence seam shared by the JSON and EF Core stores.</summary>
public static class StoreSeamTemplate
{
    /// <summary>Source of <c>Data/IStore.cs</c> in <paramref name="projectName"/>.Data.</summary>
    public static string Source(string projectName) => $$"""
using System.Collections.Generic;

namespace {{projectName}}.Data;

/// <summary>Per-type list store keyed by an int Id. Implemented by JsonStore and EfStore, so the
/// front-end persistence code is identical whichever backend a build selects.</summary>
public interface IStore<T> where T : new()
{
    /// <summary>Every stored record.</summary>
    List<T> GetAll();

    /// <summary>Overwrites the store with <paramref name="all"/>. False if it could not be written.</summary>
    bool SaveAll(List<T> all);

    /// <summary>One past the highest Id in <paramref name="existing"/>, or 1 when there are none.</summary>
    int NextId(IEnumerable<T> existing);

    /// <summary>A new record with its Id already assigned.</summary>
    T NewItem(IEnumerable<T> existing);
}
""";
}
