using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;
using CodeGenerator.App.Resume;
using CodeGenerator.Web.Workflow;
using DataIngestion;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App.Resume;

/// <summary>
/// A build read from a real file, reviewed, abandoned and resumed — through the same store the
/// console uses — must generate what the same build would have generated without the resume.
/// </summary>
/// <remarks>
/// The other resume tests hand-build the model they round-trip, so they only ever check the parts
/// of it their author remembered. This one takes the model the data path actually produces and
/// compares what reaches the generator.
/// </remarks>
[TestClass]
public class ResumeRoundTripTests
{
    private string _root = null!;
    private string _csv = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "resume-rt-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);

        _csv = Path.Combine(_root, "orders.csv");

        var lines = new List<string> { "OrderNumber,Status,StartDate,EndDate,Net,Rate,Total" };

        for (var i = 0; i < 40; i++)
        {
            var status = (i % 3) switch { 0 => "Open", 1 => "Shipped", _ => "Closed" };
            var net = 100 + i;

            lines.Add(string.Join(",",
                $"ORD-{1000 + i}", status,
                $"2026-01-{1 + i % 20:00}", $"2026-02-{1 + i % 20:00}",
                $"{net}.00", "0.20", $"{net * 1.2m:0.00}"));
        }

        File.WriteAllLines(_csv, lines);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public void A_resumed_build_reads_its_rows_again()
    {
        var before = ReviewedSession();

        var after = SaveAndResume(before);

        Assert.IsFalse(before.ImportedData.IsEmpty, "precondition: the file's rows were read");
        var entity = before.Model!.Entities.Single().Name;
        Assert.HasCount(
            before.ImportedData.For(entity).Count, after.ImportedData.For(entity),
            "the snapshot promises the source is re-read on resume");
    }

    [TestMethod]
    public void A_resumed_build_keeps_the_entity_rules_that_were_accepted()
    {
        var before = ReviewedSession();

        var after = SaveAndResume(before);

        var proposed = before.Model!.Entities.Single().Proposals;
        Assert.IsNotEmpty(proposed, "precondition: the data proposed entity-level rules");

        var restored = after.Model!.Entities.Single().Proposals;
        CollectionAssert.AreEqual(
            proposed.Select(p => $"{p.Accepted}:{p.Rule.Describe()}").ToArray(),
            restored.Select(p => $"{p.Accepted}:{p.Rule.Describe()}").ToArray());
    }

    [TestMethod]
    public void A_resumed_build_keeps_the_status_order_and_final_values()
    {
        var before = ReviewedSession();

        var after = SaveAndResume(before);

        var status = StatusProposal(after.Model!);
        CollectionAssert.AreEqual(new[] { "Open", "Shipped", "Closed" }, status.Order!.ToArray());
        CollectionAssert.AreEquivalent(new[] { "Closed" }, status.FinalValues.ToArray());
    }

    [TestMethod]
    public void A_resumed_build_generates_the_same_rules()
    {
        var before = ReviewedSession();

        var after = SaveAndResume(before);

        CollectionAssert.AreEqual(Rules(before.Model!), Rules(after.Model!));
    }

    /// <summary>
    /// Stepping forward is progress too. Only answers and edits used to save, so someone who
    /// continued past Review to Output without changing anything and closed the tab came back on
    /// Discover.
    /// </summary>
    [TestMethod]
    public void A_build_left_on_a_later_stage_resumes_on_it()
    {
        var session = ReviewedSession();
        session.Autosave = s => BuildSnapshotStore.Save(_root, BuildSnapshot.Of(s));

        while (session.Current != BuildStage.Output)
            session.Advance();

        var resumed = ResumedSession.From(BuildSnapshotStore.Load(_root)!, _root, out _);

        Assert.AreEqual(BuildStage.Output, resumed.Current);
    }

    /// <summary>
    /// A class split out by a ticked normalization finding has no rows of its own in the raw file,
    /// so reading the file again is not enough: the same finding has to be applied again.
    /// </summary>
    [TestMethod]
    public void A_resumed_build_normalizes_its_rows_the_same_way()
    {
        var csv = Path.Combine(_root, "Orders.csv");
        File.WriteAllLines(csv,
        [
            "Ref,Item1,Item2,Item3",
            "ORD-1,Nut,Bolt,Washer",
            "ORD-2,Nut,,",
            "ORD-3,Bolt,Screw,",
        ]);

        var before = AnsweredUpToDiscover(csv);

        Assert.IsTrue(DiscoveryStep.TryRead(before.Source!, out var read, out var error), error);

        var chosen = read.Report.Findings
            .Where(f => f.Kind == DataIngestion.Normalization.NormalizationFindingKind.RepeatingGroup)
            .ToList();
        Assert.HasCount(1, chosen, "precondition: the file has one repeating group");

        var (model, imported) = DiscoveryStep.BuildFromData(read, chosen);
        before.SetModel(model);
        before.SetImportedData(imported, chosen);

        var split = chosen[0].ProposedEntity!;
        Assert.IsNotEmpty(before.ImportedData.For(split), "precondition: the split class has rows");

        var after = SaveAndResume(before);

        Assert.HasCount(before.ImportedData.For(split).Count, after.ImportedData.For(split));
    }

    /// <summary>What the Review page leaves behind once someone has ticked everything it offered
    /// and put the status values in order.</summary>
    private BuildSession ReviewedSession()
    {
        var session = AnsweredUpToDiscover(_csv);

        Assert.IsTrue(DiscoveryStep.TryRead(session.Source!, out var read, out var error), error);

        var (model, imported) = DiscoveryStep.BuildFromData(read, []);
        session.SetModel(model);
        session.SetImportedData(imported);
        session.Advance();

        foreach (var entity in model.Entities)
        {
            foreach (var proposal in entity.Fields.SelectMany(f => f.Proposals))
                proposal.Accepted = true;

            foreach (var proposal in entity.Proposals)
                proposal.Accepted = true;
        }

        var status = StatusProposal(model);
        status.Order = ["Open", "Shipped", "Closed"];
        status.FinalValues = new HashSet<string> { "Closed" };

        return session;
    }

    private BuildSession AnsweredUpToDiscover(string csv)
    {
        var session = new BuildSession(
            "orders", _root, AppCustomization.Default, new DataSourceSpec(DataSourceKind.Csv, csv));

        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();

        return session;
    }

    private BuildSession SaveAndResume(BuildSession session)
    {
        BuildSnapshotStore.Save(_root, BuildSnapshot.Of(session));

        var snapshot = BuildSnapshotStore.Load(_root);
        Assert.IsNotNull(snapshot);

        var resumed = ResumedSession.From(snapshot, _root, out var notice);
        Assert.IsNull(notice, notice);

        return resumed;
    }

    private static RuleProposal StatusProposal(CandidateDomainModel model)
        => model.Entities.SelectMany(e => e.Fields).Single(f => f.Name == "Status").Proposals
            .Single(p => p.Kind == RuleProposalKind.Status);

    private static string[] Rules(CandidateDomainModel model)
        => [.. DomainModelMapper.ToClassDefinitions(model).SelectMany(c =>
            c.Properties.SelectMany(p => p.Rules.Select(r =>
                    $"{c.Name}.{p.Name}:{r.Name}:unique={r.IsUnique}:{string.Join('|', r.AllowedValues)}:{r.RegexPattern}"))
                .Concat(c.BusinessRules.Select(r => $"{c.Name} business: {r.Describe()}"))
                .Concat(c.WorkflowRules.Select(r => $"{c.Name} workflow: {r.Describe()}")))];
}
