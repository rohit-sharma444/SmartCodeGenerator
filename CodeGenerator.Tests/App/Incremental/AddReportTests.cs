using AppScaffolding.Incremental;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App.Incremental;

[TestClass]
public class AddReportTests
{
    private const string Folder = @"D:\Apps\Shop.Wpf";

    [TestMethod]
    public void Names_every_file_written()
    {
        var result = new IncrementalResult(
            true,
            [Path.Combine(Folder, "Models", "Supplier.cs"),
             Path.Combine(Folder, "ViewModels", "MainViewModel.cs")],
            41,
            "Added 1 class(es).");

        var text = string.Join("\n", AddReport.Lines(result, Folder));

        StringAssert.Contains(text, Path.Combine("Models", "Supplier.cs"));
        StringAssert.Contains(text, Path.Combine("ViewModels", "MainViewModel.cs"));
    }

    /// <summary>
    /// The half a generator never usually says, and the half a user actually wants verified.
    /// </summary>
    [TestMethod]
    public void Says_how_many_files_it_did_not_touch()
    {
        var result = new IncrementalResult(
            true, [Path.Combine(Folder, "Models", "Supplier.cs")], 41, "Added 1 class(es).");

        var text = string.Join("\n", AddReport.Lines(result, Folder));

        StringAssert.Contains(text, "41");
        StringAssert.Contains(text, "untouched");
    }

    [TestMethod]
    public void A_failure_says_what_went_wrong_and_claims_nothing()
    {
        var result = new IncrementalResult(false, [], 41, "Access to the path is denied.");

        var text = string.Join("\n", AddReport.Lines(result, Folder));

        StringAssert.Contains(text, "Access to the path is denied.");
        Assert.DoesNotContain("untouched", text);
    }

    /// <summary>Relative, because the absolute path is already on the line above and repeating it
    /// forty times turns a report into a wall.</summary>
    [TestMethod]
    public void The_paths_are_relative_to_the_project()
    {
        var result = new IncrementalResult(
            true, [Path.Combine(Folder, "Models", "Supplier.cs")], 41, "Added 1 class(es).");

        var text = string.Join("\n", AddReport.Lines(result, Folder));

        Assert.DoesNotContain(Folder, text);
    }
}
