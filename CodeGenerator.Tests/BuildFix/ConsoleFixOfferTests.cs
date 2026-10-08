using AppScaffolding;
using CodeGenerationEngine.Helper;
using CodeGenerator.App;
using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class ConsoleFixOfferTests
{
    private sealed class FakeFixer(bool green) : IBuildFixer
    {
        public int Calls;
        public string? Path;
        public ProcessResult? Failed;
        public ProcessResult Last { get; } = new(green ? 0 : 1, "last", "", false);

        public FixOutcome Fix(string p, ProcessResult failed, Func<ProcessResult> rebuild, Action<string> progress)
        {
            Calls++;
            Path = p;
            Failed = failed;
            return new FixOutcome(green, 1, [], [], [], Last);
        }
    }

    private sealed class ThrowingFixer : IBuildFixer
    {
        public FixOutcome Fix(string p, ProcessResult failed, Func<ProcessResult> rebuild, Action<string> progress)
            => throw new InvalidOperationException("boom");
    }

    private static (ScaffoldResult, ProcessResult) Failed(string root, int errors = 1)
    {
        var text = string.Join(
            "\n", Enumerable.Range(1, errors).Select(i => $@"{root}\A.cs({i},1): error CS1002: ; expected"));

        return (new ScaffoldResult(true, root, [], ""), FixTestSupport.Result(text));
    }

    [TestMethod]
    public void OfferToFix_Yes_RunsTheFixerOnce_AndReturnsExactlyItsLastBuild()
    {
        var (scaffold, failed) = Failed(FixTestSupport.TempProject());
        var fixer = new FakeFixer(green: true);

        var result = BuildAppCommand.OfferToFix(scaffold, failed, (t, o, d) => 0, fixer);

        Assert.AreEqual(1, fixer.Calls);
        Assert.AreEqual(scaffold.ProjectPath, fixer.Path);
        Assert.AreSame(failed, fixer.Failed);
        Assert.AreSame(fixer.Last, result);
        Assert.IsTrue(BuildReport.Succeeded(result!));
    }

    [TestMethod]
    public void OfferToFix_StillRed_ReturnsTheRedBuild()
    {
        var (scaffold, failed) = Failed(FixTestSupport.TempProject());
        var fixer = new FakeFixer(green: false);

        var result = BuildAppCommand.OfferToFix(scaffold, failed, (t, o, d) => 0, fixer);

        Assert.AreSame(fixer.Last, result);
        Assert.IsFalse(BuildReport.Succeeded(result!));
    }

    [TestMethod]
    public void OfferToFix_No_DoesNotTouchAnything()
    {
        var (scaffold, failed) = Failed(FixTestSupport.TempProject());
        var fixer = new FakeFixer(green: true);

        var result = BuildAppCommand.OfferToFix(scaffold, failed, (t, o, d) => 1, fixer);

        Assert.AreEqual(0, fixer.Calls);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void OfferToFix_Cancelled_IsNo()
    {
        var (scaffold, failed) = Failed(FixTestSupport.TempProject());
        var fixer = new FakeFixer(green: true);

        Assert.IsNull(BuildAppCommand.OfferToFix(scaffold, failed, (t, o, d) => null, fixer));
        Assert.AreEqual(0, fixer.Calls);
    }

    [TestMethod]
    public void OfferToFix_AsksOnce_WithTheExactTitleOptionsAndDefaultYes()
    {
        var (scaffold, failed) = Failed(FixTestSupport.TempProject());
        var asked = 0;
        string? title = null;
        IReadOnlyList<string>? options = null;
        int? defaultIndex = null;

        BuildAppCommand.OfferToFix(
            scaffold, failed,
            (t, o, d) => { asked++; title = t; options = o; defaultIndex = d; return 1; },
            new FakeFixer(true));

        Assert.AreEqual(1, asked);
        Assert.AreEqual("The build failed with 1 error. Fix them?", title);
        Assert.AreSame(BuildFixReport.Options, options);
        Assert.AreEqual(0, defaultIndex);
    }

    [TestMethod]
    public void OfferToFix_Title_IsPlural_ForTwoErrors()
    {
        var (scaffold, failed) = Failed(FixTestSupport.TempProject(), errors: 2);
        string? title = null;

        BuildAppCommand.OfferToFix(scaffold, failed, (t, o, d) => { title = t; return 1; }, new FakeFixer(true));

        Assert.AreEqual("The build failed with 2 errors. Fix them?", title);
    }

    [TestMethod]
    public void OfferToFix_NoParsableErrors_DoesNotAsk()
    {
        var scaffold = new ScaffoldResult(true, FixTestSupport.TempProject(), [], "");
        var failed = FixTestSupport.Result("Build FAILED.");
        var asked = false;
        var fixer = new FakeFixer(true);

        var result = BuildAppCommand.OfferToFix(scaffold, failed, (t, o, d) => { asked = true; return 0; }, fixer);

        Assert.IsFalse(asked);
        Assert.AreEqual(0, fixer.Calls);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void OfferToFix_FixerThrows_ReturnsNull_AndDoesNotCrash()
    {
        var (scaffold, failed) = Failed(FixTestSupport.TempProject());

        var result = BuildAppCommand.OfferToFix(scaffold, failed, (t, o, d) => 0, new ThrowingFixer());

        Assert.IsNull(result);
    }

    [TestMethod]
    public void OfferToFix_ChooseThrows_ReturnsNull_AndNeverFixes()
    {
        var (scaffold, failed) = Failed(FixTestSupport.TempProject());
        var fixer = new FakeFixer(true);

        var result = BuildAppCommand.OfferToFix(
            scaffold, failed, (t, o, d) => throw new IOException("console gone"), fixer);

        Assert.IsNull(result);
        Assert.AreEqual(0, fixer.Calls);
    }

    [TestMethod]
    public void ShouldOfferFix_OnlyForAFinishedFailedBuild()
    {
        Assert.IsTrue(BuildAppCommand.ShouldOfferFix(new ProcessResult(1, "error", "", false)));
        Assert.IsFalse(BuildAppCommand.ShouldOfferFix(new ProcessResult(1, "error", "", true)));
        Assert.IsFalse(BuildAppCommand.ShouldOfferFix(new ProcessResult(0, "Build succeeded.", "", false)));
    }

    [TestMethod]
    public void Rebuilt_ReplacesTheFailedBuild_OrKeepsItWhenNothingRan()
    {
        var failed = new ProcessResult(1, "old", "", false);
        var green = new ProcessResult(0, "new", "", false);

        Assert.AreSame(green, BuildAppCommand.Rebuilt(failed, green));
        Assert.AreSame(failed, BuildAppCommand.Rebuilt(failed, null));
    }
}
