using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.App.Resume;
using CodeGenerator.Web.Workflow;
using DataIngestion;
using DomainDiscovery.Review;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// A class or field renamed on Review keeps the rows it was read with.
/// </summary>
/// <remarks>
/// The imported rows are keyed by the names the file was read under, and renaming changed the
/// model and nothing else: rename Customer to CustomerName and the generated app's CustomerName
/// column came out empty, on the web, on the console and after a resume alike. Under C:\cg-t\ for
/// the reason IncrementalScaffolderTests gives: MAX_PATH.
/// </remarks>
[TestClass]
public class RenamedFieldsKeepTheirDataTests
{
    private string _root = string.Empty;
    private string _csv = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(@"C:\cg-t", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);

        _csv = Path.Combine(_root, "orders.csv");
        File.WriteAllLines(_csv,
        [
            "OrderNumber,Customer,City",
            "SO-1,Harbor Cafe,Seattle",
            "SO-2,Maple and Co,Boston",
        ]);
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
    public void A_renamed_field_is_seeded_under_its_new_name()
    {
        var session = Read();
        Assert.IsTrue(ModelEditor.RenameField(session.Model!, "Order", "Customer", "CustomerName"));

        var seed = SeedOf(session);

        StringAssert.Contains(seed, "\"CustomerName\": \"Harbor Cafe\"");
        Assert.DoesNotContain("\"Customer\":", seed, "the old name should not be written as well");
    }

    [TestMethod]
    public void A_renamed_class_is_seeded_under_its_new_name()
    {
        var session = Read();
        Assert.IsTrue(ModelEditor.RenameEntity(session.Model!, "Order", "Purchase"));

        var project = Scaffold(session);

        var seed = File.ReadAllText(Path.Combine(project, "Data", "Seed", "Purchase.json"));
        StringAssert.Contains(seed, "\"Customer\": \"Harbor Cafe\"");
    }

    /// <summary>Two fields trading names must trade values too, not both take one.</summary>
    [TestMethod]
    public void Two_fields_that_swap_names_swap_their_values()
    {
        var session = Read();
        var model = session.Model!;
        Assert.IsTrue(ModelEditor.RenameField(model, "Order", "City", "Town"));
        Assert.IsTrue(ModelEditor.RenameField(model, "Order", "Customer", "City"));
        Assert.IsTrue(ModelEditor.RenameField(model, "Order", "Town", "Customer"));

        var seed = SeedOf(session);

        StringAssert.Contains(seed, "\"City\": \"Harbor Cafe\"");
        StringAssert.Contains(seed, "\"Customer\": \"Seattle\"");
    }

    /// <summary>The rows are read again on resume, under the file's names, and the renamed model
    /// comes back from the snapshot - so the name each field was imported under has to come back
    /// with it.</summary>
    [TestMethod]
    public void A_resumed_build_keeps_the_rename_and_the_data()
    {
        var session = Read();
        Assert.IsTrue(ModelEditor.RenameField(session.Model!, "Order", "Customer", "CustomerName"));

        BuildSnapshotStore.Save(_root, BuildSnapshot.Of(session));
        var resumed = ResumedSession.From(BuildSnapshotStore.Load(_root)!, _root, out var notice);
        Assert.IsNull(notice, notice);

        StringAssert.Contains(SeedOf(resumed), "\"CustomerName\": \"Harbor Cafe\"");
    }

    private BuildSession Read()
    {
        var session = new BuildSession(
            "orders", _root, AppCustomization.Default, new DataSourceSpec(DataSourceKind.Csv, _csv));
        session.SetTarget(new ConsoleTarget());
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();

        Assert.IsTrue(DiscoveryStep.TryRead(session.Source!, out var read, out var error), error);
        var (model, imported) = DiscoveryStep.BuildFromData(read, []);
        Assert.IsTrue(session.SetModel(model, imported: imported, normalized: []));

        return session;
    }

    private string Scaffold(BuildSession session)
    {
        var result = GenerateStep.Scaffold(
            session, GenerateStep.NameFor(session), session.Target!, PersistenceKind.Json);
        Assert.IsTrue(result.Success, result.Message);

        return result.ProjectPath;
    }

    private string SeedOf(BuildSession session)
    {
        var name = session.Model!.Entities.Single().Name;

        return File.ReadAllText(Path.Combine(Scaffold(session), "Data", "Seed", $"{name}.json"));
    }
}
