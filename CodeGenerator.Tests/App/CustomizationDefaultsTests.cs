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
}
