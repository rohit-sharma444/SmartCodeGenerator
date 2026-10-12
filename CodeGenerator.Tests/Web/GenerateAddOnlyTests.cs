using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// Writing into an app that is already there can add only the new classes, as the console can.
/// </summary>
/// <remarks>
/// The page used to offer only overwrite, rename or cancel, so adding a class to an app someone
/// had already edited meant regenerating over it. Under C:\cg-t\ rather than the temp path for the
/// reason IncrementalScaffolderTests gives: MAX_PATH.
/// </remarks>
[TestClass]
public class GenerateAddOnlyTests
{
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
            // A generated project's obj folder can hold a lock briefly.
        }
    }

    [TestMethod]
    public void The_overwrite_question_offers_to_add_the_new_classes()
    {
        var session = WrittenThenGrown(out _);

        using var context = new BunitContext();
        var rendered = context.Render<GenerateStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("button.write").Click();

        Assert.AreEqual("Add the 1 new class", rendered.Find("button.overwrite-add").TextContent.Trim());
        StringAssert.Contains(rendered.Find(".overwrite").TextContent, "of which 1 is new");
        Assert.DoesNotContain("[a]dd", rendered.Find(".overwrite").TextContent, "console key hints");

        // One sentence is one paragraph, not three console-wrapped lines.
        var paragraphs = rendered.FindAll(".overwrite p").Select(p => p.TextContent).ToList();
        Assert.IsTrue(paragraphs.Any(p => p.StartsWith("Regenerating overwrites") && p.EndsWith("are kept.")),
            string.Join(" | ", paragraphs));

        // In an actions row, so they get the workflow's button style rather than the browser's.
        Assert.HasCount(4, rendered.FindAll(".overwrite .actions button"));
    }

    /// <summary>Nothing new, nothing to add: the button would promise an empty write.</summary>
    [TestMethod]
    public void Nothing_new_offers_no_add()
    {
        var session = WrittenThenGrown(out _, grow: false);

        using var context = new BunitContext();
        var rendered = context.Render<GenerateStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("button.write").Click();

        Assert.IsEmpty(rendered.FindAll("button.overwrite-add"));
        Assert.IsNotNull(rendered.Find("button.overwrite-yes"));
    }

    [TestMethod]
    public void Adding_writes_the_new_class_and_leaves_the_existing_one_alone()
    {
        var session = WrittenThenGrown(out var customer);
        var before = File.ReadAllBytes(customer);

        var added = GenerateStep.AddNew(
            session, GenerateStep.NameFor(session), session.Target!, PersistenceKind.Json);

        Assert.IsTrue(added.Success, added.Message);
        Assert.IsTrue(File.Exists(Path.Combine(Path.GetDirectoryName(customer)!, "Supplier.cs")));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(customer), "an existing class was rewritten");
    }

    /// <summary>An app written from Customer, and a session whose model has since grown a
    /// Supplier - or not, when <paramref name="grow"/> is false.</summary>
    private BuildSession WrittenThenGrown(out string customerFile, bool grow = true)
    {
        var session = new BuildSession("a shop", _root, AppCustomization.Default);
        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(Model("Customer"));

        while (session.Current != BuildStage.Generate)
            session.Advance();

        var name = GenerateStep.NameFor(session);
        var first = GenerateStep.Scaffold(session, name, session.Target!, PersistenceKind.Json);
        Assert.IsTrue(first.Success, first.Message);

        customerFile = Path.Combine(first.ProjectPath, "Models", "Customer.cs");
        Assert.IsTrue(File.Exists(customerFile), customerFile);

        if (grow)
            session.Model!.Entities.Add(Entity("Supplier"));

        return session;
    }

    private static CandidateDomainModel Model(params string[] names)
    {
        var model = new CandidateDomainModel();

        foreach (var name in names)
            model.Entities.Add(Entity(name));

        return model;
    }

    private static CandidateEntity Entity(string name)
    {
        var entity = new CandidateEntity { Name = name, Provenance = Provenance.Wiki };
        entity.Fields.Add(new CandidateField { Name = "Title", Type = "string", Provenance = Provenance.Wiki });

        return entity;
    }
}
