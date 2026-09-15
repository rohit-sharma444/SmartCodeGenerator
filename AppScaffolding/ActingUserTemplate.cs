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
    public static string Source(string rootNamespace, bool sessionAuth) => sessionAuth
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
