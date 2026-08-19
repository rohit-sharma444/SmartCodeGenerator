using AppScaffolding;
using AppScaffolding.Layouts;
using AppScaffolding.Targets;
using CodeGenerator.Web.Workflow;
using DataIngestion;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// What each box says it holds. The diagram is the summary of every decision as well as the
/// navigation, so these lines are the ones a user reads instead of opening a panel to remember.
/// </summary>
[TestClass]
public class StageSummaryTests
{
    [TestMethod]
    public void An_unanswered_node_says_nothing()
    {
        var session = New();

        Assert.IsNull(StageSummary.For(session, BuildStage.Describe));
        Assert.IsNull(StageSummary.For(session, BuildStage.AppType));
        Assert.IsNull(StageSummary.For(session, BuildStage.Data));
        Assert.IsNull(StageSummary.For(session, BuildStage.Discover));
        Assert.IsNull(StageSummary.For(session, BuildStage.Generate));
    }

    [TestMethod]
    public void The_description_is_the_describe_answer()
        => Assert.AreEqual(
            "a library that lends books",
            StageSummary.For(New("a library that lends books"), BuildStage.Describe));

    // A description can be a paragraph and a box is a box.
    [TestMethod]
    public void A_long_description_is_trimmed_with_an_ellipsis()
    {
        var summary = StageSummary.For(
            New("a library that lends books to members and tracks their fines"),
            BuildStage.Describe);

        Assert.IsNotNull(summary);
        Assert.IsLessThanOrEqualTo(40, summary!.Length, $"too long for a box: {summary}");
        StringAssert.EndsWith(summary, "…");
    }

    [TestMethod]
    public void The_app_type_is_the_menu_label_the_user_picked()
    {
        var session = New("a shop");
        session.SetTarget(new WebApiTarget());

        Assert.AreEqual(
            CodeGenerator.App.ApplicationTypeMenu.LabelFor(new WebApiTarget()),
            StageSummary.For(session, BuildStage.AppType));
    }

    // A file named on the command line answers this node before it is ever opened, which is the
    // state an inline `from csv <path>` arrives in.
    [TestMethod]
    public void A_data_file_is_named_by_its_file_name_not_its_path()
    {
        var session = new BuildSession(
            "a shop", @"C:\ws", AppCustomization.Default,
            new DataSourceSpec(DataSourceKind.Csv, @"C:\data\deep\orders.csv"));

        Assert.AreEqual("orders.csv", StageSummary.For(session, BuildStage.Data));
    }

    // "No data" is a decision, and the box should say it was made — but only once it has been.
    [TestMethod]
    public void Choosing_no_data_reads_as_an_answer_once_the_node_is_done()
    {
        var session = New("a shop");
        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();

        Assert.IsNull(StageSummary.For(session, BuildStage.Data), "not answered yet");

        session.Advance();

        Assert.AreEqual("discovered from the web", StageSummary.For(session, BuildStage.Data));
    }

    // The customization always has a value, so this is the box most at risk of announcing a
    // decision nobody made — and standing on the node is not the same as having answered it.
    [TestMethod]
    public void The_look_node_says_nothing_until_it_is_answered()
    {
        var session = New("a shop");

        Assert.IsNull(StageSummary.For(session, BuildStage.Look), "not reached");

        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();

        Assert.AreEqual(BuildStage.Look, session.Current);
        Assert.IsNull(StageSummary.For(session, BuildStage.Look), "open, but not answered");

        session.Advance();

        Assert.AreEqual("defaults", StageSummary.For(session, BuildStage.Look));
    }

    // Named settings, not a count: "3 changes" tells the user nothing they wanted to know.
    [TestMethod]
    public void Customization_names_what_was_changed()
    {
        var session = AfterLook(AppCustomization.Default with
        {
            Company = "Acme",
            Theme = AppTheme.Dark,
            Layout = AppLayout.Sidebar,
        });

        var summary = StageSummary.For(session, BuildStage.Look);

        Assert.IsNotNull(summary);
        StringAssert.Contains(summary!, "Acme");
        StringAssert.Contains(summary, "dark");
        StringAssert.Contains(summary, AppLayoutCatalog.KeyFor(AppLayout.Sidebar));
    }

    // A flag or an icon is a change none of the named settings covers, and looking like the
    // defaults would be wrong.
    [TestMethod]
    public void A_change_with_no_name_of_its_own_still_reads_as_customized()
    {
        var session = AfterLook(AppCustomization.Default with { Logging = true });

        Assert.AreEqual("customized", StageSummary.For(session, BuildStage.Look));
    }

    [TestMethod]
    public void Discovery_and_review_report_how_many_classes_there_are()
    {
        var session = New("a shop");
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Order" });
        model.Entities.Add(new CandidateEntity { Name = "Customer" });

        session.SetModel(model);

        Assert.AreEqual("2 classes", StageSummary.For(session, BuildStage.Discover));
        Assert.AreEqual("2 classes", StageSummary.For(session, BuildStage.Review));
    }

    [TestMethod]
    public void One_class_is_not_1_classes()
    {
        var session = New("a shop");
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Order" });

        session.SetModel(model);

        Assert.AreEqual("1 class", StageSummary.For(session, BuildStage.Discover));
    }

    [TestMethod]
    public void The_generated_node_names_the_project_and_the_run_node_says_if_it_can_start()
    {
        var session = New("a shop");
        session.SetGenerated(new GeneratedApp(
            new ScaffoldResult(true, @"C:\ws\Shop.Console", [], string.Empty),
            BuildLog: string.Empty,
            BuildSucceeded: true));

        Assert.AreEqual("Shop.Console", StageSummary.For(session, BuildStage.Generate));
        Assert.AreEqual("ready to start", StageSummary.For(session, BuildStage.Run));
    }

    [TestMethod]
    public void A_red_build_says_so_on_the_run_node()
    {
        var session = New("a shop");
        session.SetGenerated(new GeneratedApp(
            new ScaffoldResult(true, @"C:\ws\Shop.Console", [], string.Empty),
            BuildLog: string.Empty,
            BuildSucceeded: false));

        Assert.AreEqual("did not build", StageSummary.For(session, BuildStage.Run));
    }

    private static BuildSession New(string description = "")
        => new(description, @"C:\ws", AppCustomization.Default);

    /// <summary>A session that has answered the look node with <paramref name="customization"/> and
    /// moved on, which is the only state in which that node reports anything.</summary>
    private static BuildSession AfterLook(AppCustomization customization)
    {
        var session = New("a shop");

        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(customization);
        session.Advance();

        return session;
    }
}
