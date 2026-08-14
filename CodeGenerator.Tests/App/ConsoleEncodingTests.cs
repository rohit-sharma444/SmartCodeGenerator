using System.Text;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Switching the console to UTF-8 must never be able to stop the tool starting.
/// </summary>
/// <remarks>
/// The encoding actually reaching a real console cannot be asserted from a test host, which has no
/// console attached. What is testable is the contract that matters: it does not throw, whatever the
/// host, and it is safe to call more than once.
/// </remarks>
[TestClass]
public class ConsoleEncodingTests
{
    [TestMethod]
    public void UseUtf8DoesNotThrowInAHostWithNoConsole()
    {
        // Would fail before the catch existed: the test host has no console, and setting
        // OutputEncoding there is exactly the case that throws.
        ConsoleEncoding.UseUtf8();
    }

    [TestMethod]
    public void UseUtf8IsIdempotent()
    {
        var first = ConsoleEncoding.UseUtf8();
        var second = ConsoleEncoding.UseUtf8();

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void TheCharactersTheCliEmitsSurviveUtf8()
    {
        // The three that motivated this: the em dash used throughout, and the arrows in the
        // picker's hint, which Windows-1252 cannot represent at all.
        const string emitted = "— ↑ ↓";

        var utf8 = new UTF8Encoding(false);

        Assert.AreEqual(emitted, utf8.GetString(utf8.GetBytes(emitted)));

        // And the demonstration that the fix is not cosmetic: a single-byte legacy encoding cannot
        // carry any of the three, so they arrive as replacement characters.
        //
        // Latin1 rather than code page 1252, which was the first attempt: .NET Core does not
        // register 1252 without CodePagesEncodingProvider, so GetEncoding(1252) throws
        // NotSupportedException. Latin1 is built in and makes the same point.
        var legacy = Encoding.Latin1;

        Assert.AreNotEqual(emitted, legacy.GetString(legacy.GetBytes(emitted)));
    }
}
