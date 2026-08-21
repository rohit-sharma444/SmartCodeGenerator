namespace AppScaffolding.Auth;

/// <summary>Emits <c>Auth/PasswordHasher.cs</c>: PBKDF2 password hashing for a generated app.</summary>
/// <remarks>
/// Framework only — <c>Rfc2898DeriveBytes.Pbkdf2</c> is in System.Security.Cryptography, so a
/// generated app takes no package reference for this. The parameters are named constants in the
/// emitted file rather than inlined, because a future app that needs to raise the iteration count
/// has to be able to find it.
/// </remarks>
public static class PasswordHasherTemplate
{
    /// <summary>Source of <c>Auth/PasswordHasher.cs</c> in <paramref name="rootNamespace"/>.Auth.</summary>
    public static string Source(string rootNamespace) => $$"""
using System;
using System.Security.Cryptography;

namespace {{rootNamespace}}.Auth;

/// <summary>Hashes and verifies passwords with PBKDF2. Nothing reversible is ever stored.</summary>
public static class PasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 100_000;

    /// <summary>A fresh hash and salt for <paramref name="password"/>, both Base64.</summary>
    public static (string Hash, string Salt) Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);

        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password ?? string.Empty, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    /// <summary>
    /// Whether <paramref name="password"/> is the one <paramref name="hash"/> was made from.
    /// </summary>
    /// <remarks>
    /// False rather than an exception for a stored value that is not usable Base64 or is the wrong
    /// length: a hand-edited store should refuse the sign-in, not crash the application on its
    /// login screen. The comparison is fixed-time, so the answer takes the same time whether the
    /// first byte differs or the last.
    /// </remarks>
    public static bool Verify(string password, string hash, string salt)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt))
            return false;

        byte[] saltBytes;
        byte[] expected;

        try
        {
            saltBytes = Convert.FromBase64String(salt);
            expected = Convert.FromBase64String(hash);
        }
        catch (FormatException)
        {
            return false;
        }

        if (expected.Length != HashBytes || saltBytes.Length == 0)
            return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password ?? string.Empty, saltBytes, Iterations, HashAlgorithmName.SHA256, HashBytes);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
""";
}
