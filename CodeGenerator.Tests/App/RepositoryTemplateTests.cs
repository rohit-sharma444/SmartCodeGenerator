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
        StringAssert.Contains(source, "public SaveResult<T> Create(T item)");
        StringAssert.Contains(source, "public SaveResult<T> Update(T item)");
        StringAssert.Contains(source, "public bool Delete(int id)");
    }

    [TestMethod]
    public void The_repository_resolves_a_validator_and_returns_a_save_result()
    {
        var source = RepositoryTemplate.Base("Acme");

        Assert.Contains("ValidatorFactory.Create<T>()", source);
        Assert.Contains("public SaveResult<T> Create(T item)", source);
        Assert.Contains("public SaveResult<T> Update(T item)", source);
    }

    [TestMethod]
    public void The_repository_reports_a_missing_record_as_NotFound_not_as_a_validation_error()
    {
        Assert.Contains("NotFound = true", RepositoryTemplate.Base("Acme"));
    }

    [TestMethod]
    public void The_repository_validates_before_touching_the_store()
    {
        // Scoped to each write's own body: _store.GetAll() also appears in GetAll and GetById,
        // which precede both writes, so comparing indexes across the whole file proves nothing.
        foreach (var signature in new[] { "public SaveResult<T> Create(T item)", "public SaveResult<T> Update(T item)" })
        {
            var start = RepositoryTemplate.Base("Acme").IndexOf(signature, StringComparison.Ordinal);
            Assert.IsGreaterThan(0, start, signature + " should be emitted");

            var body = RepositoryTemplate.Base("Acme")[start..];
            var validate = body.IndexOf("_validator.Validate(item)", StringComparison.Ordinal);
            var load = body.IndexOf("_store.GetAll()", StringComparison.Ordinal);

            Assert.IsGreaterThan(0, validate, signature + " should validate");
            Assert.IsGreaterThan(0, load, signature + " should read the store");
            Assert.IsLessThan(load, validate, signature + " must validate before touching the store");
        }
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
