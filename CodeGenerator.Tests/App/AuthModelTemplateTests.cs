using System.Reflection;
using AppScaffolding.Auth;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>The user record rides IStore&lt;T&gt; like any other entity, so it must satisfy the
/// same constraint every store method declares: <c>where T : class, new()</c>.</summary>
[TestClass]
public class AuthModelTemplateTests
{
    /// <summary>
    /// Both files as ONE compilation unit, so the test proves User can actually see AppRole.
    /// </summary>
    /// <remarks>
    /// The namespaces are stripped by hand because <see cref="EmittedSource.Flatten"/> only knows
    /// the Acme.Models / Acme.Data / Acme.Validation names its own callers emit, and these two
    /// files declare Acme.Auth as well. Everything therefore lands in the global namespace, which
    /// is why the types below are looked up by their bare names.
    /// </remarks>
    private static Assembly EmitBoth()
    {
        var flattened = EmittedSource.Flatten(
            AuthModelTemplates.Role("Acme").Replace("namespace Acme.Auth;", string.Empty),
            AuthModelTemplates.User("Acme")
                .Replace("namespace Acme.Models;", string.Empty)
                .Replace("using Acme.Auth;", string.Empty));

        EmittedSource.AssertCompiles(flattened);

        return Assembly.Load(Compiler.Compile(flattened).AssemblyBytes!);
    }

    [TestMethod]
    public void The_role_has_exactly_two_values()
    {
        var role = EmitBoth().GetType("AppRole")!;

        Assert.IsTrue(role.IsEnum);
        CollectionAssert.AreEquivalent(new[] { "Admin", "User" }, Enum.GetNames(role));
    }

    [TestMethod]
    public void The_user_carries_the_fields_the_login_needs()
    {
        var user = EmitBoth().GetType("User")!;

        foreach (var name in new[]
                 {
                     "Id", "UserName", "DisplayName", "PasswordHash", "PasswordSalt",
                     "Role", "IsActive",
                 })
        {
            Assert.IsNotNull(user.GetProperty(name), $"User is missing {name}.");
        }
    }

    /// <summary>IStore&lt;T&gt; declares <c>where T : class, new()</c>. A User with only a
    /// non-default constructor would fail to compile at every StoreFactory.Create call site, and
    /// nothing in this file would have said so.</summary>
    [TestMethod]
    public void The_user_has_a_parameterless_constructor()
        => Assert.IsNotNull(EmitBoth().GetType("User")!.GetConstructor(Type.EmptyTypes));

    /// <summary>A new account must be usable, and must not be an administrator by accident.</summary>
    [TestMethod]
    public void A_new_user_defaults_to_the_plain_role_and_is_active()
    {
        var assembly = EmitBoth();
        var user = Activator.CreateInstance(assembly.GetType("User")!)!;

        Assert.AreEqual("User", user.GetType().GetProperty("Role")!.GetValue(user)!.ToString());
        Assert.IsTrue((bool)user.GetType().GetProperty("IsActive")!.GetValue(user)!);
    }

    /// <summary>The password itself is never a property. If one is ever added, the store writes
    /// plaintext to disk.</summary>
    [TestMethod]
    public void The_user_has_no_plaintext_password_property()
    {
        var user = EmitBoth().GetType("User")!;

        Assert.IsNull(user.GetProperty("Password"));
        Assert.IsNull(user.GetProperty("PlainPassword"));
    }
}
