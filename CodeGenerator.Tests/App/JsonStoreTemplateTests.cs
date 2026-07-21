using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class JsonStoreTemplateTests
{
    [TestMethod]
    public void Source_EmitsGenericStoreInTheGivenNamespace()
    {
        var source = JsonStoreTemplate.Source("Payroll.Data");

        StringAssert.Contains(source, "namespace Payroll.Data");
        StringAssert.Contains(source, "class JsonStore<T>");
        StringAssert.Contains(source, "AppContext.BaseDirectory");
    }

    [TestMethod]
    public void Source_ExposesTheBatchEditingSurface()
    {
        var source = JsonStoreTemplate.Source("Payroll.Data");

        StringAssert.Contains(source, "public List<T> GetAll()");
        StringAssert.Contains(source, "public bool SaveAll(List<T> all)");
        StringAssert.Contains(source, "public int NextId(IEnumerable<T> existing)");
        StringAssert.Contains(source, "public T NewItem(IEnumerable<T> existing)");
    }

    [TestMethod]
    public void Source_DropsThePerRecordConsoleSurface()
    {
        var source = JsonStoreTemplate.Source("Payroll.Data");

        Assert.DoesNotContain("public T Add(T item)", source, "Add is dead code in a grid app");
        Assert.DoesNotContain("public bool Update(T item)", source, "Update is dead code in a grid app");
        Assert.DoesNotContain("public bool Delete(int id)", source, "Delete is dead code in a grid app");
    }

    [TestMethod]
    public void Source_SurvivesACorruptStoreInsteadOfCrashingTheWindow()
    {
        var source = JsonStoreTemplate.Source("Payroll.Data");

        StringAssert.Contains(source, "JsonException");
        StringAssert.Contains(source, "UnauthorizedAccessException");
    }

    [TestMethod]
    public void Source_JsonStoreImplementsIStore()
    {
        var source = JsonStoreTemplate.Source("Payroll.Data");

        StringAssert.Contains(source, "class JsonStore<T> : IStore<T> where T : new()");
    }
}
