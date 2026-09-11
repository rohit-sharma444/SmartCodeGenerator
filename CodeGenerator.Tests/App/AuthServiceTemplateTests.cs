using System.Collections;
using System.Reflection;
using AppScaffolding.Auth;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Sign-in decides who gets in, so every test here runs the emitted code against a real in-memory
/// store. A compile test would pass just as happily on a service that returns Succeeded always.
/// </summary>
[TestClass]
public class AuthServiceTemplateTests
{
    /// <summary>
    /// An <c>IStore&lt;T&gt;</c> over a static list, standing in for JsonStore.
    /// </summary>
    /// <remarks>
    /// The emitted AuthService reaches its store through <c>StoreFactory</c>, so the fake replaces
    /// StoreFactory rather than being injected — a generated app has no container. Declared with no
    /// namespace because the emitted files are flattened into the global one.
    /// </remarks>
    internal const string StoreStub = """
using System.Collections.Generic;
using System.Linq;

public sealed record StoreWrite(bool Success, string? Refusal)
{
    public static StoreWrite Ok { get; } = new(true, null);
}

public interface IStore<T> where T : new()
{
    List<T> GetAll();
    StoreWrite SaveAll(List<T> all);
    int NextId(IEnumerable<T> existing);
    T NewItem(IEnumerable<T> existing);
}

public sealed class MemoryStore<T> : IStore<T> where T : class, new()
{
    public static readonly List<T> Rows = new();
    public List<T> GetAll() => Rows.ToList();
    public StoreWrite SaveAll(List<T> all) { Rows.Clear(); Rows.AddRange(all); return StoreWrite.Ok; }
    public int NextId(IEnumerable<T> existing) => Rows.Count + 1;
    public T NewItem(IEnumerable<T> existing) => new();
}

public static class StoreFactory
{
    public static IStore<T> Create<T>() where T : class, new() => new MemoryStore<T>();
}
""";

    /// <summary>The auth-core files below AuthService, flattened into the global namespace.
    /// Shared with <see cref="UserSeedTemplateTests"/>, which needs the same foundation.</summary>
    internal static string[] AuthCoreFragments =>
    [
        StoreStub,
        AuthModelTemplates.Role("Acme").Replace("namespace Acme.Auth;", string.Empty),
        AuthModelTemplates.User("Acme")
            .Replace("namespace Acme.Models;", string.Empty)
            .Replace("using Acme.Auth;", string.Empty),
        PasswordHasherTemplate.Source("Acme").Replace("namespace Acme.Auth;", string.Empty),
        AuthServiceTemplate.Source("Acme").Replace("namespace Acme.Auth;", string.Empty),
    ];

    private static Assembly Emit()
    {
        var source = EmittedSource.Flatten(AuthCoreFragments);

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    /// <summary>Puts one account into the MemoryStore the emitted StoreFactory hands out.</summary>
    internal static void GivenUser(
        Assembly assembly, string userName, string password, string role = "User",
        bool active = true)
    {
        var userType = assembly.GetType("User")!;
        var user = Activator.CreateInstance(userType)!;

        var created = assembly.GetType("PasswordHasher")!
            .GetMethod("Create")!.Invoke(null, [password])!;

        userType.GetProperty("Id")!.SetValue(user, 1);
        userType.GetProperty("UserName")!.SetValue(user, userName);
        userType.GetProperty("PasswordHash")!
            .SetValue(user, created.GetType().GetField("Item1")!.GetValue(created));
        userType.GetProperty("PasswordSalt")!
            .SetValue(user, created.GetType().GetField("Item2")!.GetValue(created));
        userType.GetProperty("Role")!
            .SetValue(user, Enum.Parse(assembly.GetType("AppRole")!, role));
        userType.GetProperty("IsActive")!.SetValue(user, active);

        var rows = Rows(assembly);
        rows.Clear();
        rows.Add(user);
    }

    /// <summary>The static backing list of the emitted MemoryStore&lt;User&gt;.</summary>
    internal static IList Rows(Assembly assembly)
        => (IList)assembly.GetType("MemoryStore`1")!
            .MakeGenericType(assembly.GetType("User")!)
            .GetField("Rows")!.GetValue(null)!;

    private static object SignIn(Assembly assembly, string userName, string password)
        => assembly.GetType("AuthService")!.GetMethod("SignIn")!
            .Invoke(null, [userName, password])!;

    private static string Outcome(object result)
        => result.GetType().GetProperty("Outcome")!.GetValue(result)!.ToString()!;

    [TestMethod]
    public void The_right_password_signs_in()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein");

        var result = SignIn(assembly, "admin", "letmein");

        Assert.AreEqual("Succeeded", Outcome(result));
        Assert.IsTrue((bool)result.GetType().GetProperty("Succeeded")!.GetValue(result)!);
    }

    [TestMethod]
    public void The_wrong_password_does_not_sign_in()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein");

        Assert.AreEqual("WrongPassword", Outcome(SignIn(assembly, "admin", "guess")));
    }

    [TestMethod]
    public void An_unknown_user_does_not_sign_in()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein");

        Assert.AreEqual("UnknownUser", Outcome(SignIn(assembly, "nobody", "letmein")));
    }

    /// <summary>A disabled account must be refused even with the right password — otherwise
    /// IsActive is decoration.</summary>
    [TestMethod]
    public void A_disabled_account_does_not_sign_in()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein", active: false);

        Assert.AreEqual("Disabled", Outcome(SignIn(assembly, "admin", "letmein")));
    }

    /// <summary>The user name is not case-sensitive; a login that rejects "Admin" for "admin"
    /// reads as a broken password.</summary>
    [TestMethod]
    public void The_user_name_is_matched_case_insensitively()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein");

        Assert.AreEqual("Succeeded", Outcome(SignIn(assembly, "ADMIN", "letmein")));
    }

    /// <summary>Three different failures, one message. Telling the screen which of them happened
    /// tells an attacker which user names exist.</summary>
    [TestMethod]
    public void Every_failure_reports_the_same_message()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein", active: false);

        var expected = (string)assembly.GetType("SignInResult")!
            .GetField("FailureMessage")!.GetValue(null)!;

        foreach (var (user, password) in new[]
                 { ("admin", "guess"), ("nobody", "letmein"), ("admin", "letmein") })
        {
            var result = SignIn(assembly, user, password);

            Assert.AreEqual(
                expected, result.GetType().GetProperty("Message")!.GetValue(result));
        }
    }

    /// <summary>A failed sign-in must not hand back the record it refused.</summary>
    [TestMethod]
    public void A_failed_sign_in_returns_no_user()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein");

        var result = SignIn(assembly, "admin", "guess");

        Assert.IsNull(result.GetType().GetProperty("User")!.GetValue(result));
    }

    [TestMethod]
    public void A_changed_password_replaces_the_old_one()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein");

        Assert.IsTrue((bool)assembly.GetType("AuthService")!
            .GetMethod("ChangePassword")!.Invoke(null, [1, "newsecret"])!);

        Assert.AreEqual("WrongPassword", Outcome(SignIn(assembly, "admin", "letmein")));
        Assert.AreEqual("Succeeded", Outcome(SignIn(assembly, "admin", "newsecret")));
    }
}
