using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class RepositoryTemplateTests
{
    [TestMethod]
    public void Base_EmitsGenericRepositoryInTheGivenNamespace()
    {
        var source = RepositoryTemplate.Base("Payroll");

        StringAssert.Contains(source, "namespace Payroll.Data");
        StringAssert.Contains(source, "abstract class Repository<T>");
        StringAssert.Contains(source, "where T : class, new()");
        StringAssert.Contains(source, "IStore<T> _store = StoreFactory.Create<T>();");
    }

    [TestMethod]
    public void Base_ExposesTheFiveCrudFunctions()
    {
        var source = RepositoryTemplate.Base("Payroll");

        StringAssert.Contains(source, "public List<T> GetAll()");
        StringAssert.Contains(source, "public T? GetById(int id)");
        StringAssert.Contains(source, "public T Create(T item)");
        StringAssert.Contains(source, "public bool Update(T item)");
        StringAssert.Contains(source, "public bool Delete(int id)");
    }

    [TestMethod]
    public void Base_PersistsThroughJsonStoresExistingApi()
    {
        var source = RepositoryTemplate.Base("Payroll");

        // Standalone surface: it reuses JsonStore's batch API rather than a new Add/Update/Delete.
        StringAssert.Contains(source, "_store.GetAll()");
        StringAssert.Contains(source, "_store.SaveAll(all)");
        StringAssert.Contains(source, "_store.NextId(all)");
    }

    [TestMethod]
    public void Typed_SubclassesTheGenericBaseForTheEntity()
    {
        var source = RepositoryTemplate.Typed("Payroll", "Employee");

        StringAssert.Contains(source, "namespace Payroll.Data");
        StringAssert.Contains(source, "using Payroll.Models;");
        StringAssert.Contains(source, "class EmployeeRepository : Repository<Employee>");
    }
}
