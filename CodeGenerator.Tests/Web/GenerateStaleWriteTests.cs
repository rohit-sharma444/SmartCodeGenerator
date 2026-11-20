using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerationEngine.Helper;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// A build that ends after the answers changed does not record its app.
/// </summary>
/// <remarks>
/// Found in the browser: Write it with WPF, then change the app type to Console while it builds.
/// The build still recorded the WPF app, so Generate and Run described it under the Console
/// target, and the gone panel's "Continue" landed on the node then on screen. The project is
/// really written, under C:\cg-t\ for the MAX_PATH reason <c>GenerateAddOnlyTests</c> gives.
/// </remarks>
[TestClass]
public class GenerateStaleWriteTests
{
    private static readonly ProcessResult Green = new(0, "Build succeeded.", string.Empty, false);

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

    [TestMethod]
    public void A_new_app_type_while_it_builds_keeps_the_built_app_out()
    {
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        using var context = new BunitContext();
        var (cut, session, navigation) = Writing(context, entered, gate);

        session.GoTo(BuildStage.AppType);
        session.SetTarget(new ConsoleTarget());
        gate.Set();

        cut.WaitForAssertion(() => Assert.HasCount(1, session.Written), TimeSpan.FromSeconds(30));

        Assert.IsNull(session.Generated, "the app built for the old answers was recorded");
        Assert.IsFalse(navigation.HasStep, "the build offered a step for answers it does not match");
        Assert.AreEqual(StageState.Pending, session.StateOf(BuildStage.Generate));
    }

    /// <summary>A panel left mid-build must not offer its step on the node now on screen.</summary>
    [TestMethod]
    public void A_panel_left_while_it_builds_offers_nothing()
    {
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        using var context = new BunitContext();
        var (cut, session, navigation) = Writing(context, entered, gate);

        session.GoTo(BuildStage.Review);

        // As BuildWorkflow does when the diagram moves to another node: the panel goes.
        cut.Instance.Dispose();
        gate.Set();

        cut.WaitForAssertion(() => Assert.HasCount(1, session.Written), TimeSpan.FromSeconds(30));

        Assert.IsNull(session.Generated, "a panel that is gone recorded its app");
        Assert.IsFalse(navigation.HasStep, "a panel that is gone offered its step");
    }

    /// <summary>The same answers all through: the app is recorded, as always.</summary>
    [TestMethod]
    public void An_unchanged_session_still_records_the_app()
    {
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        using var context = new BunitContext();
        var (cut, session, navigation) = Writing(context, entered, gate);

        gate.Set();

        cut.WaitForAssertion(() => Assert.IsNotNull(session.Generated), TimeSpan.FromSeconds(30));
        Assert.IsTrue(navigation.HasStep);
    }

    /// <summary>A page writing the project, held inside its build until the gate opens.</summary>
    private (IRenderedComponent<GenerateStage>, BuildSession, StageNavigation) Writing(
        BunitContext context, ManualResetEventSlim entered, ManualResetEventSlim gate)
    {
        context.Services.AddSingleton<BuildRunner>((path, onLine) =>
        {
            entered.Set();
            gate.Wait(TimeSpan.FromSeconds(60));
            return new BuildAttempt(Green, TimeSpan.FromSeconds(2));
        });

        var session = NewSession();
        var navigation = new StageNavigation();
        var cut = context.Render<GenerateStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, navigation)
            .Add(c => c.OnAdvance, () => { }));

        cut.Find("button.write").Click();

        if (cut.FindAll("button.overwrite-yes").Count > 0)
            cut.Find("button.overwrite-yes").Click();

        Assert.IsTrue(entered.Wait(TimeSpan.FromMinutes(5)), "the page never reached its build");

        return (cut, session, navigation);
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
}
