namespace AppScaffolding.Auth;

/// <summary>Emits <c>Auth/UserSeed.cs</c>: the account that makes the first run possible.</summary>
/// <remarks>
/// Separate from <c>SeedData</c>, and it runs earlier. Entity seeding happens when the view models
/// load, which is now AFTER the login screen — an admin account seeded there would not exist yet
/// when it is needed.
/// </remarks>
public static class UserSeedTemplate
{
    /// <summary>The sentence the login screens show on a store with no accounts yet. Lives here
    /// so the view templates cannot name credentials the seeder does not create.</summary>
    public const string FirstRunHint = "First run: sign in as admin / admin, then change it.";

    /// <summary>Source of <c>Auth/UserSeed.cs</c> in <paramref name="rootNamespace"/>.Auth.</summary>
    public static string Source(string rootNamespace) => $$"""
using System;
using System.Linq;
using {{rootNamespace}}.Data;
using {{rootNamespace}}.Models;

namespace {{rootNamespace}}.Auth;

/// <summary>Creates the first administrator, once.</summary>
public static class UserSeed
{
    public const string DefaultUserName = "admin";
    public const string DefaultPassword = "admin";

    /// <summary>The hint the login screen shows while the default account is still in place.</summary>
    public const string FirstRunHint = "{{FirstRunHint}}";

    /// <summary>
    /// Creates the default administrator when the user store is empty, and does nothing otherwise.
    /// </summary>
    /// <remarks>
    /// The guard is "any account at all", not "no account called admin". Someone who renamed the
    /// administrator would otherwise get a fresh admin/admin back on the next start, which is a
    /// back door into an application that looked locked down.
    /// </remarks>
    public static void EnsureAdminSeeded()
    {
        var store = StoreFactory.Create<User>();
        var all = store.GetAll();

        if (all.Count > 0)
            return;

        var (hash, salt) = PasswordHasher.Create(DefaultPassword);
        var now = DateTime.Now;

        all.Add(new User
        {
            Id = store.NextId(all),
            UserName = DefaultUserName,
            DisplayName = "Administrator",
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = AppRole.Admin,
            IsActive = true,
            CreatedOn = now,
            ModifiedOn = now,
        });

        // Unchecked: nobody could possibly be signed in yet to authorise creating the very
        // account sign-in depends on. SaveAll's permission gate -- correctly, for every other
        // caller -- refuses a signed-out caller of a login app, which would otherwise make this
        // write refuse itself and leave a freshly generated login app impossible to sign into.
        store.SaveAllUnchecked(all);
    }

    /// <summary>Whether the default account is still signing in with its default password, so a
    /// screen can say so.</summary>
    public static bool IsFirstRun()
        => AuthService.SignIn(DefaultUserName, DefaultPassword).Succeeded;
}
""";
}
