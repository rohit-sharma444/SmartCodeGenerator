using System.Collections;
using System.Reflection;
using AppScaffolding;
using AppScaffolding.Auth;
using AppScaffolding.Targets;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The Admin group belongs to the shell, which is a later branch. Until then — and permanently, for
/// anyone who names one of the four older layouts — the Users screen is an ordinary pane. An
/// application whose only account is the seeded admin otherwise has no way to gain a second.
/// </summary>
[TestClass]
public class UsersPaneTests
{
    private static string MainViewModelFor(AppCustomization customization)
        => new WpfTarget().FrontEndFiles("Shop", ["Order"], customization)
            .Single(f => f.Name == "MainViewModel.cs").Source;

    [TestMethod]
    public void A_default_desktop_app_has_a_users_pane()
        => StringAssert.Contains(MainViewModelFor(AppCustomization.Default), "new UsersViewModel()");

    [TestMethod]
    public void Nologin_has_no_users_pane()
        => Assert.IsFalse(
            MainViewModelFor(AppCustomization.Default with { Login = false })
                .Contains("UsersViewModel", StringComparison.Ordinal));

    /// <summary>The regression proof for the older layouts: with `nologin` the main view model is
    /// exactly what it was before this branch.</summary>
    [TestMethod]
    public void Nologin_leaves_the_main_view_model_byte_identical()
        => Assert.AreEqual(
            MainViewModelTemplate.Build("Shop", ["Order"], seed: true),
            MainViewModelFor(AppCustomization.Default with { Login = false }));

    /// <summary>The pane is added only for an administrator. A plain user seeing the account list
    /// is the role doing nothing.</summary>
    [TestMethod]
    public void The_users_pane_is_added_only_for_an_administrator()
        => StringAssert.Contains(MainViewModelFor(AppCustomization.Default), "Session.IsAdmin");

    [TestMethod]
    public void Both_desktop_targets_emit_the_users_view_model()
    {
        foreach (ITargetScaffolder target in new ITargetScaffolder[]
                 { new WpfTarget(), new WinFormsTarget() })
        {
            var names = target.FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
                .Select(f => f.Name).ToList();

            CollectionAssert.Contains(names, "UsersViewModel.cs", target.Key);
        }
    }

    // ---- the emitted view model, executed ----

    private static Assembly Emit()
    {
        var source = EmittedSource.Flatten(
            [
                .. AuthServiceTemplatesTestFragments(),
                ViewModelTemplates.ObservableObject("Acme")
                    .Replace("namespace Acme.ViewModels;", string.Empty),
                ViewModelTemplates.PortableRelayCommand("Acme")
                    .Replace("namespace Acme.ViewModels;", string.Empty),
                ViewModelTemplates.EntityViewModelInterface("Acme")
                    .Replace("namespace Acme.ViewModels;", string.Empty),
                UsersViewModelTemplate.Source("Acme")
                    .Replace("namespace Acme.ViewModels;", string.Empty)
                    .Replace("using Acme.Auth;", string.Empty)
                    .Replace("using Acme.Data;", string.Empty)
                    .Replace("using Acme.Models;", string.Empty),
            ]);

        EmittedSource.AssertCompiles(source);

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);

        AuthServiceTemplateTests.Rows(assembly).Clear();

        return assembly;
    }

    private static string[] AuthServiceTemplatesTestFragments()
        =>
        [
            .. AuthServiceTemplateTests.AuthCoreFragments,
            UserSeedTemplate.Source("Acme").Replace("namespace Acme.Auth;", string.Empty),
            SessionTemplate.Source("Acme")
                .Replace("namespace Acme.Auth;", string.Empty)
                .Replace("using Acme.Models;", string.Empty),
        ];

    /// <summary>Signs in as the seeded administrator and returns the live Users view model.</summary>
    private static (Assembly Assembly, object Model) SignedInAsAdmin()
    {
        var assembly = Emit();

        assembly.GetType("UserSeed")!.GetMethod("EnsureAdminSeeded")!.Invoke(null, null);

        var admin = AuthServiceTemplateTests.Rows(assembly)[0]!;
        assembly.GetType("Session")!.GetMethod("SignIn")!.Invoke(null, [admin]);

        return (assembly, Activator.CreateInstance(assembly.GetType("UsersViewModel")!)!);
    }

    private static IList Items(object model)
        => (IList)model.GetType().GetProperty("Items")!.GetValue(model)!;

    private static void Execute(object model, string command)
    {
        var value = model.GetType().GetProperty(command)!.GetValue(model)!;

        value.GetType().GetMethod("Execute")!.Invoke(value, [null]);
    }

    private static string Status(object model)
        => (string)model.GetType().GetProperty("Status")!.GetValue(model)!;

    [TestMethod]
    public void The_users_view_model_satisfies_the_pane_interface()
    {
        var source = UsersViewModelTemplate.Source("Acme");

        foreach (var member in new[]
                 { "Title", "Items", "Selected", "AddCommand", "DeleteCommand", "SaveCommand", "Status" })
        {
            StringAssert.Contains(source, member);
        }
    }

    [TestMethod]
    public void The_pane_lists_the_accounts()
    {
        var (_, model) = SignedInAsAdmin();

        Assert.HasCount(1, Items(model));
        Assert.AreEqual("Users", model.GetType().GetProperty("Title")!.GetValue(model));
    }

    /// <summary>
    /// Deleting the signed-in account would leave the running session pointing at a record that no
    /// longer exists — and, since a role can only be lost by deletion, it is also what guarantees
    /// an application always keeps at least one administrator.
    /// </summary>
    [TestMethod]
    public void An_administrator_cannot_delete_their_own_account()
    {
        var (_, model) = SignedInAsAdmin();

        model.GetType().GetProperty("Selected")!.SetValue(model, Items(model)[0]);

        Execute(model, "DeleteCommand");

        Assert.HasCount(1, Items(model));
        StringAssert.Contains(Status(model), "signed in");
    }

    [TestMethod]
    public void Another_account_can_be_added_and_deleted()
    {
        var (_, model) = SignedInAsAdmin();

        Execute(model, "AddCommand");
        Assert.HasCount(2, Items(model));

        model.GetType().GetProperty("Selected")!.SetValue(model, Items(model)[1]);
        Execute(model, "DeleteCommand");

        Assert.HasCount(1, Items(model));
    }

    /// <summary>A new account must not be usable until someone gives it a password — an account
    /// with an empty hash must never verify.</summary>
    [TestMethod]
    public void A_new_account_cannot_sign_in_until_it_is_given_a_password()
    {
        var (assembly, model) = SignedInAsAdmin();

        Execute(model, "AddCommand");

        var added = Items(model)[1]!;
        added.GetType().GetProperty("UserName")!.SetValue(added, "sam");

        Execute(model, "SaveCommand");

        var result = assembly.GetType("AuthService")!.GetMethod("SignIn")!
            .Invoke(null, ["sam", string.Empty])!;

        Assert.IsFalse((bool)result.GetType().GetProperty("Succeeded")!.GetValue(result)!);
    }

    /// <summary>Setting a password goes through the hasher, so the store never holds one in
    /// clear.</summary>
    [TestMethod]
    public void A_password_set_here_signs_in()
    {
        var (assembly, model) = SignedInAsAdmin();

        Execute(model, "AddCommand");

        var added = Items(model)[1]!;
        added.GetType().GetProperty("UserName")!.SetValue(added, "sam");

        Execute(model, "SaveCommand");

        model.GetType().GetProperty("Selected")!.SetValue(model, added);
        model.GetType().GetProperty("NewPassword")!.SetValue(model, "chosen");

        Execute(model, "SetPasswordCommand");

        var result = assembly.GetType("AuthService")!.GetMethod("SignIn")!
            .Invoke(null, ["sam", "chosen"])!;

        Assert.IsTrue((bool)result.GetType().GetProperty("Succeeded")!.GetValue(result)!);

        // Dropped as soon as it is used, so it is not sitting in a bound property afterwards.
        Assert.AreEqual(
            string.Empty, model.GetType().GetProperty("NewPassword")!.GetValue(model));
    }

    [TestMethod]
    public void The_view_model_never_writes_a_hash_itself()
    {
        var source = UsersViewModelTemplate.Source("Acme");

        StringAssert.Contains(source, "AuthService.ChangePassword");
        Assert.IsFalse(
            source.Contains("PasswordHash =", StringComparison.Ordinal),
            "hashing belongs to PasswordHasher, and only AuthService should reach it");
    }

    // ---- the web half ----

    private static string BlazorSource(AppCustomization customization, string name)
        => new BlazorTarget().FrontEndFiles("Shop", ["Order"], customization)
            .Single(f => f.Name == name).Source;

    [TestMethod]
    public void The_web_app_has_a_users_page_restricted_to_administrators()
    {
        var page = BlazorSource(AppCustomization.Default, "UsersPage.razor");

        StringAssert.Contains(page, "@page \"/users\"");
        StringAssert.Contains(page, "[Authorize(Roles = \"Admin\")]");
    }

    /// <summary>
    /// The page needs BOTH authorization namespaces, which are easy to mistake for one.
    /// </summary>
    /// <remarks>
    /// [Authorize] comes from Microsoft.AspNetCore.Authorization; AuthenticationState, which the
    /// cascading parameter is typed as, comes from Microsoft.AspNetCore.Components.Authorization.
    /// Importing only the first compiles everywhere except that one line. Found by building a
    /// generated app.
    /// </remarks>
    [TestMethod]
    public void The_users_page_imports_both_authorization_namespaces()
    {
        var page = BlazorSource(AppCustomization.Default, "UsersPage.razor");

        StringAssert.Contains(page, "@using Microsoft.AspNetCore.Authorization");
        StringAssert.Contains(page, "@using Microsoft.AspNetCore.Components.Authorization");
    }

    [TestMethod]
    public void A_nologin_web_app_has_no_users_page()
        => CollectionAssert.DoesNotContain(
            new BlazorTarget()
                .FrontEndFiles("Shop", ["Order"], AppCustomization.Default with { Login = false })
                .Select(f => f.Name).ToList(),
            "UsersPage.razor");

    /// <summary>Hidden from the nav AND refused by the route. Hiding alone is not a permission:
    /// the URL can be typed.</summary>
    [TestMethod]
    public void The_users_link_is_shown_only_to_administrators()
        => StringAssert.Contains(
            BlazorSource(AppCustomization.Default, "NavMenu.razor"),
            "<AuthorizeView Roles=\"Admin\">");
}
