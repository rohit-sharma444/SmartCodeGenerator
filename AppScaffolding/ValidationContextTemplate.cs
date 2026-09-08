namespace AppScaffolding;

/// <summary>Emits <c>Validation/ValidationContext.cs</c>: what a stage is given to judge.</summary>
/// <remarks>
/// The context carries the acting user's ROLE and ID rather than the app's own <c>User</c> type.
/// <c>AuthCoreFiles</c> emits that type only when the auth core is emitted — a login AND a target
/// with screens — so the console, webapi, worker and classlib targets have no such type while
/// still generating this folder. Two primitives compile in all seven targets and carry everything
/// the permission matrix reads.
/// </remarks>
public static class ValidationContextTemplate
{
    /// <summary>Source of <c>Validation/ValidationContext.cs</c> in
    /// <paramref name="rootNamespace"/>.Validation.</summary>
    public static string Source(string rootNamespace) => $$"""
using System.Collections.Generic;

namespace {{rootNamespace}}.Validation;

/// <summary>Which write is being attempted.</summary>
/// <remarks>
/// Post has no caller yet: the posting gate over a header and its lines arrives with the
/// Transaction stage. It is declared now so the enum a generated app exposes does not widen later.
/// </remarks>
public enum ValidationOperation
{
    Insert,
    Update,
    Delete,
    Post
}

/// <summary>One broken rule. A null <see cref="Property"/> means the record as a whole.</summary>
public sealed record ValidationError(string? Property, string Message);

/// <summary>Everything a run of the pipeline found.</summary>
public sealed record ValidationOutcome(IReadOnlyList<ValidationError> Errors)
{
    /// <summary>An outcome with nothing in it.</summary>
    public static ValidationOutcome Valid { get; } = new(new List<ValidationError>());

    /// <summary>True when no rule was broken.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>Just the sentences, for a caller that only prints them.</summary>
    public IReadOnlyList<string> Messages
    {
        get
        {
            var messages = new List<string>(Errors.Count);

            foreach (var error in Errors)
                messages.Add(error.Message);

            return messages;
        }
    }
}

/// <summary>
/// What one validation is given: the record as submitted, which operation is being attempted,
/// what is on disk now, a lookup for other records, and who is acting.
/// </summary>
/// <param name="Stored">
/// What is on disk now. Null on Insert, and null wherever a caller cannot supply it — a stage
/// that needs it must handle null rather than assume an Update always has one.
/// </param>
/// <param name="UserRole">
/// The acting user's role, or null when this app has no login at all. Null means "no notion of
/// who is acting", which the permission matrix reads as allowed — an app generated without a
/// login has nobody to refuse.
/// </param>
public sealed record ValidationContext<T>(
    T Item,
    ValidationOperation Op,
    T? Stored,
    IEntityLookup Lookup,
    string? UserRole,
    int? UserId)
{
    /// <summary>A context with no stored version, no user and a lookup that finds nothing.
    /// For tests and for a caller that genuinely has none of those.</summary>
    public static ValidationContext<T> For(T item, ValidationOperation op, IEntityLookup lookup)
        => new(item, op, default, lookup, null, null);
}
""";
}
