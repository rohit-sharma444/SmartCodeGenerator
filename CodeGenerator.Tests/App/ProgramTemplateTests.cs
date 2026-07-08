using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ProgramTemplateTests
{
    [TestMethod]
    public void Build_ListsEachEntity_AndDispatchesCrud()
    {
        var source = ProgramTemplate.Build(new[] { "Payroll", "Application" });

        StringAssert.Contains(source, "using GeneratedCode;");
        StringAssert.Contains(source, "1) Payroll");
        StringAssert.Contains(source, "2) Application");
        StringAssert.Contains(source, "Crud.Run<Payroll>(\"Payroll\")");
        StringAssert.Contains(source, "Crud.Run<Application>(\"Application\")");
        StringAssert.Contains(source, "static class Crud");
    }
}
