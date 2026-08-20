using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Cookie authentication is the one place this target has to touch files the template wrote, and
/// the one place where "it compiles" is not evidence. These tests pin the shape; the end-to-end
/// task proves the behaviour.
/// </summary>
[TestClass]
public class BlazorLoginTemplateTests
{
    private static IReadOnlyList<GeneratedFile> Files(AppCustomization customization)
        => new BlazorTarget().FrontEndFiles("Shop", ["Order"], customization);

    private static string SourceOf(AppCustomization customization, string name)
        => Files(customization).Single(f => f.Name == name).Source;

    [TestMethod]
    public void A_default_web_app_emits_a_login_page_its_endpoints_and_a_router()
    {
        var names = Files(AppCustomization.Default).Select(f => f.Name).ToList();

        CollectionAssert.Contains(names, "Login.razor");
        CollectionAssert.Contains(names, "AuthEndpoints.cs");
        CollectionAssert.Contains(names, "Routes.razor");
    }

    [TestMethod]
    public void Nologin_emits_none_of_them()
    {
        var names = Files(AppCustomization.Default with { Login = false })
            .Select(f => f.Name).ToList();

        CollectionAssert.DoesNotContain(names, "Login.razor");
        CollectionAssert.DoesNotContain(names, "AuthEndpoints.cs");
        CollectionAssert.DoesNotContain(names, "Routes.razor");
    }

    /// <summary>
    /// The login page must NOT be interactive. An interactive circuit has already sent its
    /// response headers, so it cannot set a cookie — the sign-in would appear to work and the user
    /// would stay signed out.
    /// </summary>
    [TestMethod]
    public void The_login_page_is_not_interactive()
    {
        var page = SourceOf(AppCustomization.Default, "Login.razor");

        Assert.IsFalse(
            page.Contains("@rendermode", StringComparison.Ordinal),
            "an interactive circuit cannot set an authentication cookie");

        StringAssert.Contains(page, "method=\"post\"");
        StringAssert.Contains(page, "action=\"/auth/login\"");
    }

    /// <summary>UseAntiforgery validates the token; a form without one is rejected with a bare 400
    /// that reads as a broken login.</summary>
    [TestMethod]
    public void The_login_form_carries_an_antiforgery_token()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default, "Login.razor"), "<AntiforgeryToken />");

    /// <summary>
    /// The trap that makes every other authorization check theatre.
    /// </summary>
    /// <remarks>
    /// The template's Routes.razor uses a plain RouteView, which IGNORES [Authorize] on a page
    /// entirely — the page renders for anyone. Only AuthorizeRouteView honours it. Nothing about
    /// the attribute being present proves the router reads it, which is why this test exists
    /// beside the one asserting the attribute.
    /// </remarks>
    [TestMethod]
    public void The_router_authorizes_routes_rather_than_merely_rendering_them()
    {
        var routes = SourceOf(AppCustomization.Default, "Routes.razor");

        StringAssert.Contains(routes, "AuthorizeRouteView");
        Assert.IsFalse(
            routes.Contains("<RouteView ", StringComparison.Ordinal),
            "a plain RouteView ignores [Authorize] and renders the page for anyone");
    }

    /// <summary>The template's _Imports has no authorization namespaces, so every file that uses
    /// them has to bring its own.</summary>
    [TestMethod]
    public void The_files_that_authorize_import_the_namespace_they_need()
    {
        StringAssert.Contains(
            SourceOf(AppCustomization.Default, "OrderPage.razor"),
            "@using Microsoft.AspNetCore.Authorization");

        StringAssert.Contains(
            SourceOf(AppCustomization.Default, "Routes.razor"),
            "@using Microsoft.AspNetCore.Components.Authorization");
    }

    [TestMethod]
    public void Every_entity_page_requires_authorisation()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default, "OrderPage.razor"), "@attribute [Authorize]");

    [TestMethod]
    public void Nologin_pages_carry_no_authorize_attribute()
        => Assert.IsFalse(
            SourceOf(AppCustomization.Default with { Login = false }, "OrderPage.razor")
                .Contains("[Authorize]", StringComparison.Ordinal));

    /// <summary>
    /// A CSS at-rule in an inline style block has to be escaped for Razor.
    /// </summary>
    /// <remarks>
    /// Razor reads a bare <c>@media</c> as the start of a C# expression and the page fails to
    /// compile with "the name 'media' does not exist". Found by building a generated app; every
    /// assertion in this file passed while it was broken.
    /// </remarks>
    [TestMethod]
    public void The_login_page_escapes_css_at_rules()
    {
        var page = SourceOf(AppCustomization.Default, "Login.razor");

        StringAssert.Contains(page, "@@media");
        Assert.IsFalse(
            page.Contains("\n    @media", StringComparison.Ordinal),
            "an unescaped @media is read by Razor as a C# expression");
    }

    [TestMethod]
    public void The_endpoints_sign_in_and_out()
    {
        var endpoints = SourceOf(AppCustomization.Default, "AuthEndpoints.cs");

        StringAssert.Contains(endpoints, "/auth/login");
        StringAssert.Contains(endpoints, "/auth/logout");
        StringAssert.Contains(endpoints, "SignInAsync");
        StringAssert.Contains(endpoints, "SignOutAsync");
    }

    /// <summary>The role must be a claim, or [Authorize(Roles = ...)] never matches.</summary>
    [TestMethod]
    public void The_signed_in_cookie_carries_the_role_as_a_claim()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default, "AuthEndpoints.cs"), "ClaimTypes.Role");

    /// <summary>A failed sign-in must not leak which half was wrong, on the web as on the
    /// desktop.</summary>
    [TestMethod]
    public void A_failed_sign_in_returns_to_the_login_without_saying_why()
    {
        var endpoints = SourceOf(AppCustomization.Default, "AuthEndpoints.cs");

        StringAssert.Contains(endpoints, "/login?error=1");
        Assert.IsFalse(endpoints.Contains("UnknownUser", StringComparison.Ordinal));
        Assert.IsFalse(endpoints.Contains("WrongPassword", StringComparison.Ordinal));
    }

    /// <summary>Signing out is a POST, not a link: a GET sign-out can be triggered by any image
    /// tag on any page the user visits.</summary>
    [TestMethod]
    public void The_nav_offers_sign_out_as_a_post()
    {
        var nav = SourceOf(AppCustomization.Default, "NavMenu.razor");

        StringAssert.Contains(nav, "action=\"/auth/logout\"");
        StringAssert.Contains(nav, "method=\"post\"");
        StringAssert.Contains(nav, "<AntiforgeryToken />");
    }

    [TestMethod]
    public void A_nologin_nav_offers_no_sign_out()
        => Assert.IsFalse(
            SourceOf(AppCustomization.Default with { Login = false }, "NavMenu.razor")
                .Contains("logout", StringComparison.Ordinal));
}
