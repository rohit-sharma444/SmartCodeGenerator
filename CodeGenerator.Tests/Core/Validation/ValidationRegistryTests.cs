using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerator.Tests.Core.Validation;

[TestClass]
[DoNotParallelize]
public class ValidationRegistryTests
{
    private sealed class FakeRule : IValidationRule
    {
        public ValidationCategory Category { get; init; }
        public IReadOnlyList<string> Properties => [Category.ToString()];
        public string Describe() => Category.ToString();
        public void Emit(ValidationEmitContext ctx) { }
    }

    [TestInitialize]
    public void Reset() => ValidationRegistry.Clear();

    [TestMethod]
    public void Get_by_category_filters_stored_rules()
    {
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Data });
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Business });

        Assert.HasCount(2, ValidationRegistry.Get("Order"));
        Assert.HasCount(1, ValidationRegistry.Get("Order", ValidationCategory.Data));
        Assert.HasCount(1, ValidationRegistry.Get("Order", ValidationCategory.Business));
    }

    [TestMethod]
    public void Get_is_case_insensitive_and_empty_for_unknown_class()
    {
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Data });
        Assert.HasCount(1, ValidationRegistry.Get("order"));
        Assert.IsEmpty(ValidationRegistry.Get("Missing"));
    }

    [TestMethod]
    public void A_snapshot_restores_the_rules_as_they_were()
    {
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Data });
        var snapshot = ValidationRegistry.Snapshot();

        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Business });
        ValidationRegistry.Add("Invoice", new FakeRule { Category = ValidationCategory.Data });
        ValidationRegistry.Restore(snapshot);

        Assert.HasCount(1, ValidationRegistry.Get("Order"));
        Assert.IsEmpty(ValidationRegistry.Get("Invoice"));
    }

    [TestMethod]
    public void Rename_class_moves_its_rules_to_the_new_name()
    {
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Data });
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Business });

        var moved = ValidationRegistry.RenameClass("Order", "Purchase");

        Assert.AreEqual(2, moved);
        Assert.IsEmpty(ValidationRegistry.Get("Order"));
        Assert.HasCount(2, ValidationRegistry.Get("Purchase"));
    }

    [TestMethod]
    public void Remove_class_drops_its_rules_and_leaves_the_others()
    {
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Data });
        ValidationRegistry.Add("Invoice", new FakeRule { Category = ValidationCategory.Data });

        var removed = ValidationRegistry.RemoveClass("order");

        Assert.AreEqual(1, removed);
        Assert.IsEmpty(ValidationRegistry.Get("Order"));
        Assert.HasCount(1, ValidationRegistry.Get("Invoice"));
    }

    [TestMethod]
    public void A_snapshot_is_not_changed_by_later_adds()
    {
        var snapshot = ValidationRegistry.Snapshot();
        ValidationRegistry.Add("Order", new FakeRule { Category = ValidationCategory.Data });

        Assert.IsEmpty(snapshot);
    }
}
