namespace AppScaffolding.Auth;

/// <summary>Emits <c>Auth/Session.cs</c>: who is signed in, for a DESKTOP application.</summary>
/// <remarks>
/// Emitted by the WPF and Windows Forms targets only. A static current-user is exactly right in a
/// process that serves one person and exactly wrong in a server that serves many — Blazor holds
/// the signed-in user in an authentication cookie instead, and never receives this file.
/// </remarks>
public static class SessionTemplate
{
    /// <summary>Source of <c>Auth/Session.cs</c> in <paramref name="rootNamespace"/>.Auth.</summary>
    public static string Source(string rootNamespace) => $$"""
using {{rootNamespace}}.Models;

namespace {{rootNamespace}}.Auth;

/// <summary>The signed-in user for this desktop process.</summary>
public static class Session
{
    /// <summary>The signed-in user, or null before a sign-in.</summary>
    public static User? Current { get; private set; }

    /// <summary>Whether the shell closed because the user asked to sign out, rather than to quit.
    /// The entry point reads this to decide whether to show the login again.</summary>
    public static bool SignOutRequested { get; private set; }

    /// <summary>Whether the signed-in user may reach the Users screen and delete master records.</summary>
    public static bool IsAdmin => Current is { Role: AppRole.Admin };

    /// <summary>The name to show in the shell header. Falls back to the user name, because a
    /// blank name in the header reads as a broken sign-in.</summary>
    public static string DisplayName
        => Current is null
            ? string.Empty
            : Current.DisplayName.Length > 0 ? Current.DisplayName : Current.UserName;

    public static void SignIn(User user)
    {
        Current = user;
        SignOutRequested = false;
    }

    public static void RequestSignOut() => SignOutRequested = true;

    public static void Clear()
    {
        Current = null;
        SignOutRequested = false;
    }
}
""";
}
