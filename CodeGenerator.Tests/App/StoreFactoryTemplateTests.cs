using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class StoreFactoryTemplateTests
{
    [TestMethod]
    public void Source_Json_CreatesJsonStore()
    {
        var source = StoreFactoryTemplate.Source("Payroll", PersistenceKind.Json);

        StringAssert.Contains(source, "namespace Payroll.Data");
        StringAssert.Contains(source, "public static class StoreFactory");
        StringAssert.Contains(source, "public static IStore<T> Create<T>() where T : class, new()");
        StringAssert.Contains(source, "new JsonStore<T>()");
    }

    [TestMethod]
    public void Source_Sqlite_CreatesEfStore()
    {
        var source = StoreFactoryTemplate.Source("Payroll", PersistenceKind.Sqlite);

        StringAssert.Contains(source, "new EfStore<T>()");
    }
}
