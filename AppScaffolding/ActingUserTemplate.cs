namespace AppScaffolding;

/// <summary>Emits <c>Validation/ActingUser.cs</c>: who is acting, in one place.</summary>
/// <remarks>
/// Five templates used to emit their own <c>CurrentRole()</c> in three different shapes. They
/// agreed, but nothing held them together, and every new caller was a sixth chance to disagree.
/// </remarks>
public static class ActingUserTemplate
{
    /// <summary>
    /// Source of <c>Validation/ActingUser.cs</c> in <paramref name="rootNamespace"/>.Validation.
    /// </summary>
    /// <param name="sessionAuth">
    /// <c>AppScaffolder</c>'s own flag of that name (<c>auth &amp;&amp; target.HasDesktopSession</c>),
    /// not the broader <c>auth</c>/<c>login</c>: a Blazor app has a login and no desktop
    /// <c>Session</c> at all, so naming one here for every app with a login would emit code that
    /// does not compile.
    /// </param>
    public static string Source(string rootNamespace, bool sessionAuth)
        => Source(rootNamespace, sessionAuth, login: sessionAuth);

    /// <summary>
    /// As <see cref="Source(string, bool)"/>, telling apart the two apps that have no desktop
    /// <c>Session</c>: one with no login at all, and a Blazor app, which has a login.
    /// </summary>
    /// <param name="login">
    /// Whether the app has a login at all (<c>AppScaffolder</c>'s <c>auth</c>). With a login and
    /// no Session, nobody is acting until a caller says who through <c>ActingUser.ActAs</c>. That
    /// is what lets a Blazor app's generated tests act as an administrator: before it existed
    /// they had no way to say who was acting, so <c>Permissions.Allows</c> read the null role as
    /// "signed out" and refused every write they made.
    /// </param>
    public static string Source(string rootNamespace, bool sessionAuth, bool login) => sessionAuth
        ? $$"""
using {{rootNamespace}}.Auth;

namespace {{rootNamespace}}.Validation;

/// <summary>Who is acting, for the context every validation carries.</summary>
internal static class ActingUser
{
    /// <summary>The signed-in user's role, or null before a sign-in and after a sign-out.</summary>
    public static string? Role() => Session.Current?.Role.ToString();

    /// <summary>The signed-in user's id, or null when nobody is signed in.</summary>
    public static int? Id() => Session.Current?.Id;
}
"""
        : login
        ? $$"""
using System;
using System.Threading;

namespace {{rootNamespace}}.Validation;

/// <summary>Who is acting, for the context every validation carries.</summary>
/// <remarks>
/// This app has a login but no single desktop user: a server serves many people at once, and each
/// one's sign-in lives in that circuit's authentication state, which the screens read for
/// themselves. Anything else that writes through Repository says who is acting, for one scope, with
/// <see cref="ActAs"/>. Until it does, nobody is — and an app with a login refuses a write from
/// nobody. The value is an AsyncLocal, so one caller's scope never leaks into another's request.
/// </remarks>
public static class ActingUser
{
    private static readonly AsyncLocal<Actor?> Current = new();

    /// <summary>The role named by the innermost <see cref="ActAs"/> scope, or null outside one.</summary>
    public static string? Role() => Current.Value?.Role;

    /// <summary>The user id named by the innermost <see cref="ActAs"/> scope, or null outside one.</summary>
    public static int? Id() => Current.Value?.Id;

    /// <summary>Acts as <paramref name="role"/> until the returned scope is disposed, then goes
    /// back to whoever was acting before.</summary>
    public static IDisposable ActAs(string role, int? id)
    {
        var previous = Current.Value;
        Current.Value = new Actor(role, id);

        return new Scope(previous);
    }

    private sealed record Actor(string Role, int? Id);

    private sealed class Scope : IDisposable
    {
        private readonly Actor? _previous;

        public Scope(Actor? previous) => _previous = previous;

        public void Dispose() => Current.Value = _previous;
    }
}
"""
        : $$"""
namespace {{rootNamespace}}.Validation;

/// <summary>Who is acting. This app was generated with no login, so there is nobody — and no
/// Auth namespace to name, which is what makes this the no-login half rather than a null check.
/// </summary>
internal static class ActingUser
{
    public static string? Role() => null;

    public static int? Id() => null;
}
""";
}
