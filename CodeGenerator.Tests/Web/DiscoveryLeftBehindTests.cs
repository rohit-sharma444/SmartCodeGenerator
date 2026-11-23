using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DomainDiscovery;
using DomainDiscovery.Model;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// A discovery that ends after the user left Discover neither moves the user nor applies a model
/// for answers that have changed, and a running discovery can be stopped.
/// </summary>
/// <remarks>
/// A local model takes minutes. Before this, a user who went back to Describe meanwhile and typed
/// another description was pulled off that node when discovery ended, and given the classes of
/// the old description.
/// </remarks>
[TestClass]
public class DiscoveryLeftBehindTests
{
    [TestMethod]
    public void A_changed_description_gets_no_model_and_stays_put()
    {
        using var context = new BunitContext();
        using var gate = new ManualResetEventSlim();
        var (rendered, session, discoverer) = Discovering(context, gate);

        session.GoTo(BuildStage.Describe);
        session.SetDescription("a lending library");
        gate.Set();

        // Plain waits: bUnit re-checks a WaitForAssertion only when the panel renders, and these
        // flags change on the discovery thread with no render after them, so under a loaded test
        // run the check could miss them and time out.
        Assert.IsTrue(SpinWait.SpinUntil(() => discoverer.Returned, TimeSpan.FromSeconds(30)), "the discovery never returned");
        Assert.IsTrue(SpinWait.SpinUntil(() => Discovering(session) is null, TimeSpan.FromSeconds(30)), "the discovery was never finished");

        Assert.AreEqual(BuildStage.Describe, session.Current, "the user was pulled off the node");
        Assert.IsNull(session.Model, "the old description's classes were applied");
    }

    [TestMethod]
    public void An_unchanged_description_keeps_the_model_but_does_not_move_the_user()
    {
        using var context = new BunitContext();
        using var gate = new ManualResetEventSlim();
        var (rendered, session, _) = Discovering(context, gate);

        session.GoTo(BuildStage.Describe);
        gate.Set();

        rendered.WaitForAssertion(() => Assert.IsNotNull(session.Model), TimeSpan.FromSeconds(30));
        Assert.AreEqual(BuildStage.Describe, session.Current, "the user was pulled off the node");
    }

    [TestMethod]
    public void Staying_on_Discover_still_moves_on_to_Review()
    {
        using var context = new BunitContext();
        using var gate = new ManualResetEventSlim();
        var (rendered, session, _) = Discovering(context, gate);

        gate.Set();

        rendered.WaitForAssertion(
            () => Assert.AreEqual(BuildStage.Review, session.Current), TimeSpan.FromSeconds(30));
        Assert.IsNotNull(session.Model);
    }

    [TestMethod]
    public void Stopping_ends_the_wait_and_applies_nothing()
    {
        using var context = new BunitContext();
        using var gate = new ManualResetEventSlim();
        var (rendered, session, discoverer) = Discovering(context, gate);

        rendered.Find("button.stop-discovery").Click();

        rendered.WaitForAssertion(
            () => Assert.IsEmpty(rendered.FindAll("button.stop-discovery")), TimeSpan.FromSeconds(30));
        Assert.IsNotEmpty(rendered.FindAll("button.start"), "nothing offers to start again");

        gate.Set();
        Assert.IsTrue(SpinWait.SpinUntil(() => discoverer.Returned, TimeSpan.FromSeconds(30)), "the discovery never returned");

        Assert.IsTrue(discoverer.Cancelled, "the discovery was not told to stop");

        Assert.IsNull(session.Model);
        Assert.AreEqual(BuildStage.Discover, session.Current);
    }

    /// <remarks>
    /// The call blocks for minutes. As a thread-pool work item it waited behind every other busy
    /// worker before it even started, and it then held a worker for the whole wait. The bound is the
    /// assertion: pool thread injection frees a worker after a few seconds, so a wait with no bound
    /// passes on the old code too.
    /// </remarks>
    [TestMethod]
    [DoNotParallelize]
    public void The_discovery_starts_when_every_pool_worker_is_busy()
    {
        using var context = new BunitContext();
        using var gate = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var discoverer = new Discoverer(gate, entered);
        context.Services.AddSingleton<DomainDiscoverer>(discoverer.Discover);
        var session = AtDiscover();

        ThreadPool.GetMinThreads(out var workers, out _);
        for (var queued = 0; queued < workers + 4; queued++)
            ThreadPool.UnsafeQueueUserWorkItem(_ => release.Wait(), null);

        bool started;
        try
        {
            context.Render<DiscoverStage>(p => p
                .Add(c => c.Session, session)
                .Add(c => c.Navigation, new StageNavigation())
                .Add(c => c.OnAdvance, () => { }));

            started = entered.Wait(TimeSpan.FromSeconds(1));
        }
        finally
        {
            release.Set();
            gate.Set();
        }

        Assert.IsTrue(started, "the discovery waited for a free pool worker");
    }

    private static BuildSession AtDiscover()
    {
        var session = new BuildSession("a shop", Path.GetTempPath(), AppCustomization.Default);
        session.SetTarget(new ConsoleTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();

        Assert.AreEqual(BuildStage.Discover, session.Current);

        return session;
    }

    /// <summary>The in-flight discovery the session holds, read the way a joiner would.</summary>
    private static object? Discovering(BuildSession session)
        => typeof(BuildSession)
            .GetField("_discoveryInFlight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(session);

    private sealed class Discoverer(ManualResetEventSlim gate, ManualResetEventSlim entered)
    {
        public volatile bool Returned;
        public volatile bool Cancelled;

        public DiscoveryOutcome Discover(
            string description, string workspaceRoot, Action<DiscoveryProgress>? onProgress,
            CancellationToken cancel)
        {
            entered.Set();
            gate.Wait(TimeSpan.FromSeconds(60));
            Cancelled = cancel.IsCancellationRequested;

            var model = new CandidateDomainModel();
            model.Entities.Add(new CandidateEntity { Name = "Book" });

            Returned = true;

            return new DiscoveryOutcome(model, "test", Succeeded: true, Message: null);
        }
    }

    /// <summary>A Discover panel whose discovery has started and is held until the gate opens.</summary>
    private static (IRenderedComponent<DiscoverStage>, BuildSession, Discoverer) Discovering(
        BunitContext context, ManualResetEventSlim gate)
    {
        using var entered = new ManualResetEventSlim();
        var discoverer = new Discoverer(gate, entered);
        context.Services.AddSingleton<DomainDiscoverer>(discoverer.Discover);

        var session = AtDiscover();

        var rendered = context.Render<DiscoverStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(30)), "the discovery never started");

        return (rendered, session, discoverer);
    }
}
