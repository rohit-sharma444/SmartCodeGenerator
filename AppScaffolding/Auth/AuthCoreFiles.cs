namespace AppScaffolding.Auth;

/// <summary>
/// The one list of shared auth-core files, so the scaffolder and the targets cannot drift.
/// </summary>
/// <remarks>
/// These are target-agnostic: all three UI targets receive exactly these, and only the login VIEW
/// differs. <see cref="SessionTemplate"/> and <see cref="LoginViewModelTemplate"/> are deliberately
/// NOT here — they are desktop-only, because a static signed-in user in a server process would be
/// shared by every visitor.
/// </remarks>
public static class AuthCoreFiles
{
    /// <summary>The emitted entity name. Emitted, not discovered: it must not reach the class
    /// count, the navigation list, the seeded rows or ValidatorFactory.</summary>
    public const string EntityName = "User";

    /// <summary>The names the auth core occupies, which a discovered entity may not reuse.</summary>
    /// <remarks>
    /// Passed to <c>DomainModelMapper.ToClassDefinitions</c> only when the app has a login: with
    /// `nologin` there is no auth model, so nothing collides and the entity keeps its own name.
    /// </remarks>
    public static IReadOnlyCollection<string> ReservedNames { get; } = [EntityName];

    /// <summary>The shared auth files for an app rooted at <paramref name="rootNamespace"/>.</summary>
    public static IReadOnlyList<GeneratedFile> For(string rootNamespace) =>
    [
        new("Models", "User.cs", AuthModelTemplates.User(rootNamespace)),
        new("Auth", "AppRole.cs", AuthModelTemplates.Role(rootNamespace)),
        new("Auth", "PasswordHasher.cs", PasswordHasherTemplate.Source(rootNamespace)),
        new("Auth", "AuthService.cs", AuthServiceTemplate.Source(rootNamespace)),
        new("Auth", "UserSeed.cs", UserSeedTemplate.Source(rootNamespace)),
    ];
}
