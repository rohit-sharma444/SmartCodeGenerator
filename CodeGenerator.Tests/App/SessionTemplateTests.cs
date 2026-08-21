using System.Reflection;
using AppScaffolding;
using AppScaffolding.Auth;
using AppScaffolding.Targets;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Session is a STATIC holder of the signed-in user, which is correct for a desktop process and
/// catastrophic in a server one — it would be shared by every visitor. These tests pin it to the
/// desktop targets.
/// </summary>
[TestClass]
public class SessionTemplateTests
{
    private static Assembly Emit()
    {
        var source = EmittedSource.Flatten(
            AuthModelTemplates.Role("Acme").Replace("namespace Acme.Auth;", string.Empty),
            AuthModelTemplates.User("Acme")
                .Replace("namespace Acme.Models;", string.Empty)
                .Replace("using Acme.Auth;", string.Empty),
            SessionTemplate.Source("Acme").Replace("namespace Acme.Auth;", string.Empty));

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    [TestMethod]
    public void A_fresh_session_holds_nobody_and_is_not_an_admin()
    {
        var session = Emit().GetType("Session")!;

        session.GetMethod("Clear")!.Invoke(null, null);

        Assert.IsNull(session.GetProperty("Current")!.GetValue(null));
        Assert.IsFalse((bool)session.GetProperty("IsAdmin")!.GetValue(null)!);
    }

    /// <summary>Sign-out is a flag the shell reads on close, which is what lets App.xaml.cs loop
    /// back to the login instead of restarting the process.</summary>
    [TestMethod]
    public void Requesting_sign_out_raises_the_flag_and_clearing_lowers_it()
    {
        var session = Emit().GetType("Session")!;

        session.GetMethod("RequestSignOut")!.Invoke(null, null);
        Assert.IsTrue((bool)session.GetProperty("SignOutRequested")!.GetValue(null)!);

        session.GetMethod("Clear")!.Invoke(null, null);
        Assert.IsFalse((bool)session.GetProperty("SignOutRequested")!.GetValue(null)!);
    }

    [TestMethod]
    public void An_admin_signs_in_as_an_admin()
    {
        var assembly = Emit();
        var session = assembly.GetType("Session")!;
        var userType = assembly.GetType("User")!;
        var user = Activator.CreateInstance(userType)!;

        userType.GetProperty("Role")!
            .SetValue(user, Enum.Parse(assembly.GetType("AppRole")!, "Admin"));

        session.GetMethod("SignIn")!.Invoke(null, [user]);

        Assert.IsTrue((bool)session.GetProperty("IsAdmin")!.GetValue(null)!);

        session.GetMethod("Clear")!.Invoke(null, null);
    }

    /// <summary>A plain user must not be an administrator, or the role gate is decoration.</summary>
    [TestMethod]
    public void A_plain_user_is_not_an_admin()
    {
        var assembly = Emit();
        var session = assembly.GetType("Session")!;
        var userType = assembly.GetType("User")!;
        var user = Activator.CreateInstance(userType)!;

        session.GetMethod("SignIn")!.Invoke(null, [user]);

        Assert.IsFalse((bool)session.GetProperty("IsAdmin")!.GetValue(null)!);

        session.GetMethod("Clear")!.Invoke(null, null);
    }

    /// <summary>The header shows the display name when there is one and the user name otherwise;
    /// a blank name in the shell header reads as a broken sign-in.</summary>
    [TestMethod]
    public void The_display_name_falls_back_to_the_user_name()
    {
        var assembly = Emit();
        var session = assembly.GetType("Session")!;
        var userType = assembly.GetType("User")!;
        var user = Activator.CreateInstance(userType)!;

        userType.GetProperty("UserName")!.SetValue(user, "rohit");

        session.GetMethod("SignIn")!.Invoke(null, [user]);

        Assert.AreEqual("rohit", session.GetProperty("DisplayName")!.GetValue(null));

        userType.GetProperty("DisplayName")!.SetValue(user, "Rohit Sharma");

        Assert.AreEqual("Rohit Sharma", session.GetProperty("DisplayName")!.GetValue(null));

        session.GetMethod("Clear")!.Invoke(null, null);
    }

    /// <summary>
    /// The login view model compiles against the MVVM plumbing and the auth core.
    /// </summary>
    /// <remarks>
    /// Here rather than waiting for the WPF task: nothing else compiles this template until the
    /// login window exists, and a template that is emitted but never compiled is a template nobody
    /// has checked. Uses the PORTABLE RelayCommand, because this file is shared with Windows Forms
    /// and must not depend on WPF's CommandManager.
    /// </remarks>
    [TestMethod]
    public void The_login_view_model_compiles_with_the_auth_core()
    {
        var source = EmittedSource.Flatten(
            [
                .. AuthServiceTemplateTests.AuthCoreFragments,
                UserSeedTemplate.Source("Acme").Replace("namespace Acme.Auth;", string.Empty),
                SessionTemplate.Source("Acme").Replace("namespace Acme.Auth;", string.Empty),
                ViewModelTemplates.ObservableObject("Acme")
                    .Replace("namespace Acme.ViewModels;", string.Empty),
                ViewModelTemplates.PortableRelayCommand("Acme")
                    .Replace("namespace Acme.ViewModels;", string.Empty),
                LoginViewModelTemplate.Source("Acme")
                    .Replace("namespace Acme.ViewModels;", string.Empty)
                    .Replace("using Acme.Auth;", string.Empty),
            ]);

        EmittedSource.AssertCompiles(source);
    }

    /// <summary>The guard that keeps a per-process static out of the web target. Blazor Server
    /// runs every visitor in one process; a static Current would sign them all in as whoever
    /// signed in last.</summary>
    [TestMethod]
    public void The_web_target_emits_no_session()
    {
        var files = new BlazorTarget().FrontEndFiles("Shop", ["Order"], AppCustomization.Default);

        Assert.IsFalse(
            files.Any(f => f.Name == "Session.cs"),
            "A static signed-in user in a server process is shared by every visitor.");
    }
}
