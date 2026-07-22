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
}
