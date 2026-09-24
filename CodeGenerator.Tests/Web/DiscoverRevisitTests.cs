using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.App.Resume;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DataIngestion;
using DataIngestion.Normalization;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// Coming back to the Discover node after it has done its work.
/// </summary>
/// <remarks>
/// It used to redo the work: the data path re-read the file with every finding unticked and its
/// step forward replaced the reviewed model - or, when the session held that for the discard
/// question, set the new rows anyway and reported success, so "Keep the model" left the old model
/// beside rows that no longer matched it.
/// </remarks>
[TestClass]
public class DiscoverRevisitTests
{
    private string _root = null!;
    private string _csv = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "revisit-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);

        _csv = Path.Combine(_root, "Orders.csv");
        File.WriteAllLines(_csv,
        [
            "Ref,Item1,Item2,Item3",
            "ORD-1,Nut,Bolt,Washer",
            "ORD-2,Nut,,",
            "ORD-3,Bolt,Screw,",
        ]);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public async Task Going_back_to_discover_keeps_the_reviewed_model()
    {
        var session = Reviewed(out var split);
        var model = session.Model;
        var rows = session.ImportedData.For(split).Count;

        session.GoTo(BuildStage.Discover);

        using var context = new BunitContext();
        var (rendered, navigation) = Render(context, session);

        Assert.IsNotNull(rendered.Find(".kept-model"));
        Assert.IsTrue(navigation.CanContinue, "the way on is offered");

        Assert.IsTrue(await navigation.ApplyAsync());

        Assert.AreSame(model, session.Model, "the reviewed model was replaced");
        Assert.IsNull(session.PendingDiscard, "nothing changed, so nothing to ask");
        Assert.AreEqual("Title", session.Model!.Entities[0].Name, "the review edit is gone");
        Assert.HasCount(rows, session.ImportedData.For(split));
        Assert.HasCount(1, session.Normalized);
    }

    [TestMethod]
    public void Reading_the_file_again_starts_with_last_times_findings_ticked()
    {
        var session = Reviewed(out _);
        session.GoTo(BuildStage.Discover);

        using var context = new BunitContext();
        var (rendered, _) = Render(context, session);

        rendered.Find("button.again").Click();

        var boxes = rendered.FindAll(".findings input[type=checkbox]");
        Assert.IsNotEmpty(boxes);
        Assert.IsTrue(boxes.All(b => b.HasAttribute("checked")), "last time's choice was forgotten");
    }

    /// <summary>A second model asks first - and keeping the first keeps its rows too.</summary>
    [TestMethod]
    public async Task Keeping_the_model_keeps_its_rows()
    {
        var session = Reviewed(out var split);
        var rows = session.ImportedData.For(split).Count;
        session.GoTo(BuildStage.Discover);

        using var context = new BunitContext();
        var (rendered, navigation) = Render(context, session);

        rendered.Find("button.again").Click();
        rendered.FindAll(".findings input[type=checkbox]")[0].Change(false);

        Assert.IsFalse(await navigation.ApplyAsync(), "the change must wait for the question");
        Assert.IsNotNull(session.PendingDiscard);

        session.CancelPending();

        Assert.AreEqual("Title", session.Model!.Entities[0].Name);
        Assert.HasCount(rows, session.ImportedData.For(split), "the rows went with the new read");
        Assert.HasCount(1, session.Normalized);
    }

    [TestMethod]
    public async Task Changing_anyway_applies_the_new_model_and_its_rows_together()
    {
        var session = Reviewed(out var split);
        session.GoTo(BuildStage.Discover);

        using var context = new BunitContext();
        var (rendered, navigation) = Render(context, session);

        rendered.Find("button.again").Click();
        rendered.FindAll(".findings input[type=checkbox]")[0].Change(false);
        await navigation.ApplyAsync();

        session.ConfirmPending();

        Assert.IsEmpty(session.ImportedData.For(split), "no split was asked for this time");
        Assert.IsEmpty(session.Normalized);
        Assert.IsFalse(session.Model!.Entities.Any(e => e.Name == split));
    }

    /// <summary>`normalize` on the line ticks every finding, as the manual says. The page used to
    /// open every one unticked.</summary>
    [TestMethod]
    public void Normalize_on_the_line_ticks_every_finding()
    {
        var session = new BuildSession(
            "orders", _root, AppCustomization.Default, new DataSourceSpec(DataSourceKind.Csv, _csv))
        {
            NormalizeAll = true,
        };
        AnswerUpToDiscover(session);

        using var context = new BunitContext();
        var (rendered, _) = Render(context, session);

        var boxes = rendered.FindAll(".findings input[type=checkbox]");
        Assert.IsNotEmpty(boxes);
        Assert.IsTrue(boxes.All(b => b.HasAttribute("checked")));
    }

    [TestMethod]
    public void Without_normalize_nothing_starts_ticked()
    {
        var session = new BuildSession(
            "orders", _root, AppCustomization.Default, new DataSourceSpec(DataSourceKind.Csv, _csv));
        AnswerUpToDiscover(session);

        using var context = new BunitContext();
        var (rendered, _) = Render(context, session);

        Assert.IsFalse(rendered.FindAll(".findings input[type=checkbox]").Any(b => b.HasAttribute("checked")));
    }

    /// <summary>The discovery path's revisit showed a Start button that did nothing, and no way on.</summary>
    [TestMethod]
    public void The_discovery_path_offers_the_way_on_too()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Book" });

        var session = new BuildSession("a library", _root, AppCustomization.Default);
        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(model, "Discovered from Wikipedia");
        session.Advance();
        session.GoTo(BuildStage.Discover);

        using var context = new BunitContext();
        var (rendered, navigation) = Render(context, session);

        StringAssert.Contains(rendered.Find(".kept-model").TextContent, "1 class");
        Assert.AreEqual("Discover again", rendered.Find("button.again").TextContent.Trim());
        Assert.IsTrue(navigation.CanContinue);
    }

    [TestMethod]
    public void The_same_sql_tables_are_not_a_changed_source()
    {
        var session = new BuildSession(
            "trading", _root, AppCustomization.Default,
            new DataSourceSpec(DataSourceKind.Sql, "Server=.;Database=Shop", "Shop", ["Books"]));
        AnswerUpToDiscover(session);
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Book" });
        session.SetModel(model);
        session.Advance();

        session.GoTo(BuildStage.Data);

        Assert.IsTrue(session.SetSource(
            new DataSourceSpec(DataSourceKind.Sql, "Server=.;Database=Shop", "Shop", ["Books"])));
        Assert.IsNull(session.PendingDiscard);
        Assert.AreSame(model, session.Model);
    }

    [TestMethod]
    public void A_resumed_class_keeps_its_role()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Customer", Role = DomainDiscovery.Enrichment.EntityRole.Party
        });

        var session = new BuildSession("a shop", _root, AppCustomization.Default);
        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(model);

        var restored = BuildSnapshot.Of(session).ToModel()!;

        Assert.AreEqual(DomainDiscovery.Enrichment.EntityRole.Party, restored.Entities[0].Role);
    }

    /// <summary>Read, one repeating group ticked, moved on to Review, and a class renamed there.</summary>
    private BuildSession Reviewed(out string split)
    {
        var session = new BuildSession(
            "orders", _root, AppCustomization.Default, new DataSourceSpec(DataSourceKind.Csv, _csv));
        AnswerUpToDiscover(session);

        Assert.IsTrue(DiscoveryStep.TryRead(session.Source!, out var read, out var error), error);
        var chosen = read.Report.Findings
            .Where(f => f.Kind == NormalizationFindingKind.RepeatingGroup).ToList();
        Assert.HasCount(1, chosen, "precondition: one repeating group");
        var splitName = chosen[0].ProposedEntity!;
        split = splitName;

        var (model, imported) = DiscoveryStep.BuildFromData(read, chosen);
        Assert.IsTrue(session.SetModel(model, imported: imported, normalized: chosen));
        session.Advance();

        var order = session.Model!.Entities.First(e => e.Name != splitName);
        session.Model.Entities.Remove(order);
        session.Model.Entities.Insert(0, order);
        order.Name = "Title";
        session.ModelEdited();

        Assert.IsNotEmpty(session.ImportedData.For(splitName), "precondition: the split class has rows");

        return session;
    }

    private static void AnswerUpToDiscover(BuildSession session)
    {
        session.SetTarget(new WpfTarget());
        session.Advance();

        if (!session.HasData)
        {
            session.SetNoData();
        }

        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
    }

    private static (IRenderedComponent<DiscoverStage> Rendered, StageNavigation Navigation) Render(
        BunitContext context, BuildSession session)
    {
        var navigation = new StageNavigation();

        var rendered = context.Render<DiscoverStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, navigation)
            .Add(c => c.OnAdvance, () => { }));

        return (rendered, navigation);
    }
}
