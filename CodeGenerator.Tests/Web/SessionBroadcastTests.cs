using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// What the session tells its watchers.
/// </summary>
/// <remarks>
/// One workflow can have more than one page looking at it — the session is a singleton precisely
/// so a second tab attaches rather than starting a rival run. A circuit only repaints when it is
/// told to, so anything that moves the workflow has to say so, or the tab nobody touched goes on
/// drawing a node the workflow has left, with a live Continue button on it.
/// </remarks>
[TestClass]
public class SessionBroadcastTests
{
    [TestMethod]
    public void Answering_a_node_says_so()
    {
        var session = New();
        var heard = Count(session);

        session.SetTarget(new ConsoleTarget());

        Assert.AreEqual(1, heard());
    }

    [TestMethod]
    public void Advancing_says_so()
    {
        var session = New();
        var heard = Count(session);

        session.Advance();

        Assert.AreEqual(1, heard());
    }

    [TestMethod]
    public void Revisiting_says_so()
    {
        var session = New();
        session.Advance();
        var heard = Count(session);

        session.GoTo(BuildStage.Describe);

        Assert.AreEqual(1, heard());
    }

    [TestMethod]
    public void Failing_a_node_says_so()
    {
        var session = New();
        var heard = Count(session);

        session.Fail("the file would not read");

        Assert.AreEqual(1, heard());
    }

    /// <summary>The question is the state that most needs to reach the other tab: without it,
    /// that tab shows an answer the session is not holding.</summary>
    [TestMethod]
    public void Holding_a_change_for_confirmation_says_so()
    {
        var session = Discovered();
        var heard = Count(session);

        session.SetDescription("a hotel");

        Assert.AreEqual(1, heard());

        session.CancelPending();

        Assert.AreEqual(2, heard());
    }

    [TestMethod]
    public void Ending_the_workflow_says_so()
    {
        var session = New();
        var heard = Count(session);

        session.Cancel();

        Assert.AreEqual(1, heard());
    }

    /// <summary>A page that has gone away must stop being called, or a circuit that closed while
    /// another drives the workflow takes the whole host down with it.</summary>
    [TestMethod]
    public void A_watcher_that_unsubscribes_hears_nothing_more()
    {
        var session = New();
        var heard = 0;

        void Watcher() => heard++;

        session.Changed += Watcher;
        session.Advance();
        session.Changed -= Watcher;
        session.GoTo(BuildStage.Describe);

        Assert.AreEqual(1, heard);
    }

    private static Func<int> Count(BuildSession session)
    {
        var heard = 0;

        session.Changed += () => heard++;

        return () => heard;
    }

    private static BuildSession New() => new("a shop", @"C:\ws", AppCustomization.Default);

    private static BuildSession Discovered()
    {
        var session = New();

        session.SetTarget(new ConsoleTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(new DomainDiscovery.Model.CandidateDomainModel());

        return session;
    }
}
