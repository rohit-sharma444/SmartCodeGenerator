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
        public volatile bool Returned;

        public FixOutcome Fix(string p, ProcessResult failed, Func<ProcessResult> rebuild, Action<string> progress)
        {
            try
            {
                return FixCore(p, failed, rebuild, progress);
            }
            finally
            {
                Returned = true;
            }
        }

        private FixOutcome FixCore(string p, ProcessResult failed, Func<ProcessResult> rebuild, Action<string> progress)
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
    private Page Written(FakeFixer? fixer, FakeBuild build, BuildSession? given = null, bool addNew = false,
        Func<IServiceProvider, BuildRunner>? runnerFactory = null)
    {
        var context = new BunitContext();

        // Transient, so a factory is asked again on every use (a singleton would be cached).
        if (runnerFactory is not null)
            context.Services.AddTransient(runnerFactory);
        else
            context.Services.AddSingleton<BuildRunner>(build.Run);

        if (fixer is not null)
            context.Services.AddSingleton<IBuildFixer>(fixer);

        var session = given ?? NewSession();
        var nav = new StageNavigation();
        var cut = context.Render<GenerateStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, nav)
            .Add(c => c.OnAdvance, () => { }));

        cut.Find("button.write").Click();

        // The collision is decided before the first await, so the question is already on screen.
        if (addNew)
            cut.Find("button.overwrite-add").Click();
        else if (cut.FindAll("button.overwrite-yes").Count > 0)
            cut.Find("button.overwrite-yes").Click();

        // A real scaffold on a loaded machine: a long cap, never a tight one.
        cut.WaitForAssertion(
            () => Assert.IsNotNull(session.Generated), TimeSpan.FromMinutes(5));

        return new Page(cut, session, nav, context);
    }

    private BuildSession NewSession(AppCustomization? customization = null)
    {
        customization ??= AppCustomization.Default;
        var session = new BuildSession("a shop", _root, AppCustomization.Default);
        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(customization);
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
        Assert.AreEqual("build line 2", page.Session.Generated!.BuildLog, "the run stage parses ONLY the rebuild");
        Assert.AreEqual("build line 2", page.Cut.Find("pre.build-log").TextContent, "only the green rebuild is shown");
    }

    private const string StubWarning = "declares ValidateCustom in the shape emitted before";

    /// <summary>A session whose project already holds an old-form stub, so the write warns about it.</summary>
    private BuildSession SessionWithStaleStub()
    {
        var session = NewSession();
        var folder = GenerateStep.FolderFor(session, GenerateStep.NameFor(session), new WpfTarget());
        var stub = Path.Combine(folder, "Validation", "CustomerValidator.Custom.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(stub)!);
        File.WriteAllText(
            stub,
            "using System.Collections.Generic;\nnamespace X.Validation;\npublic sealed partial class CustomerValidator\n{\n"
            + "    partial void ValidateCustom(Customer item, List<string> errors)\n    {\n    }\n}\n");

        return session;
    }

    [TestMethod]
    public void A_green_fix_clears_the_warning_the_red_log_and_the_problem_step_from_before()
    {
        var page = Written(new FakeFixer(), new FakeBuild(Red(OneError), Green), SessionWithStaleStub());
        using var _ = page.Context;

        // Present BEFORE the fix, so the absences below are not vacuous.
        Assert.IsTrue(ReportLines(page.Cut).Any(l => l.Contains(StubWarning)), "the warning is shown first");
        StringAssert.Contains(page.Cut.Markup, "The compiler reported problems");
        StringAssert.Contains(page.Cut.Find("pre.build-log").TextContent, "build line 1");
        Assert.IsTrue(ReportLines(page.Cut).Any(l => l.Contains("CS1002")));

        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForAssertion(() => StringAssert.StartsWith(FixReport(page.Cut), "The build now succeeds"), TimeSpan.FromSeconds(30));

        Assert.IsFalse(ReportLines(page.Cut).Any(l => l.Contains(StubWarning)), "the warning is about a file that is now fixed");
        Assert.DoesNotContain("The compiler reported problems", page.Cut.Markup);
        Assert.DoesNotContain("build line 1", page.Cut.Markup);
        Assert.DoesNotContain("--- fixing the build errors ---", page.Cut.Markup);
        StringAssert.Contains(page.Cut.Markup, "Compiled after the fix");
        Assert.IsTrue(ReportLines(page.Cut).Any(l => l.StartsWith("Build succeeded")));
        Assert.IsTrue(ReportLines(page.Cut).Any(l => l.StartsWith("Generated project at")), "what is still true stays");
    }

    [TestMethod]
    public void A_still_red_fix_keeps_the_warning_and_the_log_from_before()
    {
        var page = Written(new FakeFixer(), new FakeBuild(Red(OneError), Red(TwoErrors)), SessionWithStaleStub());
        using var _ = page.Context;

        Assert.IsTrue(ReportLines(page.Cut).Any(l => l.Contains(StubWarning)), "the warning is shown first");

        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForAssertion(() => StringAssert.StartsWith(FixReport(page.Cut), "Partly fixed"), TimeSpan.FromSeconds(30));

        Assert.IsTrue(ReportLines(page.Cut).Any(l => l.Contains(StubWarning)));
        StringAssert.Contains(page.Cut.Markup, "The compiler reported problems");
        StringAssert.Contains(page.Cut.Find("pre.build-log").TextContent, "build line 1");
        StringAssert.Contains(page.Cut.Find("pre.build-log").TextContent, "build line 2");
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
    public void A_fixer_that_throws_gives_one_plain_line_and_still_offers_the_fix_again()
    {
        var fixer = new FakeFixer { Throw = new InvalidOperationException("secret C:\\x\\y.cs") };
        var page = Written(fixer, new FakeBuild(Red(OneError), Green));
        using var _ = page.Context;
        var before = page.Session.Generated;

        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForAssertion(() => StringAssert.StartsWith(FixReport(page.Cut), "The fix could not finish"), TimeSpan.FromSeconds(30));

        Assert.AreEqual(
            "The fix could not finish (InvalidOperationException). The errors above are from before the fix; check the project files.",
            FixReport(page.Cut));
        Assert.DoesNotContain("secret", page.Cut.Markup);
        Assert.AreEqual("The build failed with 1 error. Fix them?", page.Cut.Find(".fix-question").TextContent.Trim());
        Assert.AreSame(before, page.Session.Generated, "the old failed build is still the session's");
        Assert.AreEqual(1, fixer.Calls);

        fixer.Throw = null;
        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForAssertion(() => Assert.IsTrue(page.Session.Generated!.BuildSucceeded), TimeSpan.FromSeconds(30));
        Assert.AreEqual(2, fixer.Calls);
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
            "It did not compile. "
            + "A fix is offered only straight after a build, so it is not offered again on a reloaded page."));
    }

    [TestMethod]
    public void A_change_during_the_fix_keeps_the_stale_app_from_coming_back()
    {
        using var gate = new ManualResetEventSlim();
        var fixer = new FakeFixer { Block = () => gate.Wait(TimeSpan.FromSeconds(30)) };
        var page = Written(fixer, new FakeBuild(Red(OneError), Green));
        using var _ = page.Context;

        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForState(() => fixer.Calls == 1, TimeSpan.FromSeconds(10));

        page.Session.ModelEdited();
        var stage = page.Session.StateOf(BuildStage.Generate);
        Assert.IsNull(page.Session.Generated);

        gate.Set();
        page.Cut.WaitForAssertion(
            () => Assert.AreEqual(
                "The app was changed while the fix ran, so its result was not kept.", FixReport(page.Cut)),
            TimeSpan.FromSeconds(30));

        Assert.IsNull(page.Session.Generated, "the stale app must not be written back");
        Assert.AreEqual(stage, page.Session.StateOf(BuildStage.Generate));
        Assert.HasCount(0, page.Cut.FindAll(".fix-offer"));
    }

    [TestMethod]
    public async Task A_change_then_a_closed_page_still_writes_nothing()
    {
        using var gate = new ManualResetEventSlim();
        var fixer = new FakeFixer { Block = () => gate.Wait(TimeSpan.FromSeconds(30)) };
        var page = Written(fixer, new FakeBuild(Red(OneError), Green));
        var handler = typeof(GenerateStage).GetMethod("FixErrors", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // The stage's own work, as a task: awaiting it is the completion signal, no waiting for silence.
        var work = page.Cut.InvokeAsync(() => (Task)handler.Invoke(page.Cut.Instance, null)!);
        page.Cut.WaitForState(() => fixer.Calls == 1, TimeSpan.FromSeconds(10));

        page.Session.ModelEdited();
        page.Context.Dispose();
        gate.Set();

        await work.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.IsTrue(fixer.Returned);
        Assert.IsNull(page.Session.Generated, "the stale app came back");
    }

    [TestMethod]
    public async Task A_throw_before_the_fixer_starts_still_releases_the_lock_and_the_flag()
    {
        var build = new FakeBuild(Red(OneError), Green);
        var armed = false;
        var page = Written(
            new FakeFixer(), build,
            runnerFactory: _ => armed ? throw new InvalidOperationException("no runner") : build.Run);
        using var _ = page.Context;
        var project = page.Session.Generated!.Scaffold.ProjectPath;
        var handler = typeof(GenerateStage).GetMethod("FixErrors", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var flag = typeof(GenerateStage).GetField("_fixing", BindingFlags.NonPublic | BindingFlags.Instance)!;

        armed = true;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => page.Cut.InvokeAsync(() => (Task)handler.Invoke(page.Cut.Instance, null)!));

        Assert.IsFalse((bool)flag.GetValue(page.Cut.Instance)!, "the page must not stay busy");
        Assert.IsTrue(FixLocks.TryEnter(project), "the project lock must be free");
        FixLocks.Leave(project);
    }

    [TestMethod]
    public void After_a_throw_the_offer_says_the_errors_may_be_old_and_the_next_Yes_reads_them_again()
    {
        var fixer = new FakeFixer { Throw = new InvalidOperationException("boom") };
        var build = new FakeBuild(Red(OneError), Red(TwoErrors), Green);
        var page = Written(fixer, build);
        using var _ = page.Context;

        Assert.HasCount(0, page.Cut.FindAll(".fix-stale"), "no warning before any throw");

        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForAssertion(() => StringAssert.StartsWith(FixReport(page.Cut), "The fix could not finish"), TimeSpan.FromSeconds(30));

        Assert.AreEqual(1, build.Calls, "a throw runs no build");
        Assert.AreEqual(
            "The errors listed may be out of date, because files may have changed.",
            page.Cut.Find(".fix-stale").TextContent.Trim());

        fixer.Throw = null;
        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForAssertion(() => Assert.IsTrue(page.Session.Generated!.BuildSucceeded), TimeSpan.FromSeconds(30));

        Assert.AreEqual(2, fixer.Calls);
        Assert.AreEqual(3, build.Calls, "first build, one fresh read of the errors, one rebuild");
        Assert.AreEqual(TwoErrors, fixer.Failed!.StandardOutput, "the fixer got the fresh errors");
        Assert.HasCount(0, page.Cut.FindAll(".fix-stale"));
    }

    [TestMethod]
    public void A_timed_out_rebuild_keeps_the_build_log_of_the_build_the_session_holds()
    {
        var timedOut = new ProcessResult(1, OneError, string.Empty, true);
        var page = Written(new FakeFixer(), new FakeBuild(Red(OneError), timedOut));
        using var _ = page.Context;
        var before = page.Session.Generated!.BuildLog;

        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForAssertion(
            () => Assert.IsTrue(ReportLines(page.Cut).Any(l => l.StartsWith("Build timed out"))),
            TimeSpan.FromSeconds(30));

        Assert.AreEqual(before, page.Session.Generated!.BuildLog, "not the partial log of the timed-out rebuild");
        Assert.AreEqual("build line 1", before);
    }

    [TestMethod]
    public void Two_pages_fixing_one_project_run_one_fixer()
    {
        using var gate = new ManualResetEventSlim();
        var first = new FakeFixer { Block = () => gate.Wait(TimeSpan.FromSeconds(30)) };
        var second = new FakeFixer();
        var a = Written(first, new FakeBuild(Red(OneError), Green));
        using var contextA = a.Context;
        var b = Written(second, new FakeBuild(Red(OneError), Green));
        using var contextB = b.Context;

        Assert.AreEqual(a.Session.Generated!.Scaffold.ProjectPath, b.Session.Generated!.Scaffold.ProjectPath);

        a.Cut.Find("button.fix-yes").Click();
        a.Cut.WaitForState(() => first.Calls == 1, TimeSpan.FromSeconds(10));

        b.Cut.Find("button.fix-yes").Click();

        Assert.AreEqual("A fix is already running for this project.", FixReport(b.Cut));
        Assert.AreEqual(0, second.Calls);
        Assert.HasCount(1, b.Cut.FindAll(".fix-offer"), "its own offer stays");

        gate.Set();
        a.Cut.WaitForAssertion(() => StringAssert.StartsWith(FixReport(a.Cut), "The build now succeeds"), TimeSpan.FromSeconds(30));

        b.Cut.Find("button.fix-yes").Click();
        b.Cut.WaitForAssertion(() => StringAssert.StartsWith(FixReport(b.Cut), "The build now succeeds"), TimeSpan.FromSeconds(30));
        Assert.AreEqual(1, second.Calls, "the lock was released");
    }

    [TestMethod]
    public void A_rebuild_that_times_out_after_the_fix_is_reported_honestly()
    {
        var fixer = new FakeFixer();
        var timedOut = new ProcessResult(1, OneError, string.Empty, true);
        var page = Written(fixer, new FakeBuild(Red(OneError), timedOut));
        using var _ = page.Context;

        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForAssertion(() => Assert.IsTrue(ReportLines(page.Cut).Any(l => l.StartsWith("Build timed out"))), TimeSpan.FromSeconds(30));

        Assert.IsFalse(page.Session.Generated!.BuildSucceeded);
        Assert.IsFalse(GenerateStep.CanRun(page.Session.Generated, page.Session.Target!));
        Assert.IsFalse(FixReport(page.Cut).StartsWith("The build now succeeds", StringComparison.Ordinal));
        Assert.IsFalse(ReportLines(page.Cut).Any(l => l.StartsWith("Build succeeded")));
        Assert.HasCount(0, page.Cut.FindAll(".fix-offer"), "a timeout is not an error list to fix");
    }

    [TestMethod]
    public void The_nologin_variant_offers_and_runs_the_fix_the_same_way()
    {
        var fixer = new FakeFixer();
        var session = NewSession(AppCustomization.Default with { Login = false });
        var page = Written(fixer, new FakeBuild(Red(OneError), Green), session);
        using var _ = page.Context;

        Assert.AreEqual("The build failed with 1 error. Fix them?", page.Cut.Find(".fix-question").TextContent.Trim());

        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForAssertion(() => Assert.IsTrue(page.Session.Generated!.BuildSucceeded), TimeSpan.FromSeconds(30));
        Assert.AreEqual(1, fixer.Calls);
    }

    [TestMethod]
    public void The_add_only_flow_offers_and_runs_the_fix_the_same_way()
    {
        var session = NewSession();
        var first = GenerateStep.Scaffold(session, GenerateStep.NameFor(session), session.Target!, PersistenceKind.Json);
        Assert.IsTrue(first.Success, first.Message);

        var supplier = new CandidateEntity { Name = "Supplier", Provenance = Provenance.Wiki };
        supplier.Fields.Add(new CandidateField { Name = "Title", Type = "string", Provenance = Provenance.Wiki });
        session.Model!.Entities.Add(supplier);

        var fixer = new FakeFixer();
        var page = Written(fixer, new FakeBuild(Red(OneError), Green), session, addNew: true);
        using var _ = page.Context;

        Assert.AreEqual("The build failed with 1 error. Fix them?", page.Cut.Find(".fix-question").TextContent.Trim());

        page.Cut.Find("button.fix-yes").Click();
        page.Cut.WaitForAssertion(() => Assert.IsTrue(page.Session.Generated!.BuildSucceeded), TimeSpan.FromSeconds(30));
        Assert.AreEqual(first.ProjectPath, fixer.Path);
    }
}
