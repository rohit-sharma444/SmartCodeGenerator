using AppScaffolding;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Every generated model carries CreatedOn and ModifiedOn, and nothing used to fill them in: a
/// seeded row, and a record added or changed through the Web API, all said 0001-01-01. These run
/// the real emitted Repository and JsonStore.
/// </summary>
[TestClass]
public class AuditFieldStampingTests
{
    [TestMethod]
    public void A_created_record_is_stamped_with_when_it_was_created()
    {
        using var app = EmittedDataLayer.Compile(EmittedLogin.None);
        var before = DateTime.Now.AddSeconds(-1);

        var created = EmittedDataLayer.Call(app.NewRepository(), "Create", app.NewItem());
        Assert.IsTrue(created.Success, string.Join("; ", created.Errors));

        // The returned item, which is what the Web API's POST answers with, and what is on disk.
        AssertRecent(app, created.Item!, before);
        AssertRecent(app, StoredAgain(app, created.Item!), before);
    }

    [TestMethod]
    public void An_updated_record_keeps_its_CreatedOn_and_moves_its_ModifiedOn()
    {
        using var app = EmittedDataLayer.Compile(EmittedLogin.None);
        var repository = app.NewRepository();

        var created = EmittedDataLayer.Call(repository, "Create", app.NewItem()).Item!;
        var createdOn = Audit(app, created, "CreatedOn");
        var firstModified = Audit(app, created, "ModifiedOn");

        // What a PUT with no CreatedOn in its body deserialises to.
        var edit = app.NewItem("Changed");
        app.Model.GetProperty("Id")!.SetValue(edit, app.Model.GetProperty("Id")!.GetValue(created));
        Thread.Sleep(20);

        var updated = EmittedDataLayer.Call(repository, "Update", edit);
        Assert.IsTrue(updated.Success, string.Join("; ", updated.Errors));

        var stored = StoredAgain(app, created);
        Assert.AreEqual(createdOn, Audit(app, stored, "CreatedOn"), "an update must not lose CreatedOn");
        Assert.IsGreaterThan(firstModified, Audit(app, stored, "ModifiedOn"));
    }

    [TestMethod]
    public void A_record_the_save_did_not_change_keeps_its_ModifiedOn()
    {
        // The screens save the whole list at once. A row nobody touched was not modified.
        using var app = EmittedDataLayer.Compile(EmittedLogin.None);
        var repository = app.NewRepository();

        var first = EmittedDataLayer.Call(repository, "Create", app.NewItem("First")).Item!;
        var firstModified = Audit(app, StoredAgain(app, first), "ModifiedOn");
        Thread.Sleep(20);

        EmittedDataLayer.Call(repository, "Create", app.NewItem("Second"));

        Assert.AreEqual(firstModified, Audit(app, StoredAgain(app, first), "ModifiedOn"));
    }

    [TestMethod]
    public void Seeded_rows_are_stamped_when_they_are_seeded()
    {
        // The seeders write through SaveAllUnchecked, and imported rows carry no audit fields.
        using var app = EmittedDataLayer.Compile(EmittedLogin.None);
        var before = DateTime.Now.AddSeconds(-1);

        var store = app.Assembly.GetType("Acme.Data.StoreFactory")!.GetMethod("Create")!
            .MakeGenericMethod(app.Model).Invoke(null, null)!;

        var rows = (System.Collections.IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(app.Model))!;
        var row = app.NewItem("Seeded");
        app.Model.GetProperty("Id")!.SetValue(row, 1);
        rows.Add(row);

        store.GetType().GetMethod("SaveAllUnchecked")!.Invoke(store, new object[] { rows });

        AssertRecent(app, StoredAgain(app, row), before);
    }

    [TestMethod]
    public void The_SQLite_store_stamps_the_same_way()
    {
        // No EF Core reference to run it against; the shared fragment is what the JSON tests run.
        var source = EfStoreTemplate.Source("Acme");

        StringAssert.Contains(source, "StampAuditFields(all, existing);");
        StringAssert.Contains(source, "using System.Text.Json;");
    }

    private static DateTime Audit(EmittedDataLayer app, object item, string name)
        => (DateTime)app.Model.GetProperty(name)!.GetValue(item)!;

    private static void AssertRecent(EmittedDataLayer app, object item, DateTime notBefore)
    {
        foreach (var name in new[] { "CreatedOn", "ModifiedOn" })
        {
            var value = Audit(app, item, name);

            Assert.IsGreaterThanOrEqualTo(notBefore, value, name + " was not stamped");
            Assert.IsLessThanOrEqualTo(DateTime.Now.AddSeconds(1), value, name);
        }
    }

    /// <summary>The record with <paramref name="item"/>'s Id, read back through a fresh
    /// repository, so what is asserted is what was written.</summary>
    private static object StoredAgain(EmittedDataLayer app, object item)
    {
        var repository = app.NewRepository();
        var id = (int)app.Model.GetProperty("Id")!.GetValue(item)!;

        return repository.GetType().GetMethod("GetById")!.Invoke(repository, new object[] { id })!;
    }
}
