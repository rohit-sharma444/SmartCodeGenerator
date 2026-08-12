using AppScaffolding;
using AppScaffolding.Targets;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>The copyright banner is prepended to generated C# only. MainWindow.xaml and the
/// Data/Seed/*.json files go through the same WriteSource, and a // banner corrupts both.</summary>
[TestClass]
public class FileHeaderInjectionTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), "hdr-" + Guid.NewGuid().ToString("N")[..8]);
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
    public void Generated_cs_files_carry_the_banner()
    {
        var request = ScaffoldRequest.For(
                TestModels.OneEntity(), "Shop", _root, new ClassLibraryTarget())
            with { Customization = AppCustomization.Default with { Company = "Codis Ltd" }, Year = 2026 };

        var result = new AppScaffolder().Scaffold(request);

        Assert.IsTrue(result.Success, result.Message);

        var order = File.ReadAllText(Path.Combine(result.ProjectPath, "Models", "Order.cs"));
        StringAssert.StartsWith(order, "// Copyright (c) 2026 Codis Ltd.");
    }

    [TestMethod]
    public void Non_cs_files_do_not_carry_the_banner()
    {
        var request = ScaffoldRequest.For(TestModels.OneEntity(), "Shop", _root, new WpfTarget())
            with { Customization = AppCustomization.Default with { Company = "Codis Ltd" }, Year = 2026 };

        var result = new AppScaffolder().Scaffold(request);

        var xaml = File.ReadAllText(Path.Combine(result.ProjectPath, "Views", "MainWindow.xaml"));
        Assert.IsFalse(xaml.StartsWith("//", StringComparison.Ordinal));
    }

    [TestMethod]
    public void No_company_means_no_banner()
    {
        var request = ScaffoldRequest.For(
            TestModels.OneEntity(), "Shop", _root, new ClassLibraryTarget());

        var result = new AppScaffolder().Scaffold(request);

        var order = File.ReadAllText(Path.Combine(result.ProjectPath, "Models", "Order.cs"));
        Assert.IsFalse(order.StartsWith("//", StringComparison.Ordinal));
    }
}
