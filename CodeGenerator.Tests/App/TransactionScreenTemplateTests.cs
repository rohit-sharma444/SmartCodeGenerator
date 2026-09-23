using AppScaffolding.Screens;

namespace CodeGenerator.Tests.App;

[TestClass]
public class TransactionScreenTemplateTests
{
    [TestMethod]
    public void EditsInline_defaults_to_false()
        => StringAssert.Contains(
            TransactionScreenTemplate.Source("Payroll"),
            "protected override bool EditsInline => false;");

    [TestMethod]
    public void EditsInline_can_be_emitted_true_for_split()
        => StringAssert.Contains(
            TransactionScreenTemplate.Source("Payroll", editsInline: true),
            "protected override bool EditsInline => true;");
}
