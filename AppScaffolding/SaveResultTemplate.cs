namespace AppScaffolding;

/// <summary>Emits <c>Data/SaveResult.cs</c>: what a validating write returns.</summary>
public static class SaveResultTemplate
{
    /// <summary>Source of <c>Data/SaveResult.cs</c> in <paramref name="rootNamespace"/>.Data.</summary>
    public static string Source(string rootNamespace) => $$"""
using System;
using System.Collections.Generic;
using {{rootNamespace}}.Validation;

namespace {{rootNamespace}}.Data;

/// <summary>The outcome of a validating write.</summary>
/// <remarks>
/// NotFound is kept distinct from Errors: an Update naming an unknown Id is a different failure
/// from an Update carrying an invalid record, and the Web API front-end maps them to 404 and 400
/// respectively.
/// </remarks>
public sealed class SaveResult<T>
{
    /// <summary>True when the record validated and was persisted.</summary>
    public bool Success { get; init; }

    /// <summary>True when no record with this Id exists. Set by Update and by Delete.</summary>
    public bool NotFound { get; init; }

    /// <summary>The persisted record, or null when the write did not happen.</summary>
    public T? Item { get; init; }

    /// <summary>Human-readable validation messages. Empty when Success is true.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>Every broken rule with the property it belongs to. Empty when Success is true.</summary>
    /// <remarks>
    /// Alongside <see cref="Errors"/> rather than replacing it: a front end that only prints
    /// sentences should not have to unpack a record to do so.
    /// </remarks>
    public IReadOnlyList<ValidationError> Failures { get; init; } = Array.Empty<ValidationError>();

    /// <summary>The failures keyed by property, with whole-record errors under the empty key —
    /// the shape Results.ValidationProblem wants.</summary>
    public Dictionary<string, string[]> AsDictionary()
    {
        var byProperty = new Dictionary<string, List<string>>();

        foreach (var failure in Failures)
        {
            var key = failure.Property ?? string.Empty;

            if (!byProperty.TryGetValue(key, out var messages))
                byProperty[key] = messages = new List<string>();

            messages.Add(failure.Message);
        }

        // Repository does not populate Failures until the pipeline is wired into it (a later
        // step than this file); until then every validation failure still arrives only as
        // Errors. Without this fallback the Web API's 400 payload would come back empty on
        // every one of those failures, not just on a store failure that truly carries no
        // attribution. Keep it even after Repository catches up: a store failure (the write
        // itself throwing, say) still has only a sentence to offer, never a property.
        if (byProperty.Count == 0 && Errors is { Count: > 0 })
            byProperty[string.Empty] = new List<string>(Errors);

        var result = new Dictionary<string, string[]>();

        foreach (var pair in byProperty)
            result[pair.Key] = pair.Value.ToArray();

        return result;
    }
}
""";
}
