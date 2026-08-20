using AppScaffolding.Layouts;

namespace AppScaffolding.Auth;

/// <summary>Emits the web target's login page, its endpoints and the router that enforces them.</summary>
/// <remarks>
/// Three things here are not preferences, and each has a test saying so.
/// <para>
/// The login page is static-rendered — no <c>@rendermode</c>. An interactive circuit has already
/// flushed its response, so it cannot write an authentication cookie: an interactive login would
/// appear to work and leave the visitor signed out.
/// </para>
/// <para>
/// <c>Routes.razor</c> is replaced because the template's uses a plain <c>RouteView</c>, which
/// ignores <c>[Authorize]</c> altogether. Without <c>AuthorizeRouteView</c> every attribute this
/// file emits is decoration and any page renders for anyone.
/// </para>
/// <para>
/// Every file that authorizes carries its own <c>@using</c>: the template's <c>_Imports.razor</c>
/// imports no authorization namespace, and it names the project it was created for, so rewriting
/// it would be a second namespace-reconciliation problem.
/// </para>
/// </remarks>
public static class BlazorLoginTemplate
{
    /// <summary>The brand panel's fill when the app was given no accent.</summary>
    private const string DefaultBrandFill = "#1D4ED8";

    /// <summary>Source of <c>Components/Pages/Login.razor</c>.</summary>
    public static string LoginPage(LayoutContext context)
    {
        var brandFill = context.Customization.Accent ?? DefaultBrandFill;

        var brandLine = context.Customization.HasBranding
            ? Escape(context.Customization.Company)
            : Escape(context.ProjectName);

        var subLine = context.Customization.HasBranding ? Escape(context.ProjectName) : string.Empty;

        return $$"""
@page "/login"
@using {{context.RootNamespace}}.Auth

<PageTitle>Sign in</PageTitle>

<style>
    .login-split { display: flex; min-height: 78vh; border: 1px solid #e5e7eb; border-radius: 8px; overflow: hidden; }
    .login-brand { width: 44%; min-width: 220px; background: {{brandFill}}; color: #fff; padding: 32px 28px; display: flex; flex-direction: column; justify-content: space-between; }
    .login-brand h1 { font-size: 1.6rem; font-weight: 700; margin: 16px 0 4px; }
    .login-brand p { opacity: .78; font-size: .9rem; }
    .login-mark { width: 40px; height: 40px; border-radius: 8px; background: rgba(255,255,255,.22); }
    .login-form { flex: 1; display: flex; align-items: center; justify-content: center; padding: 0 40px; }
    .login-form form, .login-form .login-head { width: 100%; max-width: 280px; }
    @@media (max-width: 640px) { .login-split { flex-direction: column; } .login-brand { width: auto; } }
</style>

<div class="login-split">
    <div class="login-brand">
        <div>
            <div class="login-mark"></div>
            <h1>{{brandLine}}</h1>
        </div>
        <p>{{subLine}}</p>
    </div>
    <div class="login-form">
        <div>
            <div class="login-head">
                <h2>Sign in</h2>
                <p class="text-body-secondary">Enter your credentials</p>
            </div>

            @if (ShowError)
            {
                <div class="alert alert-danger py-2">@SignInResult.FailureMessage</div>
            }

            <form method="post" action="/auth/login">
                <AntiforgeryToken />

                <label class="form-label" for="userName">User name</label>
                <input class="form-control mb-3" id="userName" name="userName" autofocus />

                <label class="form-label" for="password">Password</label>
                <input class="form-control mb-3" id="password" name="password" type="password" />

                <button class="btn btn-primary w-100" type="submit">Sign in</button>
            </form>

            @if (Hint.Length > 0)
            {
                <p class="text-body-secondary small mt-3">@Hint</p>
            }
        </div>
    </div>
</div>

@code {
    // Supplied by the endpoint's redirect on a failed attempt. The endpoint deliberately does not
    // say WHICH half was wrong -- that would tell anyone who asked which user names exist.
    [SupplyParameterFromQuery(Name = "error")]
    private string? Error { get; set; }

    private bool ShowError => Error is not null;

    private string Hint { get; set; } = string.Empty;

    protected override void OnInitialized()
    {
        // The app has no Program.cs of its own to seed from, so the first page that needs an
        // account makes sure one exists.
        UserSeed.EnsureAdminSeeded();

        Hint = UserSeed.IsFirstRun() ? UserSeed.FirstRunHint : string.Empty;
    }
}
""";
    }

    /// <summary>Source of <c>Components/Routes.razor</c>, replacing the template's.</summary>
    public static string Routes(string rootNamespace) => """
@using Microsoft.AspNetCore.Components.Authorization

<Router AppAssembly="typeof(Program).Assembly" NotFoundPage="typeof(Pages.NotFound)">
    <Found Context="routeData">
        @* AuthorizeRouteView, not RouteView: a plain RouteView ignores [Authorize] on a page and
           renders it for anyone, which would make every authorization attribute in this app
           decoration. *@
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
            <NotAuthorized>
                @* Reached when a signed-in user lacks the role. Someone not signed in at all is
                   redirected by the cookie handler's LoginPath before this renders. *@
                <div class="alert alert-warning">
                    You do not have permission to open this page.
                    <a href="/login">Sign in as another user</a>.
                </div>
            </NotAuthorized>
        </AuthorizeRouteView>
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
""";

    /// <summary>Source of <c>Auth/AuthEndpoints.cs</c>.</summary>
    /// <remarks>
    /// Endpoints rather than component code, for the reason the class remarks give: only a request
    /// that has not yet rendered can set a cookie. Keeping them in their own file also keeps the
    /// <c>Program.cs</c> surgery to three small insertions.
    /// </remarks>
    public static string Endpoints(string rootNamespace) => $$"""
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using {{rootNamespace}}.Auth;

namespace {{rootNamespace}}.Auth;

/// <summary>The two endpoints the login form and the sign-out button post to.</summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/auth/login", async (HttpContext context, [Microsoft.AspNetCore.Mvc.FromForm] string userName, [Microsoft.AspNetCore.Mvc.FromForm] string password) =>
        {
            UserSeed.EnsureAdminSeeded();

            var result = AuthService.SignIn(userName, password);

            // One destination for every kind of failure. Saying which half was wrong would tell
            // anyone who asked which user names exist.
            if (!result.Succeeded)
                return Results.Redirect("/login?error=1");

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, result.User!.UserName),
                new(ClaimTypes.NameIdentifier, result.User.Id.ToString()),

                // Without this claim [Authorize(Roles = "Admin")] never matches anybody.
                new(ClaimTypes.Role, result.User.Role.ToString()),
            };

            var identity = new ClaimsIdentity(
                claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return Results.Redirect("/");
        }).DisableAntiforgery();

        app.MapPost("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return Results.Redirect("/login");
        }).DisableAntiforgery();
    }
}
""";

    /// <summary>An HTML text value: a company name with an ampersand would otherwise break the
    /// markup it is written into.</summary>
    private static string Escape(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;");
}
