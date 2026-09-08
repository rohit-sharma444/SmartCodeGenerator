namespace AppScaffolding.Auth;

/// <summary>Emits <c>Auth/AuthService.cs</c>: sign-in and change-password over the user store.</summary>
/// <remarks>
/// Static, like <c>SeedData</c> and <c>StoreFactory</c>, because a generated app has no container
/// to resolve a service from.
/// </remarks>
public static class AuthServiceTemplate
{
    /// <summary>Source of <c>Auth/AuthService.cs</c> in <paramref name="rootNamespace"/>.Auth.</summary>
    public static string Source(string rootNamespace) => $$"""
using System;
using System.Linq;
using {{rootNamespace}}.Data;
using {{rootNamespace}}.Models;

namespace {{rootNamespace}}.Auth;

/// <summary>Why a sign-in attempt ended the way it did. Kept apart from the message the screen
/// shows, which is the same for every failure.</summary>
public enum SignInOutcome
{
    Succeeded,
    UnknownUser,
    WrongPassword,
    Disabled
}

/// <summary>The result of one sign-in attempt.</summary>
/// <remarks>
/// Three failures collapse into one message on purpose. "No such user" and "wrong password" as
/// separate messages tell anyone who asks which user names exist.
/// </remarks>
public sealed record SignInResult(SignInOutcome Outcome, User? User)
{
    /// <summary>The only thing a failed sign-in ever tells the person at the keyboard.</summary>
    public const string FailureMessage = "User name or password not recognised.";

    public bool Succeeded => Outcome == SignInOutcome.Succeeded;

    /// <summary>The message to show, or null when the sign-in worked.</summary>
    public string? Message => Succeeded ? null : FailureMessage;
}

/// <summary>Signs users in and changes their passwords.</summary>
public static class AuthService
{
    /// <summary>Attempts a sign-in. Never throws for bad input.</summary>
    public static SignInResult SignIn(string userName, string password)
    {
        var user = StoreFactory.Create<User>().GetAll()
            .FirstOrDefault(u => string.Equals(
                u.UserName, userName ?? string.Empty, StringComparison.OrdinalIgnoreCase));

        if (user is null)
            return new SignInResult(SignInOutcome.UnknownUser, null);

        if (!user.IsActive)
            return new SignInResult(SignInOutcome.Disabled, null);

        if (!PasswordHasher.Verify(password ?? string.Empty, user.PasswordHash, user.PasswordSalt))
            return new SignInResult(SignInOutcome.WrongPassword, null);

        return new SignInResult(SignInOutcome.Succeeded, user);
    }

    /// <summary>Replaces the password of the account with <paramref name="userId"/>. False when
    /// there is no such account or the store could not be written.</summary>
    public static bool ChangePassword(int userId, string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword))
            return false;

        var store = StoreFactory.Create<User>();
        var all = store.GetAll();
        var user = all.FirstOrDefault(u => u.Id == userId);

        if (user is null)
            return false;

        var (hash, salt) = PasswordHasher.Create(newPassword);

        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.ModifiedOn = DateTime.Now;

        return store.SaveAll(all).Success;
    }
}
""";
}
