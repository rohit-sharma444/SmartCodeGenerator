using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// What an unexpected failure says to the user.
/// </summary>
/// <remarks>
/// It used to print the exception's type name — "Unexpected error [InvalidOperationException]" —
/// which tells a user nothing they can act on and reads like a crash. The type is still available,
/// behind CG_VERBOSE, because whoever ends up debugging it does need it.
/// </remarks>
[TestClass]
public class ErrorTextTests
{
    private static Exception Thrown()
    {
        try
        {
            // Thrown rather than constructed, so there is a real stack trace to include or omit.
            throw new InvalidOperationException("Sequence contains no elements");
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static string Text(bool verbose)
        => string.Join("\n", ErrorText.Lines(Thrown(), verbose));

    [TestMethod]
    public void TheMessageIsKept()
    {
        // The exception's own message is often the only actionable part; dropping it would trade
        // one unhelpful output for another.
        Assert.Contains("Sequence contains no elements", Text(verbose: false));
    }

    [TestMethod]
    public void TheTypeNameIsNotShownByDefault()
    {
        Assert.DoesNotContain("InvalidOperationException", Text(verbose: false));
    }

    [TestMethod]
    public void NoStackTraceByDefault()
    {
        var text = Text(verbose: false);

        Assert.DoesNotContain("   at ", text);
        Assert.DoesNotContain("ErrorTextTests", text);
    }

    [TestMethod]
    public void TheWayToGetDetailIsNamed()
    {
        // A dead end is what makes an error frustrating. Say how to learn more.
        Assert.Contains("CG_VERBOSE", Text(verbose: false));
    }

    [TestMethod]
    public void VerboseAddsTheTypeAndTheStack()
    {
        var text = Text(verbose: true);

        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("Sequence contains no elements", text);
        Assert.Contains("ErrorTextTests", text);
    }

    [TestMethod]
    public void VerboseDoesNotAdvertiseWhatIsAlreadyOn()
    {
        Assert.DoesNotContain("CG_VERBOSE", Text(verbose: true));
    }

    [TestMethod]
    public void AnExceptionWithNoStackIsStillReported()
    {
        // A constructed-but-never-thrown exception has a null StackTrace. Reporting must not
        // itself throw while reporting a failure.
        var lines = ErrorText.Lines(new InvalidOperationException("bare"), verbose: true);

        Assert.IsNotEmpty(lines);
        Assert.Contains("bare", string.Join("\n", lines));
    }

    [TestMethod]
    public void AnInnerExceptionIsNotLost()
    {
        // The outer message is often the vague one ("An error occurred"), and the inner is the
        // one that says what actually happened.
        var outer = new InvalidOperationException(
            "An error occurred", new FileNotFoundException("rules.json is missing"));

        Assert.Contains("rules.json is missing", string.Join("\n", ErrorText.Lines(outer, false)));
    }
}
