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
        => Base(appName, validation, logging: false);

    /// <summary>
    /// As <see cref="Base(string, bool)"/>, additionally logging every write through
    /// <c>AppLog</c> when <paramref name="logging"/>.
    /// </summary>
    /// <remarks>
    /// This is the app's only write path, which is what makes it the place worth logging: the
    /// desktop view models save through the store directly, but everything else — console menu,
    /// Web API, worker, generated tests — goes through here.
    /// </remarks>
    public static string Base(string appName, bool validation, bool logging)
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
        var validateCall = (validation, logging) switch
        {
            (false, _) => string.Empty,

            // A rejected write is worth a line in the log: it is the one outcome that is neither a
            // success nor an exception, so nothing else would record it.
            (true, true) => """
                    var errors = _validator.Validate(item);
                    if (errors.Count > 0)
                    {
                        Log.LogWarning("Rejected {Entity}: {Error}", typeof(T).Name, errors[0]);
                        return new SaveResult<T> { Success = false, Errors = errors };
                    }


            """,

            _ => """
                    var errors = _validator.Validate(item);
                    if (errors.Count > 0)
                        return new SaveResult<T> { Success = false, Errors = errors };


            """,
        };

        var loggingUsings = logging
            ? $"""

              using Microsoft.Extensions.Logging;
              using {appName}.Logging;
              """
            : string.Empty;

        var loggerField = logging
            ? """
                private static readonly ILogger Log = AppLog.For<Repository<T>>();

            """
            : string.Empty;

        var createLog = logging
            ? """
                    Log.LogInformation("Created {Entity} {Id}.", typeof(T).Name, GetId(item));


            """
            : string.Empty;

        var updateLog = logging
            ? """
                    Log.LogInformation("Updated {Entity} {Id}.", typeof(T).Name, GetId(item));


            """
            : string.Empty;

        // Delete is the one method whose tail has to change rather than gain a line: it returns the
        // save result directly, and there is nothing to log against until that result is known.
        //
        // The logging branch is untouched by Task 7 on purpose: it is exercised only alongside
        // validation (no test or generated app turns logging on without it), and validation is
        // what Task 8 rewrites here wholesale -- fixing only this branch's own SaveAll typing
        // would still leave it uncompilable for the same pre-existing reason, so it is left
        // exactly as Task 8 will find it rather than half-migrated.
        var deleteTail = logging
            ? """
                    var saved = _store.SaveAll(all);

                    if (saved)
                        Log.LogInformation("Deleted {Entity} {Id}.", typeof(T).Name, id);

                    return saved;
            """
            // Delete's own signature is unchanged in this task (Task 8 makes it a SaveResult<T>),
            // so this still has only a bool to hand back -- Success, with no room yet for Refusal.
            : """
                    return _store.SaveAll(all).Success;
            """;

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
using System.Reflection;{{loggingUsings}}
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
{{validatorField}}{{loggerField}}
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

        var write = _store.SaveAll(all);
        if (!write.Success)
            return new SaveResult<T> { Success = false, Errors = new[] { write.Refusal ?? "Could not persist " + typeof(T).Name + "." } };

{{createLog}}        return new SaveResult<T> { Success = true, Item = item };
    }

    /// <summary>{{updateSummary}}</summary>
    public SaveResult<T> Update(T item)
    {
{{validateCall}}        var all = _store.GetAll();
        var index = all.FindIndex(existing => GetId(existing) == GetId(item));
        if (index < 0)
            return new SaveResult<T> { Success = false, NotFound = true };

        all[index] = item;

        var write = _store.SaveAll(all);
        if (!write.Success)
            return new SaveResult<T> { Success = false, Errors = new[] { write.Refusal ?? "Could not persist " + typeof(T).Name + "." } };

{{updateLog}}        return new SaveResult<T> { Success = true, Item = item };
    }

    /// <summary>Removes the record with this Id. False when there is none.</summary>
    public bool Delete(int id)
    {
        var all = _store.GetAll();
        if (all.RemoveAll(item => GetId(item) == id) == 0)
            return false;

{{deleteTail}}
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
