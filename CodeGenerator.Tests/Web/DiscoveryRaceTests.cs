using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.Web.Workflow;
using DomainDiscovery;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The web-zero-classes defect: a fresh top-level navigation to <c>/build</c> landing while a
/// discovery call is already in flight used to start a second, independent
/// <c>DiscoveryStep.Discover</c> call, whose <c>SetModel</c>/<c>Advance</c> pair was never
/// protected by the discard-confirmation every other model-discarding answer gets — so it could
/// silently overwrite a good model with a stale or empty one and advance <c>Current</c> twice for
/// a node it never displayed.
/// </summary>
/// <remarks>
/// Driven the way the investigation's replay harness was: directly against the real
/// <see cref="BuildSession"/>/<see cref="StageGraph"/>, replaying exactly the sequence
/// <c>DiscoverStage.Start()</c> performs (now corrected) — no mocking, no rendered component.
/// <see cref="BuildSession.JoinDiscovery"/>/<see cref="BuildSession.DiscoveryFinished"/> are the
/// re-entrancy guard; <see cref="StageGraph.Invalidates"/> listing <see cref="BuildStage.Discover"/>
/// against itself, plus checking <see cref="BuildSession.SetModel"/>'s return, is the backstop.
/// </remarks>
[TestClass]
public class DiscoveryRaceTests
{
    /// <summary>A session answered up to, but not including, discovery — <c>Current ==
    /// Discover</c>, <c>Model == null</c>. Exactly the state a fresh <c>DiscoverStage.Start()</c>
    /// (first entry or a racing second one) finds itself in.</summary>
    private static BuildSession ParkedOnDiscover()
    {
        var session = new BuildSession("a shop", @"C:\ws", AppCustomization.Default);
        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();

        return session;
    }

    private static DiscoveryOutcome Outcome(int entityCount, string label)
    {
        var model = new CandidateDomainModel();

        for (var i = 0; i < entityCount; i++)
            model.Entities.Add(new CandidateEntity { Name = $"Entity{i}" });

        return new DiscoveryOutcome(model, label, Succeeded: true, Message: null);
    }

    // --- JoinDiscovery: the re-entrancy guard itself -----------------------------------------

    [TestMethod]
    public void A_second_join_shares_the_first_call_instead_of_starting_a_new_one()
    {
        var session = ParkedOnDiscover();
        var realCalls = 0;

        var (task1, owner1) = session.JoinDiscovery(() =>
        {
            realCalls++;
            return Task.FromResult(Outcome(12, "first"));
        });

        // A reload or a second tab landing while the first call is still running.
        var (task2, owner2) = session.JoinDiscovery(() =>
        {
            realCalls++;
            return Task.FromResult(Outcome(0, "second"));
        });

        Assert.IsTrue(owner1);
        Assert.IsFalse(owner2);
        Assert.AreSame(task1, task2);
        Assert.AreEqual(1, realCalls);
    }

    [TestMethod]
    public void Finishing_discovery_lets_the_next_join_start_a_genuinely_fresh_call()
    {
        var session = ParkedOnDiscover();

        var (task1, owner1) = session.JoinDiscovery(() => Task.FromResult(Outcome(12, "first")));
        Assert.IsTrue(owner1);

        session.DiscoveryFinished();

        var realCalls = 0;

        var (task2, owner2) = session.JoinDiscovery(() =>
        {
            realCalls++;
            return Task.FromResult(Outcome(3, "second"));
        });

        Assert.IsTrue(owner2);
        Assert.AreEqual(1, realCalls);
        Assert.AreNotSame(task1, task2);
    }

    // --- The full corrected sequence, replayed exactly as DiscoverStage.Start() performs it ---

    /// <summary>
    /// Two overlapping discoveries on one session leave exactly one model, with its entities
    /// intact — not the empty one the investigation reproduced. Both calls join the SAME task
    /// (fix 1), and only the owner acts on it (fix 1 + fix 2's backstop).
    /// </summary>
    [TestMethod]
    public async Task Two_overlapping_discoveries_leave_exactly_one_model_with_entities_intact()
    {
        var session = ParkedOnDiscover();
        var tcs = new TaskCompletionSource<DiscoveryOutcome>();

        // Entry 1: the original call.
        var (task1, owner1) = session.JoinDiscovery(() => tcs.Task);

        // Entry 2: a reload/second tab landing before entry 1's call has returned.
        var (task2, owner2) = session.JoinDiscovery(() => tcs.Task);

        Assert.IsTrue(owner1);
        Assert.IsFalse(owner2);

        // The real Ollama call finally completes, with the healthy result — 12 classes, matching
        // the investigation's console run.
        tcs.SetResult(Outcome(12, "qwen2.5:3b (Ollama)"));

        var outcome1 = await task1;
        var outcome2 = await task2;

        // Mirrors DiscoverStage.Start(): only the owner applies the outcome, and only advances
        // when SetModel says it actually applied.
        if (owner1 && session.SetModel(outcome1.Model, outcome1.Describe()))
            session.Advance();

        if (owner2 && session.SetModel(outcome2.Model, outcome2.Describe()))
            session.Advance();

        Assert.IsNotNull(session.Model);
        Assert.HasCount(12, session.Model.Entities);
        Assert.AreEqual(BuildStage.Review, session.Current);
    }

    /// <summary>
    /// The exact "interleaving 2" from the investigation, replayed with the fix in place: two
    /// discovery outcomes both completing (and both calling <c>SetModel</c>) before either call
    /// gets to <c>Advance</c>. Pre-fix this reached <c>Current == Review</c>,
    /// <c>Model.Entities.Count == 0</c> — a fully-rendered Review page with nothing on it. This
    /// test does not rely on <c>JoinDiscovery</c> at all, so it also proves fix 2
    /// (<see cref="StageGraph.Invalidates"/> self-inclusion) holds up on its own.
    /// </summary>
    [TestMethod]
    public void A_second_SetModel_before_either_call_advances_is_held_not_silently_applied()
    {
        var session = ParkedOnDiscover();

        var appliedA = session.SetModel(Outcome(12, "A").Model, "A");
        var appliedB = session.SetModel(Outcome(0, "B — stale").Model, "B — stale");

        Assert.IsTrue(appliedA);
        Assert.IsFalse(appliedB);
        Assert.IsNotNull(session.PendingDiscard);

        // A SetModel that does not apply must not advance the session — the acceptance criterion
        // this test exists for. Before the fix, B's SetModel silently applied too, and this loop
        // pushed Current from Review to Generate: a double advance past a node the page never
        // showed.
        if (appliedA)
            session.Advance();

        if (appliedB)
            session.Advance();

        Assert.AreEqual(BuildStage.Review, session.Current);
        Assert.IsNotNull(session.Model);
        Assert.HasCount(12, session.Model.Entities);
    }
}
