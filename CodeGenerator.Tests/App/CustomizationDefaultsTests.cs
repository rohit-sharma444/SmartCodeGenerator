using AppScaffolding;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>The remembered profile. A broken one must leave the CLI working exactly as it does
/// without one — the precedent AiConfig.Load sets.</summary>
[TestClass]
public class CustomizationDefaultsTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), "cust-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveWorkspace()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [TestMethod]
    public void No_profile_yields_the_defaults()
        => Assert.AreEqual(AppCustomization.Default, CustomizationDefaults.Load(_root));

    [TestMethod]
    public void A_saved_profile_round_trips()
    {
        var saved = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            Theme = AppTheme.Dark,
            Accent = "#0B6E4F",
            TitlePattern = AppCustomization.CompanyTitlePattern,
            Logging = true,
            SampleSeedData = false,
            NamespacePrefix = "CodisLtd",
        };

        CustomizationDefaults.Save(_root, saved);

        Assert.AreEqual(saved, CustomizationDefaults.Load(_root));
    }

    [TestMethod]
    public void The_profile_is_written_under_dot_codegen()
    {
        CustomizationDefaults.Save(_root, AppCustomization.Default with { Company = "Codis" });

        Assert.IsTrue(File.Exists(Path.Combine(_root, ".codegen", CustomizationDefaults.FileName)));
    }

    [TestMethod]
    public void A_corrupt_profile_yields_the_defaults()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codegen"));
        File.WriteAllText(
            Path.Combine(_root, ".codegen", CustomizationDefaults.FileName), "{ not json");

        Assert.AreEqual(AppCustomization.Default, CustomizationDefaults.Load(_root));
    }

    /// <summary>A profile written by an older build, missing the fields a newer one added,
    /// must fill them from the defaults rather than with nulls.</summary>
    [TestMethod]
    public void A_partial_profile_fills_the_rest_from_the_defaults()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codegen"));
        File.WriteAllText(
            Path.Combine(_root, ".codegen", CustomizationDefaults.FileName),
            "{ \"company\": \"Codis Ltd\" }");

        var loaded = CustomizationDefaults.Load(_root);

        Assert.AreEqual("Codis Ltd", loaded.Company);
        Assert.IsTrue(loaded.Validation);
        Assert.AreEqual(AppTheme.Default, loaded.Theme);
    }

    [TestMethod]
    public void Saving_into_an_unwritable_root_does_not_throw()
        => CustomizationDefaults.Save(
            Path.Combine(_root, "no", "such", "\0bad"), AppCustomization.Default);

    /// <summary>Every other surface validates a namespace prefix — <c>InlineCustomization</c>
    /// refuses it and the wizard's <c>ParsePrefix</c> keeps the previous value. A hand-edited
    /// profile is not exempt: an invalid stored prefix must not flow into a namespace declaration
    /// that will not compile.</summary>
    [TestMethod]
    public void An_invalid_namespace_prefix_loads_as_the_default()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codegen"));
        File.WriteAllText(
            Path.Combine(_root, ".codegen", CustomizationDefaults.FileName),
            "{ \"namespacePrefix\": \"Codis Ltd\" }");

        var loaded = CustomizationDefaults.Load(_root);

        Assert.AreEqual(string.Empty, loaded.NamespacePrefix);
    }

    [TestMethod]
    public void A_layout_round_trips_through_the_profile()
    {
        CustomizationDefaults.Save(
            _root, AppCustomization.Default with { Layout = AppLayout.MasterDetail });

        Assert.AreEqual(AppLayout.MasterDetail, CustomizationDefaults.Load(_root).Layout);
    }

    /// <summary>A hand-edited profile bypasses the inline parser and the wizard, so Load
    /// re-validates — the rule the namespace prefix already follows.</summary>
    [TestMethod]
    public void An_unrecognized_stored_layout_falls_back_to_none()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codegen"));
        File.WriteAllText(
            Path.Combine(_root, ".codegen", CustomizationDefaults.FileName),
            "{ \"layout\": \"kanban\" }");

        Assert.IsNull(CustomizationDefaults.Load(_root).Layout);
    }

    /// <summary>A profile that names no layout must not gain one, or a later `layout tabs` would
    /// stop reading as typed.</summary>
    [TestMethod]
    public void A_profile_with_no_layout_stays_without_one()
    {
        CustomizationDefaults.Save(_root, AppCustomization.Default with { Company = "Codis Ltd" });

        Assert.IsNull(CustomizationDefaults.Load(_root).Layout);
        Assert.DoesNotContain(
            "\"layout\"",
            File.ReadAllText(Path.Combine(_root, ".codegen", CustomizationDefaults.FileName)),
            StringComparison.OrdinalIgnoreCase);
    }
}
