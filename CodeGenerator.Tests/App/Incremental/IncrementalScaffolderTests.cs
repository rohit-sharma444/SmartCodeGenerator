using AppScaffolding;
using AppScaffolding.Incremental;
using AppScaffolding.Targets;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App.Incremental;

/// <summary>
/// The claim the whole feature rests on: an add writes the new classes and leaves everything else
/// byte-identical.
/// </summary>
/// <remarks>
/// A real generate to a real folder. Under C:\cg-t\ and not the temp path, because a GUID plus a
/// project name plus Models\Whatever.cs blows MAX_PATH and the tell is MSB3030 claiming a file is
/// missing that is plainly there.
/// </remarks>
[TestClass]
public class IncrementalScaffolderTests
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
            // A generated project's obj folder can hold a lock briefly. A leftover under C:\cg-t
            // is not worth failing a green test over.
        }
    }

    private static CandidateDomainModel Model(params string[] names)
    {
        var model = new CandidateDomainModel();

        foreach (var name in names)
        {
            var entity = new CandidateEntity { Name = name, Provenance = Provenance.Wiki };
            entity.Fields.Add(new CandidateField
            {
                Name = "Title", Type = "string", Provenance = Provenance.Wiki
            });
            model.Entities.Add(entity);
        }

        return model;
    }

    /// <summary>
    /// The assumption the temp-and-copy approach depends on. If this fails, the approach is
    /// invalid and the plan needs revisiting — a copied file would differ from what a generate in
    /// place would have written.
    /// </summary>
    [TestMethod]
    public void Generated_content_does_not_depend_on_the_output_folder()
    {
        var target = new WpfTarget();
        var a = Path.Combine(_root, "a");
        var b = Path.Combine(_root, "b");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);

        new AppScaffolder().Scaffold(ScaffoldRequest.For(Model("Customer"), "Shop", a, target));
        new AppScaffolder().Scaffold(ScaffoldRequest.For(Model("Customer"), "Shop", b, target));

        var one = Path.Combine(
            GeneratedProjectPath.Folder(a, "Shop", target), "Models", "Customer.cs");
        var two = Path.Combine(
            GeneratedProjectPath.Folder(b, "Shop", target), "Models", "Customer.cs");

        Assert.IsTrue(File.Exists(one), one);
        Assert.IsTrue(File.Exists(two), two);

        CollectionAssert.AreEqual(File.ReadAllBytes(one), File.ReadAllBytes(two));
    }

    [TestMethod]
    public void An_add_writes_the_new_class_and_leaves_the_rest_alone()
    {
        var target = new WpfTarget();

        var first = new AppScaffolder().Scaffold(
            ScaffoldRequest.For(Model("Customer", "Order"), "Shop", _root, target));

        Assert.IsTrue(first.Success, first.Message);

        var folder = GeneratedProjectPath.Folder(_root, "Shop", target);
        var customerPath = Path.Combine(folder, "Models", "Customer.cs");

        Assert.IsTrue(File.Exists(customerPath), customerPath);

        var customerBefore = File.ReadAllBytes(customerPath);

        var result = IncrementalScaffolder.Add(
            ScaffoldRequest.For(Model("Customer", "Order", "Supplier"), "Shop", _root, target),
            ["Supplier"]);

        Assert.IsTrue(result.Success, result.Message);

        Assert.IsTrue(File.Exists(Path.Combine(folder, "Models", "Supplier.cs")));

        // THE ASSERTION THIS FEATURE EXISTS FOR.
        // CollectionAssert, because Assert.AreEqual on two byte[] compares references and
        // would fail even on identical content - a false red that would send someone hunting
        // a bug in the copy instead of in the test.
        CollectionAssert.AreEqual(
            customerBefore,
            File.ReadAllBytes(customerPath),
            "an existing class was rewritten by an add");
    }

    /// <summary>
    /// The three files that enumerate the entities. Miss any one and the new class has no
    /// screen, no seed data, or no registered validator - and the last two compile perfectly.
    /// </summary>
    [TestMethod]
    public void The_three_composition_files_learn_about_the_new_class()
    {
        var target = new WpfTarget();

        new AppScaffolder().Scaffold(
            ScaffoldRequest.For(Model("Customer"), "Shop", _root, target));

        var folder = GeneratedProjectPath.Folder(_root, "Shop", target);

        IncrementalScaffolder.Add(
            ScaffoldRequest.For(Model("Customer", "Supplier"), "Shop", _root, target),
            ["Supplier"]);

        foreach (var relative in new[]
                 {
                     Path.Combine("ViewModels", "MainViewModel.cs"),
                     Path.Combine("Data", "SeedData.cs"),
                     Path.Combine("Validation", "ValidatorFactory.cs"),
                 })
        {
            var path = Path.Combine(folder, relative);

            Assert.IsTrue(File.Exists(path), path);
            StringAssert.Contains(
                File.ReadAllText(path), "Supplier", $"{relative} does not know about it");
        }
    }

    /// <summary>
    /// Every file the full generate produced for the new class must have arrived. This proves
    /// the staging-and-copy mechanism carries them all - one left behind is silent otherwise,
    /// until the project does not compile.
    /// </summary>
    [TestMethod]
    public void No_file_of_a_new_class_is_left_behind()
    {
        var target = new WpfTarget();

        new AppScaffolder().Scaffold(
            ScaffoldRequest.For(Model("Customer"), "Shop", _root, target));

        var folder = GeneratedProjectPath.Folder(_root, "Shop", target);

        IncrementalScaffolder.Add(
            ScaffoldRequest.For(Model("Customer", "Supplier"), "Shop", _root, target),
            ["Supplier"]);

        // A full generate of the same model, elsewhere, is the reference.
        var reference = Path.Combine(_root, "ref");
        Directory.CreateDirectory(reference);

        new AppScaffolder().Scaffold(
            ScaffoldRequest.For(Model("Customer", "Supplier"), "Shop", reference, target));

        var referenceFolder = GeneratedProjectPath.Folder(reference, "Shop", target);

        var missing = Directory
            .GetFiles(referenceFolder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(referenceFolder, f))
            .Where(r => !r.StartsWith("obj", StringComparison.OrdinalIgnoreCase))
            .Where(r => !r.StartsWith("bin", StringComparison.OrdinalIgnoreCase))
            .Where(r => Path.GetFileName(r).StartsWith("Supplier", StringComparison.Ordinal))
            .Where(r => !File.Exists(Path.Combine(folder, r)))
            .ToList();

        Assert.IsEmpty(
            missing, $"files of the new class were not copied: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// A hand edit to an existing class survives an add. Before, every file whose generated
    /// content differed from disk was copied -- so any edit to Models/Customer.cs was reverted by
    /// adding a Supplier, the one thing "add" promised not to do.
    /// </summary>
    [TestMethod]
    public void An_add_keeps_a_hand_edit_to_an_existing_class()
    {
        var target = new WpfTarget();

        new AppScaffolder().Scaffold(ScaffoldRequest.For(Model("Customer"), "Shop", _root, target));

        var customerPath = Path.Combine(GeneratedProjectPath.Folder(_root, "Shop", target), "Models", "Customer.cs");
        File.AppendAllText(customerPath, "// MY HAND EDIT\r\n");

        IncrementalScaffolder.Add(
            ScaffoldRequest.For(Model("Customer", "Supplier"), "Shop", _root, target), ["Supplier"]);

        StringAssert.Contains(File.ReadAllText(customerPath), "// MY HAND EDIT", "an add reverted a hand edit");
    }

    /// <summary>
    /// An existing class the model has changed is left as it is on disk: the prompt says "Adding
    /// leaves them as they are", and overwrite is the choice that applies such a change.
    /// </summary>
    [TestMethod]
    public void An_add_leaves_a_changed_existing_class_as_it_is()
    {
        var target = new WpfTarget();

        new AppScaffolder().Scaffold(ScaffoldRequest.For(Model("Customer"), "Shop", _root, target));

        var customerPath = Path.Combine(GeneratedProjectPath.Folder(_root, "Shop", target), "Models", "Customer.cs");
        var before = File.ReadAllBytes(customerPath);

        var changed = Model("Customer", "Supplier");
        changed.Entities[0].Fields.Add(new CandidateField { Name = "Phone", Type = "string", Provenance = Provenance.Wiki });

        IncrementalScaffolder.Add(ScaffoldRequest.For(changed, "Shop", _root, target), ["Supplier"]);

        CollectionAssert.AreEqual(before, File.ReadAllBytes(customerPath), "an add rewrote a class it said it would leave");
    }

    private static ScaffoldRequest Request(
        CandidateDomainModel model, string root, ITargetScaffolder target,
        AppCustomization customization)
        => ScaffoldRequest.For(model, "Shop", root, target) with { Customization = customization };

    private static readonly AppCustomization Acme =
        AppCustomization.Default with { NamespacePrefix = "Acme", Company = "Acme Ltd" };

    /// <summary>
    /// An add writes the new class into the namespace the app already uses, not the one the
    /// current answers name. An app built without a prefix, then added to from a workspace whose
    /// profile named one, had files rewritten into ABCLtd.* and stopped compiling (88 errors).
    /// </summary>
    [TestMethod]
    [DataRow(true, DisplayName = "settings recorded at generate")]
    [DataRow(false, DisplayName = "app generated before the record existed")]
    public void An_add_uses_the_namespace_the_app_was_generated_with(bool recorded)
    {
        var target = new ConsoleTarget();

        new AppScaffolder().Scaffold(
            Request(Model("Customer"), _root, target, AppCustomization.Default));

        var folder = GeneratedProjectPath.Folder(_root, "Shop", target);
        var csproj = GeneratedProjectPath.CsprojPath(_root, "Shop", target);
        var csprojBefore = File.ReadAllBytes(csproj);

        if (!recorded)
            File.Delete(BuildSettingsRecord.PathFor(folder));

        var result = IncrementalScaffolder.Add(
            Request(Model("Customer", "Supplier"), _root, target, Acme), ["Supplier"]);

        Assert.IsTrue(result.Success, result.Message);

        var supplier = File.ReadAllText(Path.Combine(folder, "Models", "Supplier.cs"));
        StringAssert.Contains(supplier, "namespace Shop.Console.Models");
        Assert.DoesNotContain("Acme", supplier);

        foreach (var file in result.Written)
            Assert.DoesNotContain("Acme", File.ReadAllText(file), file);

        CollectionAssert.AreEqual(csprojBefore, File.ReadAllBytes(csproj), "the project file was rewritten");
    }

    /// <summary>The other direction: a prefixed app keeps its prefix when the add's answers
    /// name none.</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void An_add_keeps_the_prefix_of_a_prefixed_app(bool recorded)
    {
        var target = new ConsoleTarget();

        new AppScaffolder().Scaffold(Request(Model("Customer"), _root, target, Acme));

        var folder = GeneratedProjectPath.Folder(_root, "Shop", target);

        if (!recorded)
            File.Delete(BuildSettingsRecord.PathFor(folder));

        IncrementalScaffolder.Add(
            Request(Model("Customer", "Supplier"), _root, target, AppCustomization.Default),
            ["Supplier"]);

        StringAssert.Contains(
            File.ReadAllText(Path.Combine(folder, "Models", "Supplier.cs")),
            "namespace Acme.Shop.Console.Models");
    }

    [TestMethod]
    public void Adding_nothing_writes_nothing()
    {
        var target = new WpfTarget();

        new AppScaffolder().Scaffold(
            ScaffoldRequest.For(Model("Customer"), "Shop", _root, target));

        var result = IncrementalScaffolder.Add(
            ScaffoldRequest.For(Model("Customer"), "Shop", _root, target), []);

        Assert.IsEmpty(result.Written);
    }
}
