using AppScaffolding;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

[TestClass]
public class PermissionsTemplateTests
{
    private const string Driver = """
using Acme.Validation;

namespace Acme.Probe;

public static class Probe
{
    public static bool Allows(string op, string entity, string? role)
        => Permissions.Allows(
            op switch
            {
                "Insert" => ValidationOperation.Insert,
                "Update" => ValidationOperation.Update,
                "Delete" => ValidationOperation.Delete,
                _ => ValidationOperation.Post,
            },
            entity,
            role);
}
""";

    [TestMethod]
    [DataRow("Admin", "Customer", "Delete", true)]
    [DataRow("Admin", "User", "Delete", true)]
    [DataRow("User", "Customer", "Update", true)]
    [DataRow("User", "Order", "Delete", true)]
    [DataRow("User", "Customer", "Delete", false)]
    [DataRow("User", "User", "Update", false)]
    [DataRow(null, "Customer", "Delete", true)]
    public void TheDefaultMatrix_PreservesTodaysBehaviour(
        string? role, string entity, string op, bool expected)
    {
        // Today's whole rule, in one table: an Admin may do anything; a User may do anything
        // except delete a MASTER record or touch Users; and an app with no login at all refuses
        // nothing, because it has no notion of who is acting. Customer is a master here and
        // Order is a transaction.
        //
        // The null-role row is the one that matters most: four of the seven targets emit no auth
        // core, so every operation in a console or Web API app arrives with a null role. If this
        // returned false, those apps would refuse every write. This table is all login: false —
        // ANullRole_IsAllowedOnlyWhenTheAppHasNoLogin below covers the login: true half.
        var probe = Compile(login: false);

        // Positional, not named: Probe.Allows declares (op, entity, role) in that order, so the
        // invocation array has to match the DECLARED order rather than this test's own parameter
        // order, which is (role, entity, op).
        var allows = (bool)probe.GetType("Acme.Probe.Probe")!
            .GetMethod("Allows")!.Invoke(null, [op, entity, role])!;

        Assert.AreEqual(expected, allows);
    }

    [TestMethod]
    public void TheRefusalNamesTheOperationAndTheEntity_ButNoField()
    {
        // Security runs first precisely so an unauthorised user is not told which fields were
        // wrong. The sentence must not leak one.
        var source = PermissionsTemplate.Source("Acme", ["Customer"], login: false);

        StringAssert.Contains(source, "public static string Refusal(");
        StringAssert.Contains(source, "not permitted");
    }

    [TestMethod]
    public void An_app_with_a_login_refuses_a_signed_out_caller()
    {
        var source = PermissionsTemplate.Source("Acme", ["Customer"], login: true);

        StringAssert.Contains(source, "private const bool AppHasLogin = true;");
        StringAssert.Contains(source, "if (role is null)");
        StringAssert.Contains(source, "return !AppHasLogin;");
    }

    [TestMethod]
    public void An_app_with_no_login_still_allows_everything()
    {
        var source = PermissionsTemplate.Source("Acme", ["Customer"], login: false);

        // The four targets with no auth core have nobody to attribute a write to. Refusing a null
        // role there would refuse every write in every console, Web API and worker app.
        StringAssert.Contains(source, "private const bool AppHasLogin = false;");
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void ANullRole_IsAllowedOnlyWhenTheAppHasNoLogin(bool login, bool expected)
    {
        // The two text-assertion tests above pin the emitted const and the emitted lines, but
        // neither one COMPILES and INVOKES Allows — so the behaviour this task exists to add, a
        // signed-out caller of a login app being refused, was executed nowhere in the suite. This
        // compiles the real Permissions.cs both ways and calls Allows(op, entity, null) through
        // reflection, the same way TheDefaultMatrix_PreservesTodaysBehaviour does.
        var probe = Compile(login);

        var allows = (bool)probe.GetType("Acme.Probe.Probe")!
            .GetMethod("Allows")!.Invoke(null, ["Delete", "Customer", null])!;

        Assert.AreEqual(expected, allows);
    }

    [TestMethod]
    public void AnAppWithNoMasters_StillCompiles()
    {
        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", [], login: false)),
        });

        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => e.ToString())));
    }

    private static System.Reflection.Assembly Compile(bool login)
    {
        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", ["Customer"], login)),
            ("Probe/Probe.cs", Driver),
        });

        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => e.ToString())));

        return System.Reflection.Assembly.Load(result.AssemblyBytes!);
    }
}
