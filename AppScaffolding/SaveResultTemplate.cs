namespace AppScaffolding;

/// <summary>Emits <c>Data/SaveResult.cs</c>: what a validating write returns.</summary>
public static class SaveResultTemplate
{
    /// <summary>Source of <c>Data/SaveResult.cs</c> in <paramref name="projectName"/>.Data.</summary>
    public static string Source(string projectName) => $$"""
using System;
using System.Collections.Generic;

namespace {{projectName}}.Data;

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

    /// <summary>True when no record with this Id exists. Only ever set by Update.</summary>
    public bool NotFound { get; init; }

    /// <summary>The persisted record, or null when the write did not happen.</summary>
    public T? Item { get; init; }

    /// <summary>Human-readable validation messages. Empty when Success is true.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>Every message under a single empty key, the shape Results.ValidationProblem wants.
    /// Messages are whole sentences rather than per-property, so they surface as top-level errors
    /// rather than being attributed to individual fields.</summary>
    public Dictionary<string, string[]> AsDictionary() =>
        new() { [string.Empty] = Errors is null ? Array.Empty<string>() : [.. Errors] };
}
""";
}
