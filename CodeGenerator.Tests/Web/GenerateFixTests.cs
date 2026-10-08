using System.Reflection;
using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerationEngine.Helper;
using CodeGenerator.App;
using CodeGenerator.BuildFix;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The Generate stage offers to fix a failed build, runs the fixer, and shows what came of it.
/// </summary>
/// <remarks>
/// The build is a fake (the <see cref="BuildRunner"/> seam) so a red build costs no minute and is
/// the same every run; the project is still really written, under C:\cg-t\ for the MAX_PATH reason
/// <c>GenerateAddOnlyTests</c> gives. A real red build on the page is a live check, not a unit test.
/// </remarks>
[TestClass]
public class GenerateFixTests
{
    private const string OneError = @"Models\Customer.cs(3,5): error CS1002: ; expected";
    private const string TwoErrors = OneError + "\n" + @"Models\Customer.cs(4,5): error CS1002: ; expected";

    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(@"C:\cg-t", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A lock on a generated folder, briefly.
        }
    }

    /// <summary>Returns the next result each call, and the last one for ever after.</summary>
    private sealed class FakeBuild(params ProcessResult[] results)
    {
        private int _next;
        public int Calls;

        public BuildAttempt Run(string path, Action<string> onLine)
        {
            Calls++;
            var result = results[Math.Min(_next++, results.Length - 1)];
            onLine("build line " + Calls);

            return new BuildAttempt(result, TimeSpan.FromSeconds(2));
        }
    }

    private sealed class FakeFixer : IBuildFixer
    {
        public int Calls;
        public string? Path;
        public ProcessResult? Failed;
        public string EditedBy = "removed the duplicate";
        public Action? Block;
        public Exception? Throw;

        public FixOutcome Fix(string p, ProcessResult failed, Func<ProcessResult> rebuild, Action<string> progress)
        {
            Interlocked.Increment(ref Calls);
            Path = p;
            Failed = failed;
            progress($"Fixed CS1002 (rule) in {p}\\Models\\Customer.cs");
            Block?.Invoke();

            if (Throw is not null)
                throw Throw;

            var last = rebuild();
            var green = BuildReport.Succeeded(last);

            return new FixOutcome(
                green, 1, [new FixEdit(@"Models\Customer.cs", EditedBy, 1)],
                green ? [] : BuildDiagnostics.Parse(last, p), [], last);
        }
    }

    private static ProcessResult Red(string text) => new(1, text, string.Empty, false);

    private static readonly ProcessResult Green = new(0, "Build succeeded.", string.Empty, false);

    private sealed record Page(
        IRenderedComponent<GenerateStage> Cut, BuildSession Session, StageNavigation Nav, BunitContext Context);

    /// <summary>A page whose write button has been pressed and whose first build has finished.</summary>
    private Page Written(FakeFixer? fixer, FakeBuild build)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<BuildRunner>(build.Run);

        if (fixer is not null)
            context.Services.AddSingleton<IBuildFixer>(fixer);

        var session = NewSession();
        var nav = new StageNavigation();
        var cut = context.Render<GenerateStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, nav)
            .Add(c => c.OnAdvance, () => { }));

        cut.Find("button.write").Click();
        cut.WaitForAssertion(() => Assert.IsNotNull(session.Generated), TimeSpan.FromSeconds(60));

        return new Page(cut, session, nav, context);
    }

    private BuildSession NewSession()
    {
        var session = new BuildSession("a shop", _root, AppCustomization.Default);
        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();

        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = "Customer", Provenance = Provenance.Wiki };
        entity.Fields.Add(new CandidateField { Name = "Title", Type = "string", Provenance = Provenance.Wiki });
        model.Entities.Add(entity);
        session.SetModel(model);

        while (session.Current != BuildStage.Generate)
            session.Advance();

        return session;
    }

    private static string FixReport(IRenderedComponent<GenerateStage> cut) => cut.Find("pre.fix-report").TextContent;

    private static IEnumerable<string> ReportLines(IRenderedComponent<GenerateStage> cut)
        => cut.FindAll("p.report-line").Select(p => p.TextContent);

    [TestMethod]
    public void A_failed_parsable_build_offers_the_fix_in_the_shared_words()
    {
        var page = Written(new FakeFixer(), new FakeBuild(Red(OneError)));
        using var _ = page.Context;

        Assert.AreEqual("The build failed with 1 error. Fix them?", page.Cut.Find(".fix-question").TextContent.Trim());
        Assert.AreEqual("Yes, fix them", page.Cut.Find("button.fix-yes").TextContent.Trim());
        Assert.AreEqual("No, leave them", page.Cut.Find("button.fix-no").TextContent.Trim());
        Assert.HasCount(1, page.Cut.FindAll(".fix-offer"));
    }

    [TestMethod]
    public void The_title_counts_the_errors()
    {
        var page = Written(new FakeFixer(), new FakeBuild(Red(TwoErrors)));
        using var _ = page.Context;

        Assert.AreEqual("The build failed with 2 errors. Fix them?", page.Cut.Find(".fix-question").TextContent.Trim());
    }

    [TestMethod]
    public void No_fixer_registered_means_no_offer()
    {
        var page = Written(null, new FakeBuild(Red(OneError)));
        using var _ = page.Context;

        Assert.HasCount(0, page.Cut.FindAll(".fix-offer"));
        Assert.IsTrue(ReportLines(page.Cut).Any(l => l.Contains("CS1002")), "the errors are still shown");
    }

    [TestMethod]
    public void A_green_build_offers_nothing()
    {
        var page = Written(new FakeFixer(), new FakeBuild(Green));
        using var _ = page.Context;

        Assert.HasCount(0, page.Cut.FindAll(".fix-offer"));
    }

    [TestMethod]
    public void A_timed_out_build_offers_nothing()
    {
        var page = Written(new FakeFixer(), new FakeBuild(new ProcessResult(1, OneError, string.Empty, true)));
        using var _ = page.Context;

        Assert.HasCount(0, page.Cut.FindAll(".fix-offer"));
    }

    [TestMethod]
    public void A_red_build_with_no_readable_error_offers_nothing()
    {
        var page = Written(new FakeFixer(), new FakeBuild(Red("MSBuild could not start")));
        using var _ = page.Context;

        Assert.HasCount(0, page.Cut.FindAll(".fix-offer"));
    }

    [TestMethod]
    public void No_hides_the_offer_and_keeps_the_errors_and_never_calls_the_fixer()
    {
        var fixer = new FakeFixer();
        var page = Written(fixer, new FakeBuild(Red(OneError)));
        using var _ = page.Context;

        page.Cut.Find("button.fix-no").Click();

        Assert.HasCount(0, page.Cut.FindAll(".fix-offer"));
        Assert.AreEqual(0, fixer.Calls);
        Assert.IsTrue(ReportLines(page.Cut).Any(l => l.Contains("Build FAILED") && l.Contains("1 error")));
        Assert.IsTrue(ReportLines(page.Cut).Any(l => l.Contains("CS1002")));
        Assert.IsFalse(page.Session.Generated!.BuildSucceeded);
    }

    [TestMethod]
    public void Yes_runs_the_fixer_once_and_a_green_rebuild_unlocks_the_next_step()
    {
        var fixer = new FakeFixer();
        var build = new FakeBuild(Red(OneError), Green);
        var page = Written(fixer, build);
        using var _ = page.Context;
        var project = page.Session.Generated!.Scaffold.ProjectPath;

        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForAssertion(() => Assert.IsTrue(page.Session.Generated!.BuildSucceeded), TimeSpan.FromSeconds(30));
        page.Cut.WaitForAssertion(() => StringAssert.StartsWith(FixReport(page.Cut), "The build now succeeds"));

        Assert.AreEqual(1, fixer.Calls);
        Assert.AreEqual(project, fixer.Path);
        Assert.AreEqual(OneError, fixer.Failed!.StandardOutput);
        Assert.AreEqual(2, build.Calls, "the first build and one rebuild");

        var lines = FixReport(page.Cut).Split('\n');
        Assert.AreEqual("The build now succeeds with 1 edit kept.", lines[0]);
        Assert.AreEqual(@"  Edited Models\Customer.cs: removed the duplicate", lines[1]);

        var outcome = new FixOutcome(
            true, 1, [new FixEdit(@"Models\Customer.cs", "removed the duplicate", 1)], [], [], Green);
        CollectionAssert.AreEqual(BuildFixReport.Lines(outcome, project).ToList(), lines, "same words as the console");

        Assert.HasCount(0, page.Cut.FindAll(".fix-offer"));
        Assert.IsFalse(ReportLines(page.Cut).Any(l => l.Contains("CS1002")), "the old red result is gone");
        Assert.IsTrue(ReportLines(page.Cut).Any(l => l.StartsWith("Build succeeded (2.0s)")));
        Assert.IsTrue(page.Nav.HasStep, "the box offers the way on again");
        Assert.IsTrue(GenerateStep.CanRun(page.Session.Generated, page.Session.Target!));
        StringAssert.Contains(page.Session.Generated!.BuildLog, "build line 2");
    }

    [TestMethod]
    public void A_still_red_rebuild_shows_what_remains_and_offers_another_try()
    {
        var fixer = new FakeFixer();
        var page = Written(fixer, new FakeBuild(Red(OneError), Red(TwoErrors)));
        using var _ = page.Context;

        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForAssertion(() => StringAssert.StartsWith(FixReport(page.Cut), "Partly fixed"), TimeSpan.FromSeconds(30));

        var lines = FixReport(page.Cut).Split('\n');
        Assert.AreEqual("Partly fixed: 1 edit kept.", lines[0]);
        Assert.AreEqual("The build still fails: 2 errors remain.", lines[1]);
        Assert.IsTrue(ReportLines(page.Cut).Any(l => l.Contains("2 errors") && l.StartsWith("Build FAILED")));
        Assert.IsFalse(page.Session.Generated!.BuildSucceeded);
        Assert.IsFalse(GenerateStep.CanRun(page.Session.Generated, page.Session.Target!));
        Assert.AreEqual("The build failed with 2 errors. Fix them?", page.Cut.Find(".fix-question").TextContent.Trim());
    }

    [TestMethod]
    public void A_fixer_that_throws_gives_one_plain_line_and_leaves_the_page_alive()
    {
        var fixer = new FakeFixer { Throw = new InvalidOperationException("secret C:\\x\\y.cs") };
        var page = Written(fixer, new FakeBuild(Red(OneError)));
        using var _ = page.Context;

        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForAssertion(() => StringAssert.StartsWith(FixReport(page.Cut), "The fix could not finish"), TimeSpan.FromSeconds(30));

        Assert.AreEqual(
            "The fix could not finish (InvalidOperationException). The errors above are from before the fix; check the project files.",
            FixReport(page.Cut));
        Assert.DoesNotContain("secret", page.Cut.Markup);
        Assert.HasCount(0, page.Cut.FindAll(".fix-offer"), "the old build is stale after an aborted fix");
        Assert.IsTrue(page.Nav.HasStep);
        Assert.AreEqual(1, fixer.Calls);
    }

    [TestMethod]
    public async Task A_second_click_while_the_fix_runs_does_not_start_a_second_fixer()
    {
        using var gate = new ManualResetEventSlim();
        var fixer = new FakeFixer { Block = () => gate.Wait(TimeSpan.FromSeconds(30)) };
        var page = Written(fixer, new FakeBuild(Red(OneError), Green));
        using var _ = page.Context;
        var handler = typeof(GenerateStage).GetMethod("FixErrors", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var first = page.Cut.InvokeAsync(() => (Task)handler.Invoke(page.Cut.Instance, null)!);
        page.Cut.WaitForState(() => fixer.Calls == 1, TimeSpan.FromSeconds(10));
        await page.Cut.InvokeAsync(() => (Task)handler.Invoke(page.Cut.Instance, null)!);

        Assert.AreEqual(1, fixer.Calls);
        Assert.HasCount(0, page.Cut.FindAll(".fix-offer"), "no buttons while it runs");
        Assert.IsFalse(page.Nav.HasStep, "the box offers no way on while it runs");

        gate.Set();
        await first;
        Assert.AreEqual(1, fixer.Calls);
    }

    [TestMethod]
    public async Task Progress_is_shown_while_the_fixer_runs_and_the_path_is_cleaned()
    {
        using var gate = new ManualResetEventSlim();
        var fixer = new FakeFixer { Block = () => gate.Wait(TimeSpan.FromSeconds(30)) };
        var page = Written(fixer, new FakeBuild(Red(OneError), Green));
        using var _ = page.Context;

        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForAssertion(
            () => Assert.AreEqual("Fixed CS1002 (rule) in Models\\Customer.cs", FixReport(page.Cut)),
            TimeSpan.FromSeconds(10));

        gate.Set();
        page.Cut.WaitForAssertion(() => StringAssert.StartsWith(FixReport(page.Cut), "The build now succeeds"), TimeSpan.FromSeconds(30));
        await Task.CompletedTask;
    }

    [TestMethod]
    public void Report_text_is_shown_as_text_not_markup()
    {
        var fixer = new FakeFixer { EditedBy = "<b>bold</b> & <img src=x>" };
        var page = Written(fixer, new FakeBuild(Red(OneError), Green));
        using var _ = page.Context;

        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForAssertion(() => StringAssert.StartsWith(FixReport(page.Cut), "The build now succeeds"), TimeSpan.FromSeconds(30));

        Assert.HasCount(0, page.Cut.FindAll("pre.fix-report b"));
        Assert.HasCount(0, page.Cut.FindAll("pre.fix-report img"));
        StringAssert.Contains(FixReport(page.Cut), "<b>bold</b> & <img src=x>");
    }

    [TestMethod]
    public async Task Leaving_the_page_mid_fix_still_records_the_result_and_throws_nothing()
    {
        using var gate = new ManualResetEventSlim();
        var fixer = new FakeFixer { Block = () => gate.Wait(TimeSpan.FromSeconds(30)) };
        var page = Written(fixer, new FakeBuild(Red(OneError), Green));
        var unobserved = new List<Exception>();
        EventHandler<UnobservedTaskExceptionEventArgs> watch = (_, e) => unobserved.Add(e.Exception);
        TaskScheduler.UnobservedTaskException += watch;

        try
        {
            page.Cut.Find("button.fix-yes").Click();
            page.Cut.WaitForState(() => fixer.Calls == 1, TimeSpan.FromSeconds(10));

            page.Context.Dispose();
            gate.Set();

            for (var i = 0; i < 100 && !page.Session.Generated!.BuildSucceeded; i++)
                await Task.Delay(100);

            GC.Collect();
            GC.WaitForPendingFinalizers();

            Assert.IsTrue(page.Session.Generated!.BuildSucceeded, "the fix changed files; the session must know");
            Assert.IsEmpty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= watch;
        }
    }

    [TestMethod]
    public void A_reloaded_page_with_a_red_build_shows_no_offer_and_says_why()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IBuildFixer>(new FakeFixer());

        var session = NewSession();
        var scaffold = GenerateStep.Scaffold(session, GenerateStep.NameFor(session), session.Target!, PersistenceKind.Json);
        session.SetGenerated(new GeneratedApp(scaffold, "log", BuildSucceeded: false));

        var cut = context.Render<GenerateStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation()));

        Assert.HasCount(0, cut.FindAll(".fix-offer"));
        Assert.IsTrue(ReportLines(cut).Contains(
            "It did not compile — the log is above the last time it ran. "
            + "A fix is offered only straight after a build, so it is not offered again on a reloaded page."));
    }
}
