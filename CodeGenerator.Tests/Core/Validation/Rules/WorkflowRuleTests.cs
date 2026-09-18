using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation.Rules;

[TestClass]
public class WorkflowRuleTests
{
    private static ValidationEmitContext ContextFor(string property) =>
        new(new System.Text.StringBuilder(),
            [new PropertyDefinition { Name = property, Type = "string" }]);

    [TestMethod]
    public void Category_is_Workflow()
    {
        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Shipped"], new HashSet<string>());

        Assert.AreEqual(ValidationCategory.Workflow, rule.Category);
    }

    [TestMethod]
    public void Properties_names_the_one_status_property()
    {
        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Shipped"], new HashSet<string>());

        CollectionAssert.AreEqual(new[] { "Status" }, rule.Properties.ToArray());
    }

    [TestMethod]
    public void Emit_writes_a_backward_move_check_naming_both_values()
    {
        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Shipped"], new HashSet<string>());
        var ctx = ContextFor("Status");

        rule.Emit(ctx);
        var code = ctx.Source.ToString();

        StringAssert.Contains(code, "stored.Status");
        StringAssert.Contains(code, "item.Status");
        StringAssert.Contains(code, "\"Draft\"");
        StringAssert.Contains(code, "\"Approved\"");
        StringAssert.Contains(code, "\"Shipped\"");
    }

    [TestMethod]
    public void Emit_writes_a_final_status_check_naming_the_final_value()
    {
        var rule = new WorkflowRule("Status", ["Draft", "Approved", "Cancelled"], new HashSet<string> { "Cancelled" });
        var ctx = ContextFor("Status");

        rule.Emit(ctx);
        var code = ctx.Source.ToString();

        StringAssert.Contains(code, "\"Cancelled\"");
    }

    [TestMethod]
    public void Emit_skips_with_a_comment_when_the_property_is_missing()
    {
        var rule = new WorkflowRule("Status", ["Draft", "Approved"], new HashSet<string>());
        var ctx = ContextFor("SomethingElse");

        rule.Emit(ctx);

        StringAssert.Contains(ctx.Source.ToString(), "workflow rule skipped");
    }
}
