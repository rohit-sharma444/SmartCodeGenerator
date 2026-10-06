using System.Text.Json;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using DataIngestion;
using DataIngestion.Schema;

namespace CodeGenerator.Tests.App;

/// <summary>
/// An empty CSV cell, a JSON null or a SQL NULL in a number, flag or date column used to become
/// 0, False or 01-Jan-0001 in the generated app, with nothing reported: every model property was
/// non-nullable. A column whose rows hold a missing value is now a NULLABLE property, the seed
/// leaves the value out (which loads as null), and the generated model and validator compile and
/// accept their own rows.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MissingValuesStayMissingTests
{
    [TestInitialize]
    public void Setup() => ValidationRegistry.Clear();

    [TestCleanup]
    public void Cleanup() => ValidationRegistry.Clear();

    private static TabularTable Orders()
        => SeedRowsPassTheirOwnRulesTests.Table(
            "Orders",
            ["Ref", "Qty", "Amount", "Shipped", "OrderDate", "ShipDate"],
            ["O-1", "2", "10.50", "true", "2026-01-15", "2026-01-20"],
            ["O-2", "3", "", "", "2026-02-01", ""],
            ["O-3", "1", "7.25", "false", "2026-03-01", null]);

    [TestMethod]
    public void A_column_with_a_missing_value_is_a_nullable_property_and_a_full_one_is_not()
    {
        var classes = DomainModelMapper.ToClassDefinitions(DataModelBuilder.Build([Orders()], []));
        var order = classes.Single(c => c.Name == "Order");

        string TypeOf(string name) => order.Properties.Single(p => p.Name == name).Type;

        Assert.AreEqual("int", TypeOf("Qty"));
        Assert.AreEqual("decimal?", TypeOf("Amount"));
        Assert.AreEqual("bool?", TypeOf("Shipped"));
        Assert.AreEqual("DateTime", TypeOf("OrderDate"));
        Assert.AreEqual("DateTime?", TypeOf("ShipDate"));
        Assert.AreEqual("string", TypeOf("Ref"));

        // A missing value is not a reason to require one: nothing on a nullable is required.
        Assert.IsFalse(order.Properties.Single(p => p.Name == "Amount").Rules
            .Any(r => string.Equals(r.Name, "required", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void The_seed_leaves_a_missing_value_out_so_it_loads_as_null()
    {
        var table = Orders();
        var model = DataModelBuilder.Build([table], []);
        var classes = DomainModelMapper.ToClassDefinitions(model, out var classNames);

        var rows = JsonDocument.Parse(SeedFileWriter.Files(
                classes, classNames, TabularImport.ToImportedData([table]), out var unreadable)
            .Single().Json).RootElement.EnumerateArray().ToArray();

        Assert.IsEmpty(unreadable);
        Assert.IsFalse(rows[1].TryGetProperty("Amount", out _));
        Assert.IsFalse(rows[1].TryGetProperty("Shipped", out _));
        Assert.IsFalse(rows[1].TryGetProperty("ShipDate", out _));
        Assert.AreEqual(10.50m, rows[0].GetProperty("Amount").GetDecimal());
    }

    /// <summary>
    /// The nullable model, its validator (with every proposal the scan made accepted) and the seed
    /// rows compile together and the rows pass: a null is neither out of range nor a duplicate.
    /// </summary>
    [TestMethod]
    public void The_nullable_model_and_its_validator_compile_and_accept_their_own_rows()
    {
        var errors = SeedRowsPassTheirOwnRulesTests.ErrorsFromTheSeedRows(Orders(), "Order");

        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    /// <summary>
    /// The SQL path: declared scale and date facts put a scale rule and a date-only rule on the
    /// property, and both must compile against a nullable one.
    /// </summary>
    [TestMethod]
    public void Declared_rules_on_a_nullable_sql_column_compile_and_pass_a_null()
    {
        var table = new TabularTable { Name = "Payment" };
        table.Columns.Add(new TabularColumn { Name = "Ref", Type = "string" });
        table.Columns.Add(new TabularColumn
        {
            Name = "Amount", Type = "decimal",
            Facts = new TypeFacts { IsNullable = true, Precision = 10, Scale = 2, DeclaredType = "decimal" }
        });
        table.Columns.Add(new TabularColumn
        {
            Name = "PaidOn", Type = "DateTime",
            Facts = new TypeFacts { IsNullable = true, DeclaredType = "date" }
        });
        table.Columns.Add(new TabularColumn
        {
            Name = "Tries", Type = "int",
            Facts = new TypeFacts { IsNullable = true, DeclaredType = "tinyint" }
        });
        table.Rows.Add(["P-1", "12.50", "2026-01-15", "3"]);
        table.Rows.Add(["P-2", null, null, null]);

        var classes = DomainModelMapper.ToClassDefinitions(DataModelBuilder.Build([table], []));
        var payment = classes.Single(c => c.Name == "Payment");
        Assert.AreEqual("decimal?", payment.Properties.Single(p => p.Name == "Amount").Type);
        Assert.AreEqual("DateTime?", payment.Properties.Single(p => p.Name == "PaidOn").Type);
        Assert.AreEqual("int?", payment.Properties.Single(p => p.Name == "Tries").Type);

        var errors = SeedRowsPassTheirOwnRulesTests.ErrorsFromTheSeedRows(table, "Payment");

        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    /// <summary>
    /// A value a nullable column cannot hold is reported by its kind ("int"), not by the C#
    /// spelling the user never wrote ("int?").
    /// </summary>
    [TestMethod]
    public void An_unreadable_value_in_a_nullable_column_is_reported_by_its_kind()
    {
        var table = new TabularTable { Name = "Customer" };
        table.Columns.Add(new TabularColumn { Name = "Name", Type = "string" });
        table.Columns.Add(new TabularColumn
        {
            Name = "BigNo", Type = "int",
            Facts = new TypeFacts { IsNullable = true, DeclaredType = "bigint" }
        });
        table.Rows.Add(["Ann", "9000000000"]);
        table.Rows.Add(["Bob", null]);

        var model = DataModelBuilder.Build([table], []);
        var classes = DomainModelMapper.ToClassDefinitions(model, out var classNames);
        SeedFileWriter.Files(classes, classNames, TabularImport.ToImportedData([table]), out var unreadable);

        var line = unreadable.Single();
        StringAssert.Contains(line, "could not be read as int and");
    }

    /// <summary>
    /// A blank foreign key already means "no parent" at 0, and every relationship consumer matches
    /// an int key, so it stays an int.
    /// </summary>
    [TestMethod]
    public void A_foreign_key_with_a_missing_value_stays_an_int()
    {
        var customers = SeedRowsPassTheirOwnRulesTests.Table(
            "Customer", ["Name"], ["Ann"], ["Bob"]);
        var orders = SeedRowsPassTheirOwnRulesTests.Table(
            "Orders", ["Ref", "CustomerId"], ["O-1", "1"], ["O-2", ""]);

        var classes = DomainModelMapper.ToClassDefinitions(DataModelBuilder.Build([customers, orders], []));

        Assert.AreEqual("int", classes.Single(c => c.Name == "Order")
            .Properties.Single(p => p.Name == "CustomerId").Type);
    }
}
