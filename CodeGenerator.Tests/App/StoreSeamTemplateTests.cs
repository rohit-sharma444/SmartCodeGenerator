using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class StoreSeamTemplateTests
{
    [TestMethod]
    public void Source_EmitsIStoreInterfaceInTheGivenNamespace()
    {
        var source = StoreSeamTemplate.Source("Payroll");

        StringAssert.Contains(source, "namespace Payroll.Data");
        StringAssert.Contains(source, "public interface IStore<T> where T : new()");
    }

    [TestMethod]
    public void Source_DeclaresTheFiveStoreMembers()
    {
        var source = StoreSeamTemplate.Source("Payroll");

        StringAssert.Contains(source, "List<T> GetAll();");
        StringAssert.Contains(source, "StoreWrite SaveAll(List<T> all);");
        StringAssert.Contains(source, "StoreWrite SaveAllUnchecked(List<T> all);");
        StringAssert.Contains(source, "int NextId(IEnumerable<T> existing);");
        StringAssert.Contains(source, "T NewItem(IEnumerable<T> existing);");
    }
}
