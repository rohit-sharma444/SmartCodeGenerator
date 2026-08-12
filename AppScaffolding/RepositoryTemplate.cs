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
    public static string Base(string appName) => Base(appName, validation: true);

    /// <summary>
    /// Source of <c>Data/Repository.cs</c> for <paramref name="appName"/>. With
    /// <paramref name="validation"/> false the Validation folder is not generated at all, so every
    /// mention of it has to go with it: the using, the field, both Validate calls, and the two doc
    /// comments that promise validation.
    /// </summary>
    /// <remarks>
    /// What does NOT change is the surface: Create and Update still return a
    /// <c>SaveResult&lt;T&gt;</c> carrying Success and Errors, so every caller — the console menu,
    /// the Web API's ValidationProblem — is untouched by the flag. Only the errors a store failure
    /// produces reach them now.
    /// </remarks>
    public static string Base(string appName, bool validation)
    {
        // Raw string literals, not "\n" concatenation: a raw literal carries the line endings of
        // the source file it is written in, which is exactly what the template below carries. These
        // three fragments are lines lifted OUT of that template, so anything else would leave the
        // default output one CR per line different from what this generator has always produced.
        var validationUsing = validation
            ? $"""
            using {appName}.Validation;

            """
            : string.Empty;

        var validatorField = validation
            ? """
                private readonly IValidator<T> _validator = ValidatorFactory.Create<T>();

            """
            : string.Empty;

        // Two blank lines before the closing delimiter, so the fragment ends with the blank line
        // that separates the guard from the body — the last newline of a raw literal is its
        // delimiter's, and is not part of the value.
        var validateCall = validation
            ? """
                    var errors = _validator.Validate(item);
                    if (errors.Count > 0)
                        return new SaveResult<T> { Success = false, Errors = errors };


            """
            : string.Empty;

        var createSummary = validation
            ? "Validates, then assigns the next Id, appends the record and persists it."
            : "Assigns the next Id, appends the record and persists it.";

        var updateSummary = validation
            ? "Validates, then replaces the record sharing this item's Id."
            : "Replaces the record sharing this item's Id.";

        return $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
{{validationUsing}}
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
{{validatorField}}
    /// <summary>Every stored record.</summary>
    public List<T> GetAll() => _store.GetAll();

    /// <summary>The record with this Id, or null when there is none.</summary>
    public T? GetById(int id) => _store.GetAll().FirstOrDefault(item => GetId(item) == id);

    /// <summary>{{createSummary}}</summary>
    public SaveResult<T> Create(T item)
    {
{{validateCall}}        var all = _store.GetAll();
        SetId(item, _store.NextId(all));
        all.Add(item);

        if (!_store.SaveAll(all))
            return new SaveResult<T> { Success = false, Errors = new[] { "Could not persist " + typeof(T).Name + "." } };

        return new SaveResult<T> { Success = true, Item = item };
    }

    /// <summary>{{updateSummary}}</summary>
    public SaveResult<T> Update(T item)
    {
{{validateCall}}        var all = _store.GetAll();
        var index = all.FindIndex(existing => GetId(existing) == GetId(item));
        if (index < 0)
            return new SaveResult<T> { Success = false, NotFound = true };

        all[index] = item;

        if (!_store.SaveAll(all))
            return new SaveResult<T> { Success = false, Errors = new[] { "Could not persist " + typeof(T).Name + "." } };

        return new SaveResult<T> { Success = true, Item = item };
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
    }

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
