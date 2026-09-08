namespace AppScaffolding;

/// <summary>Emits <c>Validation/Permissions.cs</c>: who may do what.</summary>
/// <remarks>
/// Emitted whether or not <c>novalidation</c> was given. Turning validation off must not turn
/// permissions off — that is the failure the requirement's own seventeenth item warns about.
/// <para>
/// Nothing here is inferred: permissions are in nobody's data. The default matrix reproduces what
/// the generated apps already displayed — an Admin may do anything, a User may do anything except
/// delete a master record or reach the Users screen — and the change is that it is now ENFORCED
/// rather than only shown.
/// </para>
/// </remarks>
public static class PermissionsTemplate
{
    /// <summary>Source of <c>Validation/Permissions.cs</c>, with
    /// <paramref name="masterEntityNames"/> from the build's own role classification.</summary>
    public static string Source(string rootNamespace, IReadOnlyList<string> masterEntityNames)
    {
        var masters = masterEntityNames.Count == 0
            ? string.Empty
            : string.Join(", ", masterEntityNames.Select(name => $"\"{name}\""));

        return $$"""
using System;
using System.Collections.Generic;

namespace {{rootNamespace}}.Validation;

/// <summary>Who may perform which operation on which entity.</summary>
public static class Permissions
{
    /// <summary>The role name that may do anything.</summary>
    private const string AdministratorRole = "Admin";

    /// <summary>The entity holding the app's own user accounts.</summary>
    private const string UserEntity = "User";

    /// <summary>The entities this build classified as masters, from the same classification the
    /// screens use — so a screen and its permission cannot disagree.</summary>
    private static readonly HashSet<string> Masters =
        new(new string[] { {{masters}} }, StringComparer.Ordinal);

    /// <summary>Whether <paramref name="role"/> may perform <paramref name="op"/> on
    /// <paramref name="entity"/>.</summary>
    /// <remarks>
    /// A null role means this app has no login at all — the auth core is emitted only for a
    /// target with screens — so there is no notion of who is acting and nothing to refuse.
    /// Returning false for a null role would refuse every write in every console, Web API and
    /// worker app.
    /// </remarks>
    public static bool Allows(ValidationOperation op, string entity, string? role)
    {
        if (role is null)
            return true;

        if (string.Equals(role, AdministratorRole, StringComparison.Ordinal))
            return true;

        if (string.Equals(entity, UserEntity, StringComparison.Ordinal))
            return false;

        return op != ValidationOperation.Delete || !Masters.Contains(entity);
    }

    /// <summary>The sentence shown when <see cref="Allows"/> said no.</summary>
    /// <remarks>
    /// Names the operation and the entity and NOTHING about the record's fields. The Security
    /// stage runs first so that an unauthorised user is refused without being told which fields
    /// were wrong, and a message leaking one would undo that.
    /// </remarks>
    public static string Refusal(ValidationOperation op, string entity)
        => $"You are not permitted to {op.ToString().ToLowerInvariant()} a {entity} record.";
}
""";
    }
}
