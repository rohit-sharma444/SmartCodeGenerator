using AppScaffolding.Layouts;

namespace AppScaffolding.Auth;

/// <summary>Emits the web target's login page, its endpoints and the router that enforces them.</summary>
/// <remarks>
/// Three things here are not preferences, and each has a test saying so.
/// <para>
/// The login page is static-rendered — no <c>@rendermode</c>, and
/// <c>[ExcludeFromInteractiveRouting]</c> to keep it that way now that the ROUTER is interactive.
/// An interactive circuit has already flushed its response, so it cannot write an authentication
/// cookie: an interactive login would appear to work and leave the visitor signed out. The other
/// half of the opt-out is in <see cref="BlazorProjectFixup.EnableInteractiveRouting"/> — without
/// it the interactive router does not know this page's route and renders Not Found over the form
/// the server had just sent.
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
@using Microsoft.AspNetCore.Components
@using {{context.RootNamespace}}.Auth
@attribute [ExcludeFromInteractiveRouting]

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

            @* The token is not decoration: the endpoint validates it, so a form without one is
               refused with a 400. Verified against a running app. Without it, any page anywhere
               could post here and sign a visitor into an account of its choosing. *@
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

    /// <summary>Source of <c>Components/Pages/UsersPage.razor</c>.</summary>
    /// <remarks>
    /// Restricted twice over: the nav does not show the link to a non-administrator, and the page
    /// itself refuses one. Only the second is a permission — a URL can be typed.
    /// </remarks>
    public static string UsersPage(string rootNamespace) => $$"""
@page "/users"
@rendermode InteractiveServer
@using Microsoft.AspNetCore.Authorization
@* A different namespace from the one above: [Authorize] comes from
   Microsoft.AspNetCore.Authorization, AuthenticationState from
   Microsoft.AspNetCore.Components.Authorization. Importing only the first builds
   everywhere except the cascading parameter. *@
@using Microsoft.AspNetCore.Components.Authorization
@using {{rootNamespace}}.Auth
@using {{rootNamespace}}.Data
@using {{rootNamespace}}.Models
@attribute [Authorize(Roles = "Admin")]

<PageTitle>Users</PageTitle>

<h1>Users</h1>

<table class="table">
    <thead>
        <tr><th>User name</th><th>Display name</th><th>Role</th><th>Active</th><th></th></tr>
    </thead>
    <tbody>
        @foreach (var account in _accounts)
        {
            <tr>
                <td><input class="form-control" @bind="account.UserName" /></td>
                <td><input class="form-control" @bind="account.DisplayName" /></td>
                <td>
                    <select class="form-select" @bind="account.Role">
                        <option value="@AppRole.User">User</option>
                        <option value="@AppRole.Admin">Admin</option>
                    </select>
                </td>
                <td><input type="checkbox" @bind="account.IsActive" /></td>
                <td>
                    <button class="btn btn-sm btn-outline-danger"
                            disabled="@IsSignedIn(account)"
                            @onclick="() => Delete(account)">Delete</button>
                </td>
            </tr>
        }
    </tbody>
</table>

<div class="d-flex gap-2 align-items-end mb-3">
    <button class="btn btn-primary" @onclick="Add">Add</button>
    <button class="btn btn-secondary" @onclick="Save">Save</button>

    <div class="ms-4">
        <label class="form-label mb-0">Set password for the account above</label>
        <div class="d-flex gap-2">
            <input class="form-control" type="password" @bind="_newPassword" />
            <button class="btn btn-outline-secondary" @onclick="SetPassword">Set</button>
        </div>
    </div>
</div>

<p class="text-body-secondary">@_status</p>

@code {
    private readonly IStore<User> _store = StoreFactory.Create<User>();

    private List<User> _accounts = new();
    private User? _selected;
    private string _newPassword = string.Empty;
    private string _status = string.Empty;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    private string _signedInUser = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        _accounts = _store.GetAll();

        if (AuthenticationState is not null)
        {
            var state = await AuthenticationState;
            _signedInUser = state.User.Identity?.Name ?? string.Empty;
        }
    }

    // The signed-in account cannot be deleted: it would leave the session pointing at a record
    // that no longer exists, and it is what guarantees one administrator always remains.
    private bool IsSignedIn(User account)
        => string.Equals(account.UserName, _signedInUser, StringComparison.OrdinalIgnoreCase);

    private void Add()
    {
        // No password, so the account is inert until someone sets one. A default password on a
        // new account is a door everyone knows about.
        var user = new User
        {
            Id = _store.NextId(_accounts),
            UserName = "new.user",
            DisplayName = "New user",
            Role = AppRole.User,
            IsActive = true,
            CreatedOn = DateTime.Now,
            ModifiedOn = DateTime.Now,
        };

        _accounts.Add(user);
        _selected = user;
        _status = "Added. Set a password before it can sign in.";
    }

    private void Delete(User account)
    {
        if (IsSignedIn(account))
        {
            _status = "You cannot delete the account you are signed in with.";
            return;
        }

        _accounts.Remove(account);
        Save();
    }

    private void Save()
    {
        var write = _store.SaveAll(_accounts);
        _status = write.Success ? "Saved." : write.Refusal ?? "Could not save the accounts.";
    }

    private void SetPassword()
    {
        var target = _selected ?? _accounts.LastOrDefault();

        if (target is null || _newPassword.Length == 0)
        {
            _status = "Choose an account and type a password first.";
            return;
        }

        // Through AuthService, so hashing stays in one place.
        _status = AuthService.ChangePassword(target.Id, _newPassword)
            ? "Password set."
            : "Could not set the password.";

        _newPassword = string.Empty;
        _accounts = _store.GetAll();
    }
}
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
        });

        app.MapPost("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return Results.Redirect("/login");
        });
    }
}
""";

    /// <summary>An HTML text value: a company name with an ampersand would otherwise break the
    /// markup it is written into.</summary>
    private static string Escape(string value)
        => AppCustomization.RazorText(value);
}
