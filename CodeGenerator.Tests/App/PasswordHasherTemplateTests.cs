using System.Reflection;
using AppScaffolding.Auth;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The hasher is the one emitted file where a compile test proves almost nothing: a hasher that
/// returns the password unchanged compiles perfectly. Every test here loads the emitted assembly
/// and calls it.
/// </summary>
[TestClass]
public class PasswordHasherTemplateTests
{
    private static (MethodInfo Create, MethodInfo Verify) Emit()
    {
        var source = PasswordHasherTemplate.Source("Acme");

        EmittedSource.AssertCompiles(source);

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
        var type = assembly.GetType("Acme.Auth.PasswordHasher")!;

        return (type.GetMethod("Create")!, type.GetMethod("Verify")!);
    }

    /// <summary>Unpacks the emitted ValueTuple, whose fields are Item1/Item2 to reflection
    /// however they are named in source.</summary>
    private static (string Hash, string Salt) Create(MethodInfo create, string password)
    {
        var tuple = create.Invoke(null, [password])!;
        var hash = (string)tuple.GetType().GetField("Item1")!.GetValue(tuple)!;
        var salt = (string)tuple.GetType().GetField("Item2")!.GetValue(tuple)!;

        return (hash, salt);
    }

    [TestMethod]
    public void A_created_hash_verifies_against_its_own_password()
    {
        var (create, verify) = Emit();
        var (hash, salt) = Create(create, "correct horse");

        Assert.IsTrue((bool)verify.Invoke(null, ["correct horse", hash, salt])!);
    }

    [TestMethod]
    public void A_wrong_password_does_not_verify()
    {
        var (create, verify) = Emit();
        var (hash, salt) = Create(create, "correct horse");

        Assert.IsFalse((bool)verify.Invoke(null, ["wrong horse", hash, salt])!);
    }

    /// <summary>The point of the salt. Two accounts sharing a password must not share a hash, or
    /// one cracked hash reveals every account that reused the password.</summary>
    [TestMethod]
    public void The_same_password_hashes_differently_for_two_users()
    {
        var (create, _) = Emit();

        var first = Create(create, "same");
        var second = Create(create, "same");

        Assert.AreNotEqual(first.Salt, second.Salt);
        Assert.AreNotEqual(first.Hash, second.Hash);
    }

    /// <summary>The stored password must not be recoverable from what is written to disk.</summary>
    [TestMethod]
    public void The_stored_hash_does_not_contain_the_password()
    {
        var (create, _) = Emit();
        var (hash, salt) = Create(create, "plaintext");

        Assert.IsFalse(hash.Contains("plaintext", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(salt.Contains("plaintext", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A store hand-edited to nonsense must fail the sign-in, not crash the app.</summary>
    [TestMethod]
    public void A_malformed_stored_hash_fails_instead_of_throwing()
    {
        var (_, verify) = Emit();

        Assert.IsFalse((bool)verify.Invoke(null, ["anything", "not base64!", "also not"])!);
        Assert.IsFalse((bool)verify.Invoke(null, ["anything", "", ""])!);
    }
}
