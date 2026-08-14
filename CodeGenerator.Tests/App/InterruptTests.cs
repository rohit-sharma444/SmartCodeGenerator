using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// What Ctrl-C does.
/// </summary>
/// <remarks>
/// Nothing handled it before: during a Wikipedia fetch or a three-minute build, Ctrl-C killed the
/// process outright — possibly mid-write, leaving a half-generated project with no indication of
/// what had completed.
/// <para>
/// The real key press cannot be raised from a test, so what is lifted out and tested is the policy:
/// the first press asks, the second insists.
/// </para>
/// </remarks>
/// <remarks>
/// <see cref="DoNotParallelizeAttribute"/> because <see cref="Interrupt"/> is deliberately static —
/// there is one console and one key press — so tests mutating it cannot run alongside each other.
/// The house convention for global state here; a dozen other classes carry the same attribute.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class InterruptTests
{
    [TestInitialize]
    public void Reset() => Interrupt.Reset();

    [TestMethod]
    public void NothingIsRequestedToStartWith()
    {
        Assert.IsFalse(Interrupt.Requested);
    }

    [TestMethod]
    public void TheFirstPressRequestsCancellationWithoutKillingTheProcess()
    {
        // False means "do not let the runtime terminate us" — the whole point, so that a write in
        // flight finishes and the workspace is left consistent.
        Assert.IsFalse(Interrupt.ShouldTerminate());
        Assert.IsTrue(Interrupt.Requested);
    }

    [TestMethod]
    public void TheSecondPressGivesUpAndLetsItDie()
    {
        // A trap that can never be escaped is worse than no trap: if the first press did not stop
        // whatever is running, the user must still be able to leave.
        Interrupt.ShouldTerminate();

        Assert.IsTrue(Interrupt.ShouldTerminate());
    }

    [TestMethod]
    public void TheTokenIsCancelledByTheFirstPress()
    {
        var token = Interrupt.Token;

        Assert.IsFalse(token.IsCancellationRequested);

        Interrupt.ShouldTerminate();

        Assert.IsTrue(token.IsCancellationRequested);
    }

    [TestMethod]
    public void ResetClearsTheRequestSoThePromptCanCarryOn()
    {
        // A cancelled command returns to the prompt, and the next one must not start already
        // cancelled.
        Interrupt.ShouldTerminate();
        Assert.IsTrue(Interrupt.Requested);

        Interrupt.Reset();

        Assert.IsFalse(Interrupt.Requested);
        Assert.IsFalse(Interrupt.Token.IsCancellationRequested);
    }

    [TestMethod]
    public void ResetGivesAFreshTokenRatherThanReusingACancelledOne()
    {
        Interrupt.ShouldTerminate();

        var cancelled = Interrupt.Token;

        Interrupt.Reset();

        Assert.AreNotEqual(cancelled, Interrupt.Token);
        Assert.IsFalse(Interrupt.Token.IsCancellationRequested);
    }
}
