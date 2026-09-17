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
        => Base(appName, validation, logging, auth: false);

    /// <summary>
    /// As <see cref="Base(string, bool, bool)"/>.
    /// </summary>
    /// <param name="auth">
    /// Unused: kept so every existing caller of this overload compiles unchanged.
    /// <see cref="ActingUserTemplate"/> now decides, from its own <c>sessionAuth</c>, whether
    /// <c>ActingUser.Role()</c> and <c>ActingUser.Id()</c> read a real desktop <c>Session</c> or
    /// return null — this template no longer needs to ask. That flag is narrower than "this app
    /// has a login": <c>Session</c> is emitted from exactly one place, <c>DesktopViewModelFiles</c>,
    /// which only the WinForms and WPF targets reach — see <c>ITargetScaffolder.HasDesktopSession</c>.
    /// A Blazor app has a login, has no Session, and would not compile if this named one; its own
    /// delete is guarded at the grid instead.
    /// </param>
    public static string Base(string appName, bool validation, bool logging, bool auth)
    {
        // Raw string literals, not "\n" concatenation: a raw literal carries the line endings of
        // the source file it is written in, which is exactly what the template below carries. These
        // fragments are lines lifted OUT of that template, so anything else would leave the
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

        // What every write does when a stage said no: only what each puts in the context differs.
        // A rejected write is worth a line in the log — it is the one outcome that is neither a
        // success nor an exception, so nothing else would record it.
        //
        // The three fragments below splice this in at the MARGIN, because an interpolation does
        // not re-indent the lines of a multi-line value: the indentation these lines need in the
        // emitted file is baked in here.
        var refuse = logging
            ? """
                    if (!outcome.IsValid)
                    {
                        var messages = outcome.Messages;

                        Log.LogWarning("Rejected {Entity}: {Error}", typeof(T).Name, messages[0]);
                        return new SaveResult<T> { Success = false, Failures = outcome.Errors, Errors = messages };
                    }
            """
            : """
                    if (!outcome.IsValid)
                        return new SaveResult<T> { Success = false, Failures = outcome.Errors, Errors = outcome.Messages };
            """;

        // Two blank lines before the closing delimiter, so each fragment ends with the blank line
        // that separates the guard from the body — the last newline of a raw literal is its
        // delimiter's, and is not part of the value.
        var validateCreate = validation
            ? $$"""
                    var outcome = _validator.Validate(new ValidationContext<T>(
                        item, ValidationOperation.Insert, null, new StoreEntityLookup(),
                        ActingUser.Role(), ActingUser.Id()));

            {{refuse}}


            """
            : string.Empty;

        // Update reads what is on disk first: that is the whole point of the Stored field. An
        // immutable-field rule and a status transition are inexpressible without it.
        var validateUpdate = validation
            ? $$"""
                    var stored = GetById(GetId(item));

                    var outcome = _validator.Validate(new ValidationContext<T>(
                        item, ValidationOperation.Update, stored, new StoreEntityLookup(),
                        ActingUser.Role(), ActingUser.Id()));

            {{refuse}}


            """
            : string.Empty;

        // Delete judges the record that is about to go, not a submitted one — it is both the item
        // and the stored version. A record that is not there is NotFound, which is a different
        // outcome from a refusal and is deliberately not logged as one.
        var validateDelete = validation
            ? $$"""
                    var doomed = GetById(id);

                    if (doomed is null)
                        return new SaveResult<T> { Success = false, NotFound = true };

                    var outcome = _validator.Validate(new ValidationContext<T>(
                        doomed, ValidationOperation.Delete, doomed, new StoreEntityLookup(),
                        ActingUser.Role(), ActingUser.Id()));

            {{refuse}}


            """
            : string.Empty;

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

        // Delete's line, unlike Create's and Update's, waits inside the method rather than after
        // the write it reports: there is nothing to record until the save result is known.
        // Validation and logging are independent flags (validation: false, logging: true is a real
        // `novalidation logging` build), so this reaches Delete with no context built above it and
        // must compile and behave correctly on its own.
        var deleteLog = logging
            ? """
                    if (saved.Success)
                        Log.LogInformation("Deleted {Entity} {Id}.", typeof(T).Name, id);


            """
            : string.Empty;

        var createSummary = validation
            ? "Validates, then assigns the next Id, appends the record and persists it."
            : "Assigns the next Id, appends the record and persists it.";

        var updateSummary = validation
            ? "Validates, then replaces the record sharing this item's Id."
            : "Replaces the record sharing this item's Id.";

        // Neither summary promises a bool any more, and neither may say "false when there is
        // none": the result now separates a missing record from a refusal, and a refusal carries
        // its reason. Both are possible with validation off too — the store asks the permission
        // matrix on its own.
        var deleteSummary = validation
            ? "Validates the record about to go, then removes it, or says why not."
            : "Removes the record with this Id, or says why not.";

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
{{validateCreate}}        var all = _store.GetAll();
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
{{validateUpdate}}        var all = _store.GetAll();
        var index = all.FindIndex(existing => GetId(existing) == GetId(item));
        if (index < 0)
            return new SaveResult<T> { Success = false, NotFound = true };

        all[index] = item;

        var write = _store.SaveAll(all);
        if (!write.Success)
            return new SaveResult<T> { Success = false, Errors = new[] { write.Refusal ?? "Could not persist " + typeof(T).Name + "." } };

{{updateLog}}        return new SaveResult<T> { Success = true, Item = item };
    }

    /// <summary>{{deleteSummary}}</summary>
    public SaveResult<T> Delete(int id)
    {
{{validateDelete}}        var all = _store.GetAll();

        if (all.RemoveAll(item => GetId(item) == id) == 0)
            return new SaveResult<T> { Success = false, NotFound = true };

        var saved = _store.SaveAll(all);

{{deleteLog}}        return saved.Success
            ? new SaveResult<T> { Success = true }
            : new SaveResult<T> { Success = false, Errors = new[] { saved.Refusal ?? "Could not persist " + typeof(T).Name + "." } };
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
