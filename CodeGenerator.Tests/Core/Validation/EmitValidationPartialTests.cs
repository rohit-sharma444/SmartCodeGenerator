using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation;

[TestClass]
public class EmitValidationPartialTests
{
    [TestMethod]
    public void Emits_a_partial_class_with_a_data_section()
    {
        var props = new List<PropertyDefinition> { new() { Name = "Name", Type = "string" } };
        var rules = new List<IValidationRule> { new RequiredRule("Name") };

        var code = ValidationEngine.EmitValidationPartial("Acme", "Employee", props, rules);

        StringAssert.Contains(code, "namespace Acme");
        StringAssert.Contains(code, "public partial class Employee");
        StringAssert.Contains(code, "public System.Collections.Generic.List<string> Validate()");
        StringAssert.Contains(code, "// Data validation");
        StringAssert.Contains(code, "Name is required.");
        StringAssert.Contains(code, "catch (System.Exception)");
        StringAssert.Contains(code, "return errors;");
    }
}
