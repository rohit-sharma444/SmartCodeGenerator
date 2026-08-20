namespace AppScaffolding.Auth;

/// <summary>Emits the two types the auth core stores: the role enum and the user record.</summary>
/// <remarks>
/// <c>User</c> goes into <c>Models/</c> beside the discovered classes and persists through
/// <c>IStore&lt;User&gt;</c>, so JSON and SQLite both work with no new persistence code. It is
/// EMITTED, not DISCOVERED: it must never reach the "Generated N class(es)" summary, the
/// navigation list, the seeded sample rows or ValidatorFactory.
/// <para>
/// The audit fields are present because <c>DomainModelMapper.AddAuditFields</c> gives every
/// discovered class the same pair, and a User that lacked them would be the one model in the
/// folder with a different shape.
/// </para>
/// </remarks>
public static class AuthModelTemplates
{
    /// <summary>Source of <c>Auth/AppRole.cs</c>.</summary>
    public static string Role(string rootNamespace) => $$"""
namespace {{rootNamespace}}.Auth;

/// <summary>What a signed-in user may do. Exactly two values: an Admin reaches the Users screen
/// and may delete master records; a User may do everything else.</summary>
public enum AppRole
{
    Admin,
    User
}
""";

    /// <summary>Source of <c>Models/User.cs</c>.</summary>
    public static string User(string rootNamespace) => $$"""
using System;
using {{rootNamespace}}.Auth;

namespace {{rootNamespace}}.Models;

/// <summary>An account that can sign in. The password is never stored — only a PBKDF2 hash and
/// the salt it was made with.</summary>
public class User
{
    public int Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string PasswordSalt { get; set; } = string.Empty;

    public AppRole Role { get; set; } = AppRole.User;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedOn { get; set; }

    public DateTime ModifiedOn { get; set; }
}
""";
}
