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

    /// <summary>
    /// A kept <c>.Custom.cs</c> stub written before the validation pipeline reaches the console.
    /// </summary>
    /// <remarks>
    /// It cannot ride on <see cref="IncrementalResult.Message"/>: this report never prints the
    /// message on the success path, so a note folded into it would be invisible on exactly the run
    /// that leaves a folder unable to compile.
    /// </remarks>
    [TestMethod]
    public void Names_a_validator_stub_that_predates_the_pipeline()
    {
        var result = new IncrementalResult(
            true, [Path.Combine(Folder, "Models", "Supplier.cs")], 41, "Added 1 class(es).")
        {
            OutdatedValidatorStubs =
            [
                @"Validation\CustomerValidator.Custom.cs declares ValidateCustom in the old shape",
            ],
        };

        var text = string.Join("\n", AddReport.Lines(result, Folder));

        StringAssert.Contains(text, "CustomerValidator.Custom.cs");
        StringAssert.Contains(text, "before the validation pipeline");
    }

    /// <summary>An ordinary add says nothing about stubs, so the report does not grow a section
    /// for every user who has nothing to fix.</summary>
    [TestMethod]
    public void Says_nothing_about_stubs_when_there_are_none()
    {
        var result = new IncrementalResult(
            true, [Path.Combine(Folder, "Models", "Supplier.cs")], 41, "Added 1 class(es).");

        var text = string.Join("\n", AddReport.Lines(result, Folder));

        Assert.DoesNotContain("pipeline", text);
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

    /// <summary>A file the add left as it was names the version beside it and the exact lines
    /// to copy in, so finishing the add needs no diff tool.</summary>
    [TestMethod]
    public void A_file_left_as_it_was_says_what_to_add()
    {
        var result = new IncrementalResult(
            true, [Path.Combine(Folder, "Models", "Supplier.cs")], 41, "Added 1 class(es).")
        {
            NotMerged =
            [
                new SetAside(
                    "Program.cs", "Program.cs.new",
                    [new LineChange([], ["MapEntity<Supplier>(app, \"supplier\");"])]),
            ],
        };

        var text = string.Join("\n", AddReport.Lines(result, Folder));

        StringAssert.Contains(text, "Program.cs.new");
        StringAssert.Contains(text, "+ MapEntity<Supplier>(app, \"supplier\");");
    }
}
