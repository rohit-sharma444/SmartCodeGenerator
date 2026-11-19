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

        StringAssert.Contains(source, "class JsonStore<T> : IStore<T> where T : class, new()");
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
        Assert.AreEqual(DateTimeKind.Unspecified, ((DateTime)visitType.GetProperty("On")!.GetValue(visit)!).Kind);
        Assert.IsNull(visitType.GetProperty("Left")!.GetValue(visit));
        Assert.AreEqual(default(DateTime), visitType.GetProperty("CreatedOn")!.GetValue(visit));
    }

    /// <summary>
    /// The JSON store compiles without a warning under nullable reference types. Its T was
    /// constrained to <c>new()</c> alone, so T could be a nullable type and the date loop's
    /// <c>item.GetType()</c> was CS8602 in every generated JsonStore.cs - which a user building
    /// with -warnaserror could not get past. The SQLite store's <c>class, new()</c> never warned.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void The_JSON_store_compiles_without_a_warning(bool sessionAuth)
    {
        var files = new List<(string Path, string Code)>
        {
            ("Models/Visit.cs", "namespace Acme.Models;\n\npublic sealed class Visit\n{\n    public int Id { get; set; }\n}\n"),
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", Array.Empty<string>(), sessionAuth)),
            ("Validation/ActingUser.cs", ActingUserTemplate.Source("Acme", sessionAuth, sessionAuth)),
            ("Data/IStore.cs", StoreSeamTemplate.Source("Acme")),
            ("Data/SaveResult.cs", SaveResultTemplate.Source("Acme")),
            ("Data/StoreFactory.cs", StoreFactoryTemplate.Source("Acme", PersistenceKind.Json)),
            ("Data/JsonStore.cs", JsonStoreTemplate.Source("Acme.Data", sessionAuth)),
        };

        if (sessionAuth)
        {
            files.Add(("Models/User.cs", AppScaffolding.Auth.AuthModelTemplates.User("Acme")));
            files.Add(("Auth/AppRole.cs", AppScaffolding.Auth.AuthModelTemplates.Role("Acme")));
            files.Add(("Auth/Session.cs", AppScaffolding.Auth.SessionTemplate.Source("Acme")));
        }

        var result = CodeGenerationEngine.Core.Compiler.CompileSources(files);

        var errors = result.Diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        Assert.IsEmpty(errors, string.Join("\n", errors));

        var warnings = result.Diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning
                        && d.Location.SourceTree?.FilePath == "Data/JsonStore.cs")
            .Select(d => d.ToString())
            .ToList();
        Assert.IsEmpty(warnings, string.Join("\n", warnings));
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
