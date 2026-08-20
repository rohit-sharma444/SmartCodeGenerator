using System.Collections;
using System.Reflection;
using AppScaffolding.Auth;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>Seeding runs at every application start, so "does it run twice" is the whole
/// test.</summary>
[TestClass]
public class UserSeedTemplateTests
{
    private static Assembly Emit()
    {
        var source = EmittedSource.Flatten(
            [
                .. AuthServiceTemplateTests.AuthCoreFragments,
                UserSeedTemplate.Source("Acme").Replace("namespace Acme.Auth;", string.Empty),
            ]);

        EmittedSource.AssertCompiles(source);

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);

        // Each Emit compiles under a fresh assembly name and so gets its own statics, but the
        // clear makes that independence a property of the test rather than of the compiler.
        Rows(assembly).Clear();

        return assembly;
    }

    private static IList Rows(Assembly assembly) => AuthServiceTemplateTests.Rows(assembly);

    private static void Seed(Assembly assembly)
        => assembly.GetType("UserSeed")!.GetMethod("EnsureAdminSeeded")!.Invoke(null, null);

    private static string Outcome(Assembly assembly, string userName, string password)
    {
        var result = assembly.GetType("AuthService")!.GetMethod("SignIn")!
            .Invoke(null, [userName, password])!;

        return result.GetType().GetProperty("Outcome")!.GetValue(result)!.ToString()!;
    }

    [TestMethod]
    public void The_first_run_creates_the_admin_account()
    {
        var assembly = Emit();

        Seed(assembly);

        Assert.HasCount(1, Rows(assembly));
    }

    /// <summary>The seeded account must be able to reach the Users screen; seeding a plain User
    /// would lock the first run out of account management entirely.</summary>
    [TestMethod]
    public void The_seeded_account_is_an_administrator_and_can_sign_in()
    {
        var assembly = Emit();
        Seed(assembly);

        var result = assembly.GetType("AuthService")!.GetMethod("SignIn")!
            .Invoke(null, ["admin", "admin"])!;

        Assert.AreEqual(
            "Succeeded", result.GetType().GetProperty("Outcome")!.GetValue(result)!.ToString());

        var user = result.GetType().GetProperty("User")!.GetValue(result)!;

        Assert.AreEqual("Admin", user.GetType().GetProperty("Role")!.GetValue(user)!.ToString());
    }

    /// <summary>Seeding runs on every start. A second admin on the second run would be a new
    /// account with the default password, which is a back door.</summary>
    [TestMethod]
    public void A_second_run_creates_nothing()
    {
        var assembly = Emit();

        Seed(assembly);
        Seed(assembly);

        Assert.HasCount(1, Rows(assembly));
    }

    /// <summary>Once someone has changed the admin password, a later start must not put the
    /// default back.</summary>
    [TestMethod]
    public void A_changed_password_survives_the_next_start()
    {
        var assembly = Emit();
        Seed(assembly);

        assembly.GetType("AuthService")!.GetMethod("ChangePassword")!
            .Invoke(null, [1, "chosen"]);

        Seed(assembly);

        Assert.AreEqual("WrongPassword", Outcome(assembly, "admin", "admin"));
        Assert.AreEqual("Succeeded", Outcome(assembly, "admin", "chosen"));
    }

    /// <summary>Someone who renamed the administrator must not get a fresh admin/admin back on the
    /// next start — that would be a back door into an app that looked locked down.</summary>
    [TestMethod]
    public void A_renamed_administrator_does_not_bring_the_default_back()
    {
        var assembly = Emit();
        Seed(assembly);

        var user = Rows(assembly)[0]!;
        user.GetType().GetProperty("UserName")!.SetValue(user, "rohit");

        Seed(assembly);

        Assert.HasCount(1, Rows(assembly));
        Assert.AreEqual("UnknownUser", Outcome(assembly, "admin", "admin"));
    }

    /// <summary>The hint on the login screen names the seeded credentials. Two literals would
    /// drift the day someone changes the default.</summary>
    [TestMethod]
    public void The_first_run_hint_names_the_seeded_credentials()
    {
        StringAssert.Contains(UserSeedTemplate.FirstRunHint, "admin");
        StringAssert.Contains(UserSeedTemplate.Source("Acme"), "DefaultUserName = \"admin\"");
        StringAssert.Contains(UserSeedTemplate.Source("Acme"), UserSeedTemplate.FirstRunHint);
    }
}
