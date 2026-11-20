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

        rendered.WaitForAssertion(() => Assert.IsTrue(discoverer.Returned), TimeSpan.FromSeconds(10));
        rendered.WaitForAssertion(() => Assert.IsNull(Discovering(session)), TimeSpan.FromSeconds(10));

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

        rendered.WaitForAssertion(() => Assert.IsNotNull(session.Model), TimeSpan.FromSeconds(10));
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
            () => Assert.AreEqual(BuildStage.Review, session.Current), TimeSpan.FromSeconds(10));
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
            () => Assert.IsEmpty(rendered.FindAll("button.stop-discovery")), TimeSpan.FromSeconds(10));
        Assert.IsNotEmpty(rendered.FindAll("button.start"), "nothing offers to start again");

        gate.Set();
        rendered.WaitForAssertion(() => Assert.IsTrue(discoverer.Returned), TimeSpan.FromSeconds(10));

        Assert.IsTrue(discoverer.Cancelled, "the discovery was not told to stop");

        Assert.IsNull(session.Model);
        Assert.AreEqual(BuildStage.Discover, session.Current);
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
            gate.Wait(TimeSpan.FromSeconds(30));
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

        var session = new BuildSession("a shop", Path.GetTempPath(), AppCustomization.Default);
        session.SetTarget(new ConsoleTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();

        Assert.AreEqual(BuildStage.Discover, session.Current);

        var rendered = context.Render<DiscoverStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)), "the discovery never started");

        return (rendered, session, discoverer);
    }
}
