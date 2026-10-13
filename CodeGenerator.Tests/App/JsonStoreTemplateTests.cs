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
        StringAssert.Contains(source, "public StoreWrite SaveAll(List<T> all)");
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

    /// <summary>
    /// The Add button of the older layouts asks the store for a new record: its dates start at
    /// today, midnight, so the form never shows 01-Jan-0001.
    /// </summary>
    [TestMethod]
    public void NewItem_starts_its_dates_at_today()
    {
        using var app = EmittedDataLayer.Compile(EmittedLogin.None, validation: true, ("Models/Visit.cs", """
            using System;

            namespace Acme.Models;

            public sealed class Visit
            {
                public int Id { get; set; }
                public DateTime On { get; set; }
                public DateTime? Left { get; set; }
                public DateTime CreatedOn { get; set; }
            }
            """));

        var visitType = app.Assembly.GetType("Acme.Models.Visit")!;
        var store = Activator.CreateInstance(
            app.Assembly.GetType("Acme.Data.JsonStore`1")!.MakeGenericType(visitType))!;
        var empty = Activator.CreateInstance(typeof(List<>).MakeGenericType(visitType))!;

        var visit = store.GetType().GetMethod("NewItem")!.Invoke(store, [empty])!;

        Assert.AreEqual(1, visitType.GetProperty("Id")!.GetValue(visit));
        Assert.AreEqual(DateTime.Today, visitType.GetProperty("On")!.GetValue(visit));
        Assert.IsNull(visitType.GetProperty("Left")!.GetValue(visit));
        Assert.AreEqual(default(DateTime), visitType.GetProperty("CreatedOn")!.GetValue(visit));
    }

    /// <summary>No EF Core reference to run the SQLite store against; it shares the JSON store's
    /// fragment, which the test above runs.</summary>
    [TestMethod]
    public void The_SQLite_store_starts_new_dates_at_today_the_same_way()
    {
        var source = EfStoreTemplate.Source("Acme");

        StringAssert.Contains(source, NewRecordTemplate.StoreMember());
        StringAssert.Contains(source, "StartDatesAtToday(item);");
    }
}
