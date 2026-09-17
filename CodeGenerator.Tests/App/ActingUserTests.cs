using System;
using System.Collections.Generic;
using System.IO;
using AppScaffolding;
using AppScaffolding.Screens;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ActingUserTests
{
    [TestMethod]
    public void A_login_app_reads_the_acting_user_from_the_session()
    {
        var source = ActingUserTemplate.Source("Acme", sessionAuth: true);

        StringAssert.Contains(source, "namespace Acme.Validation;");
        StringAssert.Contains(source, "public static string? Role()");
        StringAssert.Contains(source, "public static int? Id()");
        StringAssert.Contains(source, "Session.Current");
    }

    [TestMethod]
    public void An_app_with_no_login_has_nobody_acting()
    {
        // No Session type is emitted at all for a target with no auth core, so naming one here is
        // the CS0234 that issue #126 is about. This returns null without referring to it.
        var source = ActingUserTemplate.Source("Acme", sessionAuth: false);

        Assert.IsFalse(source.Contains("Session", StringComparison.Ordinal));
        StringAssert.Contains(source, "public static string? Role() => null;");
    }

    [TestMethod]
    public void No_template_emits_its_own_CurrentRole()
    {
        foreach (var (name, source) in EveryEmittedScreenSource())
            Assert.IsFalse(
                source.Contains("CurrentRole", System.StringComparison.Ordinal),
                $"{name} still emits its own copy.");
    }

    /// <summary>
    /// The one seam that can catch the call site passing the wrong flag: <c>AppScaffolder</c>
    /// must hand <see cref="ActingUserTemplate.Source"/> its own <c>sessionAuth</c> (auth AND
    /// <c>HasDesktopSession</c>), not the broader <c>auth</c>/<c>login</c>. A Blazor app has a
    /// login and screens (auth == true) but no desktop <c>Session</c> (HasDesktopSession ==
    /// false), so sessionAuth == false. Passing <c>auth</c> there would emit <c>using
    /// Acme.Auth;</c> and <c>Session.Current</c> into a Blazor app that never receives a Session
    /// type -- CS0234, a generated app that does not compile. A template-level test over
    /// <see cref="ActingUserTemplate.Source"/> alone cannot see this: the defect is at the call
    /// site, not in the template.
    /// </summary>
    [TestMethod]
    public void A_blazor_login_apps_acting_user_names_no_session()
    {
        InAScaffoldedBlazorLoginApp((_, project) =>
        {
            var actingUser = File.ReadAllText(Path.Combine(project, "Validation", "ActingUser.cs"));

            Assert.IsFalse(actingUser.Contains("Session", StringComparison.Ordinal));
            StringAssert.Contains(actingUser, "public static string? Role() => null;");
        });
    }

    /// <summary>Each of the five templates that used to emit its own <c>CurrentRole()</c>,
    /// called with the arguments its own existing tests use.</summary>
    private static IEnumerable<(string Name, string Source)> EveryEmittedScreenSource()
    {
        yield return ("EfStoreTemplate (auth)", EfStoreTemplate.Source("Acme", auth: true));
        yield return ("EfStoreTemplate (no auth)", EfStoreTemplate.Source("Acme", auth: false));
        yield return ("JsonStoreTemplate (auth)", JsonStoreTemplate.Source("Acme.Data", auth: true));
        yield return ("JsonStoreTemplate (no auth)", JsonStoreTemplate.Source("Acme.Data", auth: false));
        yield return ("RepositoryTemplate (auth)",
            RepositoryTemplate.Base("Acme", validation: true, logging: false, auth: true));
        yield return ("RepositoryTemplate (no auth)",
            RepositoryTemplate.Base("Acme", validation: true, logging: false, auth: false));
        yield return ("MasterScreenTemplate (login)",
            MasterScreenTemplate.Source("Acme", validation: true, login: true));
        yield return ("MasterScreenTemplate (no login)",
            MasterScreenTemplate.Source("Acme", validation: true, login: false));
        yield return ("ViewModelTemplates.EntityViewModel (login)",
            ViewModelTemplates.EntityViewModel("Acme", validation: true, login: true));
        yield return ("ViewModelTemplates.EntityViewModel (no login)",
            ViewModelTemplates.EntityViewModel("Acme", validation: true, login: false));
    }

    /// <summary>
    /// Scaffolds a real Blazor app with a login into a fresh temp workspace, hands
    /// <paramref name="assert"/> the workspace and the project inside it, and deletes the
    /// workspace afterwards whatever happened.
    /// </summary>
    private static void InAScaffoldedBlazorLoginApp(Action<string, string> assert)
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("blazor", out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        try
        {
            // Constructed directly rather than through ScaffoldRequest.For, which hard-codes
            // AppCustomization.Default: explicit here so this test does not depend silently on
            // Default's own Login value never changing.
            var request = new ScaffoldRequest(
                TestModels.OneEntity(), "Shop", workspace, target, PersistenceKind.Json,
                ImportedData: null, AppCustomization.Default with { Login = true }, DateTime.Now.Year);

            var result = new AppScaffolder().Scaffold(request);

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            assert(workspace, result.ProjectPath);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
