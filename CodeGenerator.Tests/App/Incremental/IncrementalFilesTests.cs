using AppScaffolding.Incremental;

namespace CodeGenerator.Tests.App.Incremental;

/// <summary>
/// What an add does with each generated file, decided from what the generator writes for the app
/// as it is (before), what it writes with the new classes (after), and what is on disk.
/// </summary>
[TestClass]
public class IncrementalFilesTests
{
    [TestMethod]
    public void A_file_new_with_this_add_is_created()
        => Assert.AreEqual(FileAction.Create, IncrementalFiles.Decide(
            @"Models\Supplier.cs", before: null, "class Supplier {}", existing: null));

    /// <summary>
    /// The assertion the whole feature rests on. A file the add does not change is not written at
    /// all, however much the copy on disk differs - an existing class among them.
    /// </summary>
    [TestMethod]
    public void A_file_the_add_does_not_change_is_left_alone()
        => Assert.AreEqual(FileAction.Leave, IncrementalFiles.Decide(
            @"Models\Book.cs", "class Book { Author }", "class Book { Author }",
            "class Book { Author; Label() }"));

    /// <summary>The composition files change because the new class is in them. Untouched since
    /// they were generated, they are simply replaced.</summary>
    [TestMethod]
    public void An_unedited_file_the_add_changes_is_replaced()
        => Assert.AreEqual(FileAction.Replace, IncrementalFiles.Decide(
            @"ViewModels\MainViewModel.cs",
            "Entities = [Customer];", "Entities = [Customer, Supplier];", "Entities = [Customer];"));

    /// <summary>Edited since, they get only the add's lines.</summary>
    [TestMethod]
    public void An_edited_file_the_add_changes_is_merged()
        => Assert.AreEqual(FileAction.Merge, IncrementalFiles.Decide(
            "Program.cs", "Map<Customer>();", "Map<Customer>();\nMap<Supplier>();",
            "MapGet(\"/ping\");\nMap<Customer>();"));

    /// <summary>A file generated for the app as it is and missing from disk was removed, or
    /// belongs to a feature the app was built without. An add does not bring it back.</summary>
    [TestMethod]
    public void A_file_the_app_does_not_have_is_not_brought_back()
        => Assert.AreEqual(FileAction.Leave, IncrementalFiles.Decide(
            @"Logging\AppLog.cs", "class AppLog {}", "class AppLog {}", existing: null));

    /// <summary>The add would create a file, and the user already has one there.</summary>
    [TestMethod]
    public void A_user_file_where_the_add_would_create_one_is_set_aside()
        => Assert.AreEqual(FileAction.SetAside, IncrementalFiles.Decide(
            @"Data\SupplierRepository.cs", before: null, "generated", "mine"));

    /// <summary>
    /// OverwritePrompt promises in as many words that .Custom.cs rules are kept. A user who has
    /// written a rule there must not lose it to an add, and the staged copy is always an empty
    /// stub.
    /// </summary>
    [TestMethod]
    public void An_existing_custom_validator_is_never_overwritten()
        => Assert.AreEqual(FileAction.Leave, IncrementalFiles.Decide(
            @"Validation\CustomerValidator.Custom.cs",
            "// add your rules here", "// add your rules here, now",
            "if (Age < 18) yield return \"too young\";"));

    /// <summary>But a NEW class's stub has to be written, or its validator half is missing.</summary>
    [TestMethod]
    public void A_new_custom_validator_stub_is_written()
        => Assert.AreEqual(FileAction.Create, IncrementalFiles.Decide(
            @"Validation\SupplierValidator.Custom.cs", before: null, "// add your rules here",
            existing: null));

    /// <summary>Case, because Windows file names are case-insensitive and the rule must not be
    /// defeated by CUSTOM.CS.</summary>
    [TestMethod]
    public void The_custom_rule_is_not_defeated_by_casing()
        => Assert.AreEqual(FileAction.Leave, IncrementalFiles.Decide(
            @"Validation\CustomerValidator.CUSTOM.CS", "stub", "stub 2", "my rules"));

    /// <summary>Paths come from Directory enumeration, so the separator is the platform's.</summary>
    [TestMethod]
    public void A_forward_slash_path_reads_the_same()
        => Assert.AreEqual(FileAction.Leave, IncrementalFiles.Decide(
            "Validation/CustomerValidator.Custom.cs", "stub", "stub 2", "my rules"));
}
