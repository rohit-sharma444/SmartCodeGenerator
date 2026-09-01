using AppScaffolding.Incremental;

namespace CodeGenerator.Tests.App.Incremental;

/// <summary>
/// Which generated files an add is allowed to write, decided by comparing what a full generate
/// would produce against what is already there.
/// </summary>
[TestClass]
public class IncrementalFilesTests
{
    [TestMethod]
    public void A_file_that_is_not_there_is_copied()
        => Assert.IsTrue(IncrementalFiles.ShouldCopy(
            @"Models\Supplier.cs", "class Supplier {}", existingContent: null));

    /// <summary>
    /// The assertion the whole feature rests on. An untouched file is not written at all, so
    /// "byte-identical" is true by construction rather than by a filter being right.
    /// </summary>
    [TestMethod]
    public void A_file_whose_content_has_not_changed_is_left_alone()
        => Assert.IsFalse(IncrementalFiles.ShouldCopy(
            @"Models\Customer.cs", "class Customer {}", "class Customer {}"));

    /// <summary>
    /// The composition files — MainViewModel, SeedData, ValidatorFactory — and nothing else. They
    /// change precisely because the new class is in them.
    /// </summary>
    [TestMethod]
    public void A_file_the_new_class_changed_is_copied()
        => Assert.IsTrue(IncrementalFiles.ShouldCopy(
            @"ViewModels\MainViewModel.cs",
            "Entities = [Customer, Supplier];",
            "Entities = [Customer];"));

    /// <summary>
    /// OverwritePrompt promises in as many words that .Custom.cs rules are kept. A user who has
    /// written a rule there must not lose it to an add, and the staged copy is always an empty
    /// stub — so "differs" would otherwise overwrite it every single time.
    /// </summary>
    [TestMethod]
    public void An_existing_custom_validator_is_never_overwritten()
        => Assert.IsFalse(IncrementalFiles.ShouldCopy(
            @"Validation\CustomerValidator.Custom.cs",
            "// add your rules here",
            "if (Age < 18) yield return \"too young\";"));

    /// <summary>But a NEW class's stub has to be written, or its validator half is missing.</summary>
    [TestMethod]
    public void A_new_custom_validator_stub_is_written()
        => Assert.IsTrue(IncrementalFiles.ShouldCopy(
            @"Validation\SupplierValidator.Custom.cs",
            "// add your rules here",
            existingContent: null));

    [TestMethod]
    public void A_generated_validator_half_is_copied_when_it_changed()
        => Assert.IsTrue(IncrementalFiles.ShouldCopy(
            @"Validation\SupplierValidator.Generated.cs", "new", "old"));

    /// <summary>Case, because Windows file names are case-insensitive and the rule must not be
    /// defeated by CUSTOM.CS.</summary>
    [TestMethod]
    public void The_custom_rule_is_not_defeated_by_casing()
        => Assert.IsFalse(IncrementalFiles.ShouldCopy(
            @"Validation\CustomerValidator.CUSTOM.CS", "stub", "my rules"));

    /// <summary>Paths come from Directory enumeration, so the separator is the platform's.</summary>
    [TestMethod]
    public void A_forward_slash_path_reads_the_same()
        => Assert.IsFalse(IncrementalFiles.ShouldCopy(
            "Validation/CustomerValidator.Custom.cs", "stub", "my rules"));
}
