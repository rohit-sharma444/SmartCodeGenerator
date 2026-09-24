using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>
/// A generated app with a login refuses a write from nobody, and its generated tests used to BE
/// nobody: a Blazor app built with the default login failed its own round-trip and validator
/// tests with "You are not permitted to insert a Customer record." These run the emitted
/// TestSupport against the emitted data layer, for each of the three acting-user shapes.
/// </summary>
[TestClass]
public class GeneratedTestsActAsAdministratorTests
{
    [TestMethod]
    public void A_sessionless_login_app_refuses_nobody_and_allows_whoever_ActAs_names()
    {
        using var app = EmittedDataLayer.Compile(EmittedLogin.Sessionless);
        var repository = app.NewRepository();

        var refused = EmittedDataLayer.Call(repository, "Create", app.NewItem());
        Assert.IsFalse(refused.Success, "a login app must refuse a write from nobody");
        StringAssert.Contains(string.Join("; ", refused.Errors), "not permitted");

        using (var scope = (IDisposable)app.Invoke("Acme.Validation.ActingUser", "ActAs", "Admin", null)!)
        {
            var allowed = EmittedDataLayer.Call(repository, "Create", app.NewItem());
            Assert.IsTrue(allowed.Success, string.Join("; ", allowed.Errors));
        }

        Assert.IsNull(app.Invoke("Acme.Validation.ActingUser", "Role"), "the scope must end");
    }

    [TestMethod]
    public void The_generated_tests_can_write_to_a_sessionless_login_app()
        => AssertTestSupportCanWrite(EmittedLogin.Sessionless);

    [TestMethod]
    public void The_generated_tests_can_write_to_a_desktop_login_app()
        => AssertTestSupportCanWrite(EmittedLogin.Session);

    [TestMethod]
    public void The_generated_tests_can_write_to_an_app_with_no_login()
        => AssertTestSupportCanWrite(EmittedLogin.None);

    [TestMethod]
    public void The_generated_tests_act_as_the_administrator_when_there_is_a_login()
    {
        foreach (var login in new[] { EmittedLogin.Session, EmittedLogin.Sessionless })
        {
            using var app = WithTestSupport(login);

            Assert.AreEqual("Admin", ActingRole(app), login.ToString());
        }

        using var noLogin = WithTestSupport(EmittedLogin.None);
        Assert.IsNull(ActingRole(noLogin), "with no login there is nobody to act as");
    }

    [TestMethod]
    public void The_round_trip_acts_as_the_administrator_around_every_write()
    {
        var source = TestProjectTemplates.RoundTripTests("Shop", new[] { "Customer" });

        var acting = source.IndexOf("using var acting = TestSupport.ActAsAdministrator();", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, acting, "the round trip must say who is acting");

        // Before Fill, too: Fill creates a real row for a foreign key, through a repository.
        Assert.IsLessThan(source.IndexOf("TestSupport.Fill(item);", StringComparison.Ordinal), acting);
    }

    [TestMethod]
    public void The_validator_tests_carry_the_acting_role_in_every_context()
    {
        var source = TestProjectTemplates.ValidatorTests("Shop", "Customer", "Name");

        // ValidationContext.For carries no role, which a login app's Security stage refuses
        // before any field rule runs -- so "Name is required." would never be reported either.
        Assert.DoesNotContain("ValidationContext<Customer>.For(", source);
        Assert.AreEqual(3, source.Split("TestSupport.ActingRole").Length - 1);
    }

    /// <summary>The wiring: the scaffolder has to hand the test templates the app's own login
    /// shape, or a Blazor app's tests are emitted as if it had none.</summary>
    [TestMethod]
    public void A_scaffolded_blazor_login_apps_tests_act_through_ActingUser()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("blazor", out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        try
        {
            var request = new ScaffoldRequest(
                TestModels.OneEntity(), "Shop", workspace, target, PersistenceKind.Json,
                ImportedData: null, AppCustomization.Default with { Login = true }, DateTime.Now.Year);

            var result = new AppScaffolder().Scaffold(request);
            Assert.IsTrue(result.Success, result.Message);
            Assert.IsNotNull(result.TestProjectPath, "the Blazor target creates a test project");

            var support = File.ReadAllText(
                Path.Combine(Path.GetDirectoryName(result.TestProjectPath)!, "TestSupport.cs"));

            StringAssert.Contains(support, "ActingUser.ActAs(");
            StringAssert.Contains(support, "AppRole.Admin");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Inside TestSupport.ActAsAdministrator, a Create through the real repository
    /// succeeds; and the scope ends when it is disposed.</summary>
    private static void AssertTestSupportCanWrite(EmittedLogin login)
    {
        using var app = WithTestSupport(login);
        var repository = app.NewRepository();

        using (var scope = (IDisposable)app.Invoke("Acme.Tests.TestSupport", "ActAsAdministrator")!)
        {
            var created = EmittedDataLayer.Call(repository, "Create", app.NewItem());
            Assert.IsTrue(created.Success, login + ": " + string.Join("; ", created.Errors));

            var updated = EmittedDataLayer.Call(repository, "Update", created.Item!);
            Assert.IsTrue(updated.Success, login + ": " + string.Join("; ", updated.Errors));
        }

        if (login != EmittedLogin.None)
        {
            var afterwards = EmittedDataLayer.Call(repository, "Create", app.NewItem());
            Assert.IsFalse(afterwards.Success, login + ": the administrator must not outlive the scope");
        }
    }

    private static EmittedDataLayer WithTestSupport(EmittedLogin login)
        => EmittedDataLayer.Compile(
            login,
            validation: true,
            ("Tests/TestSupport.cs", TestProjectTemplates.TestSupport(
                "Acme", login: login != EmittedLogin.None, sessionAuth: login == EmittedLogin.Session)));

    private static string? ActingRole(EmittedDataLayer app)
        => (string?)app.Assembly.GetType("Acme.Tests.TestSupport")!
            .GetProperty("ActingRole", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
            .GetValue(null);
}
