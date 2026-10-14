using System.Reflection;
using System.Text.Json;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using DataIngestion.Schema;
using DataIngestion.Schema.Readers.Sql;
using DataIngestion.Tests.Schema.Readers.Sql;
using DomainDiscovery.Model;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataIngestion.Tests;

/// <summary>
/// Keys a source holds that are not an <c>int</c> called <c>Id</c> or <c>&lt;Table&gt;Id</c>: a
/// currency keyed by its code, a region by a letter, a ticket by its number, a product by its
/// SKU; a member of staff whose manager is another member of staff; a payment paid by one member
/// of staff and approved by another. Every row is numbered 1..N in the app, so each of these must
/// keep the values the user's data holds AND have every reference to it follow the renumbering.
/// </summary>
/// <remarks>
/// Read through <see cref="SqlServerReader"/> over a fake database, so the declared primary and
/// foreign keys arrive exactly as the real reader hands them on.
/// </remarks>
public class SourceKeyTests
{
    private static FakeSqlSchemaSource Database()
    {
        var source = new FakeSqlSchemaSource();

        void Table(string name, SqlColumnInfo[] columns, params string?[][] rows)
        {
            source.TableList.Add(new SqlTableInfo("dbo", name, columns.Length, rows.Length));
            source.ColumnsByTable[name] = [.. columns];
            source.RowsByTable[name] = [.. rows];
        }

        void Key(string table, string column, string target, string targetColumn)
            => source.Keys.Add(new TabularForeignKey(column, target, targetColumn, table, "dbo", "dbo"));

        Table("Currency",
            [new("CurrencyId", "char", false, 3, IsPrimaryKey: true), new("CurrencyName", "nvarchar", false, 50)],
            ["GBP", "Pound"], ["EUR", "Euro"], ["USD", "Dollar"]);

        Table("Region",
            [new("RegionCode", "nchar", false, 1, IsPrimaryKey: true), new("RegionName", "nvarchar", false, 50)],
            ["N", "North"], ["S", "South"]);

        Table("Staff",
            [
                new("StaffId", "int", false, IsPrimaryKey: true), new("Name", "nvarchar", false, 50),
                new("ManagerId", "int", true), new("RegionCode", "nchar", false, 1)
            ],
            ["500", "Ann", null, "S"], ["510", "Bob", "500", "N"], ["520", "Cy", "510", "S"]);
        Key("Staff", "ManagerId", "Staff", "StaffId");
        Key("Staff", "RegionCode", "Region", "RegionCode");

        Table("Ticket",
            [new("TicketNo", "int", false, IsPrimaryKey: true), new("Subject", "nvarchar", false, 50)],
            ["7001", "Login fails"], ["7002", "Slow page"]);

        Table("Product",
            [new("Sku", "nvarchar", false, 10, IsPrimaryKey: true), new("ProductName", "nvarchar", false, 50)],
            ["NUT", "Nut"], ["BOLT", "Bolt"]);

        Table("InvoiceLine",
            [new("InvoiceLineId", "int", false, IsPrimaryKey: true), new("Sku", "nvarchar", false, 10), new("Qty", "int", false)],
            ["1", "BOLT", "4"], ["2", "NUT", "9"]);
        Key("InvoiceLine", "Sku", "Product", "Sku");

        Table("Payment",
            [
                new("PaymentId", "int", false, IsPrimaryKey: true), new("TicketNo", "int", false),
                new("CurrencyId", "char", false, 3), new("PaidById", "int", false),
                new("ApprovedById", "int", true), new("Amount", "decimal", false, Precision: 10, Scale: 2),
                new("Ref", "nvarchar", false, 20)
            ],
            ["1", "7002", "EUR", "510", "500", "10.50", "PAY-1"], ["2", "7001", "GBP", "520", null, "4.00", "PAY-2"]);
        Key("Payment", "TicketNo", "Ticket", "TicketNo");
        Key("Payment", "CurrencyId", "Currency", "CurrencyId");
        Key("Payment", "PaidById", "Staff", "StaffId");
        Key("Payment", "ApprovedById", "Staff", "StaffId");

        return source;
    }

    private static IReadOnlyList<TabularTable> Read()
        => new SqlServerReader(Database()).Read(new DataSourceSpec(DataSourceKind.Sql, "Server=.", "Shop", []));

    private sealed record Generated(
        CandidateDomainModel Model,
        IReadOnlyList<ClassDefinition> Classes,
        Dictionary<string, JsonElement[]> Seeds,
        ImportedData Data);

    private static Generated Generate()
    {
        var tables = Read();
        var model = DataModelBuilder.Build(tables);
        var classes = DomainModelMapper.ToClassDefinitions(model, out var classNames);
        var data = TabularImport.ToImportedData(tables);

        var seeds = SeedFileWriter.Files(classes, classNames, data)
            .ToDictionary(
                f => Path.GetFileNameWithoutExtension(f.FileName),
                f => JsonDocument.Parse(f.Json).RootElement.EnumerateArray().Select(e => e.Clone()).ToArray());

        return new Generated(model, classes, seeds, data);
    }

    /// <summary><paramref name="field"/> of the <paramref name="target"/> row each
    /// <paramref name="owner"/> row's <paramref name="key"/> names, keyed by the owner's
    /// <paramref name="label"/>; null where it names none.</summary>
    private static Dictionary<string, string?> Followed(
        Generated app, string owner, string label, string key, string target, string field)
    {
        var byId = app.Seeds[target].ToDictionary(
            r => r.GetProperty("Id").GetInt32(), r => r.GetProperty(field).ToString());

        return app.Seeds[owner].ToDictionary(
            r => r.GetProperty(label).ToString(),
            r => r.TryGetProperty(key, out var id) && byId.TryGetValue(id.GetInt32(), out var value)
                ? value
                : null);
    }

    private static ClassDefinition Class(Generated app, string name) => app.Classes.Single(c => c.Name == name);

    private static PropertyDefinition Property(ClassDefinition definition, string name)
        => definition.Properties.Single(p => p.Name == name);

    // --- K1: a key that is not a number keeps its values, and the references follow it ---

    [Fact]
    public void A_text_primary_key_keeps_its_codes_and_the_references_to_it_link_to_the_right_row()
    {
        var app = Generate();

        var currency = Class(app, "Currency");
        Assert.Equal("string", Property(currency, "CurrencyCode").Type);
        Assert.Equal(["GBP", "EUR", "USD"], app.Seeds["Currency"].Select(r => r.GetProperty("CurrencyCode").GetString()));

        var payment = app.Model.Entities.Single(e => e.Name == "Payment");
        Assert.Equal("int", payment.Fields.Single(f => f.Name == "CurrencyId").Type);
        Assert.Contains(payment.Relationships, r => r.Target == "Currency" && r.ForeignKey == "CurrencyId");

        var currencyOf = Followed(app, "Payment", "Ref", "CurrencyId", "Currency", "CurrencyCode");
        Assert.Equal("EUR", currencyOf["PAY-1"]);
        Assert.Equal("GBP", currencyOf["PAY-2"]);

        Assert.Contains(app.Model.Decisions, d => d.Name == "CurrencyId" && d.Reason == DecisionReasons.KeyKeptAsAField);
    }

    /// <summary>The spreadsheet case: no declared keys at all, a customer keyed C001, and an order
    /// naming its customer by that code.</summary>
    [Fact]
    public void A_text_key_found_by_its_name_links_the_text_references_that_name_it()
    {
        var customers = new TabularTable { Name = "Customers", SourceLabel = "keys.xlsx" };
        customers.Columns.Add(new TabularColumn { Name = "CustomerId" });
        customers.Columns.Add(new TabularColumn { Name = "Name" });
        customers.Rows.Add(["C001", "Ada"]);
        customers.Rows.Add(["C002", "Bob"]);

        var orders = new TabularTable { Name = "Orders", SourceLabel = "keys.xlsx" };
        orders.Columns.Add(new TabularColumn { Name = "OrderId", Type = "int" });
        orders.Columns.Add(new TabularColumn { Name = "CustomerId" });
        orders.Columns.Add(new TabularColumn { Name = "Ref" });
        orders.Rows.Add(["1", "C002", "ORD-BOB"]);
        orders.Rows.Add(["2", "C001", "ORD-ADA"]);

        var model = DataModelBuilder.Build([customers, orders]);
        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.Equal(["Customer"], ForeignKeys.TargetsOf(Class(classes, "Order"), ForeignKeys.ClassNameSet(classes)));
        Assert.Contains(Class(classes, "Customer").Properties, p => p.Name == "CustomerCode");

        var data = TabularImport.ToImportedData([customers, orders]);
        Assert.Equal(["2", "1"], data.For("Order").Select(r => r["CustomerId"]));
        Assert.Equal(["C001", "C002"], data.For("Customer").Select(r => r["CustomerCode"]));
    }

    /// <summary>A GUID key is kept, not dropped.</summary>
    [Fact]
    public void A_guid_key_is_kept_in_a_field()
    {
        var table = new TabularTable { Name = "Devices", SourceLabel = "devices.csv" };
        table.Columns.Add(new TabularColumn { Name = "Id" });
        table.Columns.Add(new TabularColumn { Name = "Label" });
        table.Rows.Add(["5f2b4e1c-0c3a-4d1e-9a51-6f7f3d2b8e10", "Hub"]);

        var data = TabularImport.ToImportedData([table]);

        Assert.Equal("5f2b4e1c-0c3a-4d1e-9a51-6f7f3d2b8e10", data.For("Device")[0]["DeviceCode"]);
    }

    /// <summary>A text column that names no row of the table it is named for is the user's own
    /// data, and is left exactly as it was read.</summary>
    [Fact]
    public void A_text_column_naming_no_row_is_left_as_it_was_read()
    {
        var customers = new TabularTable { Name = "Customers" };
        customers.Columns.Add(new TabularColumn { Name = "CustomerId" });
        customers.Rows.Add(["ALFKI"]);

        var orders = new TabularTable { Name = "Orders" };
        orders.Columns.Add(new TabularColumn { Name = "CustomerId" });
        orders.Columns.Add(new TabularColumn { Name = "Ref" });
        orders.Rows.Add(["WALK-IN", "ORD-1"]);

        var model = DataModelBuilder.Build([customers, orders]);
        var order = model.Entities.Single(e => e.Name == "Order");

        Assert.Equal("string", order.Fields.Single(f => f.Name == "CustomerId").Type);
        Assert.Empty(order.Relationships);
        Assert.Equal("WALK-IN", TabularImport.ToImportedData([customers, orders]).For("Order")[0]["CustomerId"]);
    }

    // --- K2: keys to a key that is not <Class>Id ---

    [Fact]
    public void A_foreign_key_to_a_key_with_its_own_name_is_linked_and_renumbered()
    {
        var app = Generate();
        var names = ForeignKeys.ClassNameSet(app.Classes);

        // Ticket keeps its numbers as data, and is numbered 1, 2 by its Id.
        Assert.Equal([7001, 7002], app.Seeds["Ticket"].Select(r => r.GetProperty("TicketNo").GetInt32()));

        Assert.Contains(ForeignKeys.LinksOf(Class(app, "Payment"), names), l => l.Property == "TicketId" && l.Target == "Ticket");
        Assert.Contains(ForeignKeys.LinksOf(Class(app, "InvoiceLine"), names), l => l.Property == "ProductId" && l.Target == "Product");
        Assert.Contains(ForeignKeys.LinksOf(Class(app, "Staff"), names), l => l.Property == "RegionId" && l.Target == "Region");

        Assert.Equal("7002", Followed(app, "Payment", "Ref", "TicketId", "Ticket", "TicketNo")["PAY-1"]);
        Assert.Equal("BOLT", Followed(app, "InvoiceLine", "Qty", "ProductId", "Product", "Sku")["4"]);
        Assert.Equal("N", Followed(app, "Staff", "Name", "RegionId", "Region", "RegionCode")["Bob"]);

        Assert.Contains(ForeignKeys.ReferencesTo("Ticket", app.Classes), r => r == ("Payment", "TicketId"));
        Assert.DoesNotContain(app.Data.Notes, n => n.Contains("could not be linked"));
    }

    // --- K3: a reference to the row's own table ---

    [Fact]
    public void A_reference_to_its_own_table_is_kept_and_renumbered()
    {
        var app = Generate();

        var managerOf = Followed(app, "Staff", "Name", "ManagerId", "Staff", "Name");
        Assert.Null(managerOf["Ann"]);
        Assert.Equal("Ann", managerOf["Bob"]);
        Assert.Equal("Bob", managerOf["Cy"]);

        var staff = Class(app, "Staff");
        Assert.Equal("Staff", Property(staff, "Manager").Type);
        Assert.Contains(ForeignKeys.LinksOf(staff, ForeignKeys.ClassNameSet(app.Classes)),
            l => l is { Property: "ManagerId", Navigation: "Manager", Target: "Staff" });
        Assert.Contains(ForeignKeys.ReferencesTo("Staff", app.Classes), r => r == ("Staff", "ManagerId"));
    }

    /// <summary>A self-reference whose column does not end in Id is named so it reads as a link,
    /// not as the table's own key.</summary>
    [Fact]
    public void A_self_reference_not_named_as_a_key_becomes_one()
    {
        var employees = new TabularTable { Name = "Employee" };
        employees.Columns.Add(new TabularColumn { Name = "EmployeeID", Type = "int", IsKey = true });
        employees.Columns.Add(new TabularColumn { Name = "ReportsTo", Type = "int" });
        employees.ForeignKeys.Add(new TabularForeignKey("ReportsTo", "Employee", "EmployeeID"));
        employees.Rows.Add(["7", null]);
        employees.Rows.Add(["9", "7"]);

        var model = DataModelBuilder.Build([employees]);

        var entity = model.Entities.Single();
        Assert.Contains(entity.Relationships, r => r.Target == "Employee" && r.ForeignKey == "ReportsToId");
        Assert.Equal("1", TabularImport.ToImportedData([employees]).For("Employee")[1]["ReportsToId"]);
    }

    // --- K4: two keys to one table ---

    [Fact]
    public void Two_keys_to_the_same_table_are_two_links_each_with_its_own_navigation()
    {
        var app = Generate();

        var payment = app.Model.Entities.Single(e => e.Name == "Payment");
        Assert.Contains(payment.Relationships, r => r.Target == "Staff" && r.ForeignKey == "PaidById");
        Assert.Contains(payment.Relationships, r => r.Target == "Staff" && r.ForeignKey == "ApprovedById");

        var definition = Class(app, "Payment");
        Assert.Equal("Staff", Property(definition, "PaidBy").Type);
        Assert.Equal("Staff", Property(definition, "ApprovedBy").Type);
        Assert.DoesNotContain(definition.Properties, p => p.Name == "Staff");

        Assert.Equal("Bob", Followed(app, "Payment", "Ref", "PaidById", "Staff", "Name")["PAY-1"]);
        Assert.Equal("Ann", Followed(app, "Payment", "Ref", "ApprovedById", "Staff", "Name")["PAY-1"]);
    }

    // --- The generated code: compiles, checks every key, refuses the deletes ---

    /// <summary>
    /// The models and validators generated from this database compile, and the validators treat
    /// every link as one: a NOT NULL key may not be 0, a nullable one may, an unknown key is
    /// refused, and a member of staff others name as manager or payer cannot be deleted.
    /// </summary>
    [Fact]
    public void The_generated_models_and_validators_compile_and_check_every_link()
    {
        var app = Generate();
        var names = ForeignKeys.ClassNameSet(app.Classes);

        var files = new List<(string Path, string Code)>
        {
            ("GlobalUsings.cs", "global using System;\nglobal using System.Collections.Generic;"),
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", [], login: false)),
            ("Validation/StoreEntityLookup.cs", Lookup),
        };

        foreach (var definition in app.Classes)
        {
            files.Add(($"Models/{definition.Name}.cs", RoslynClassGenerator.Generate(definition, "Acme.Models")));
            files.Add(($"Validation/{definition.Name}Validator.Generated.cs",
                ValidatorTemplate.GeneratedForLinks("Acme", definition, ForeignKeys.LinksOf(definition, names),
                    referencesRequired: false,
                    referencedBy: ForeignKeys.ReferencesTo(definition.Name, app.Classes))));
            files.Add(($"Validation/{definition.Name}Validator.Custom.cs",
                ValidatorTemplate.CustomStub("Acme", definition.Name)));
        }

        var result = Compiler.CompileSources(files);
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));

        var assembly = Assembly.Load(result.AssemblyBytes!);

        IReadOnlyList<string> Validate(string className, string operation, params (string, object)[] values)
        {
            var type = assembly.GetType("Acme.Models." + className)!;
            var item = Activator.CreateInstance(type)!;
            foreach (var (property, value) in values)
                type.GetProperty(property)!.SetValue(item, value);

            var validatorType = assembly.GetType($"Acme.Validation.{className}Validator")!;
            var context = assembly.GetType("Acme.Validation.ValidationContext`1")!.MakeGenericType(type)
                .GetMethod("For")!
                .Invoke(null, [item, Enum.Parse(assembly.GetType("Acme.Validation.ValidationOperation")!, operation),
                    Activator.CreateInstance(assembly.GetType("Acme.Validation.StoreEntityLookup")!)]);
            var outcome = validatorType.GetMethod("Validate")!.Invoke(Activator.CreateInstance(validatorType), [context])!;
            return (IReadOnlyList<string>)outcome.GetType().GetProperty("Messages")!.GetValue(outcome)!;
        }

        // 99 is an Id no lookup knows (the stub knows 1..9).
        var unknown = Validate("Payment", "Insert",
            ("Amount", 1m), ("Ref", "PAY-9"), ("CurrencyId", 1), ("PaidById", 1), ("TicketId", 1), ("ApprovedById", 99));
        Assert.Contains(unknown, m => m.Contains("ApprovedById does not refer to a known Staff."));

        var delete = Validate("Staff", "Delete", ("Id", 1), ("Name", "Ann"), ("RegionId", 1));
        Assert.Contains(delete, m => m.Contains("Staff record(s) refer to it"));
        Assert.Contains(delete, m => m.Contains("Payment record(s) refer to it"));
    }

    /// <summary>Ids 1..9 exist; every record is referenced once.</summary>
    private const string Lookup = """
        namespace Acme.Validation;

        public sealed class StoreEntityLookup : IEntityLookup
        {
            public bool Exists<TEntity>(int id) where TEntity : class, new() => id is >= 1 and <= 9;
            public int CountReferencing<TEntity>(string foreignKeyProperty, int id) where TEntity : class, new() => 1;
            public bool AnyOtherWith<TEntity>(string propertyName, object? value, int exceptId) where TEntity : class, new() => false;
        }
        """;

    private static ClassDefinition Class(IReadOnlyList<ClassDefinition> classes, string name)
        => classes.Single(c => c.Name == name);
}
