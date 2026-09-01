using DataIngestion.Schema;
using DataIngestion.Schema.Readers.Sql;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers.Sql;

public class SqlServerReaderTests
{
    private const string Connection = "Server=.;Database=Northwind;Trusted_Connection=True";

    private static FakeSqlSchemaSource Northwind()
    {
        var source = new FakeSqlSchemaSource();

        source.TableList.Add(new SqlTableInfo("dbo", "Customers", 2, 2));
        source.TableList.Add(new SqlTableInfo("dbo", "Orders", 2, 1));

        source.ColumnsByTable["Customers"] =
        [
            new SqlColumnInfo("CustomerID", "int", false),
            new SqlColumnInfo("CompanyName", "nvarchar", false),
        ];
        source.ColumnsByTable["Orders"] =
        [
            new SqlColumnInfo("OrderID", "int", false),
            new SqlColumnInfo("OrderDate", "datetime", true),
        ];

        source.RowsByTable["Customers"] = [["1", "Alfreds"], ["2", "Ana"]];
        source.RowsByTable["Orders"] = [["10248", "1996-07-04"]];

        return source;
    }

    private static DataSourceSpec Spec(params string[] tables)
        => new(DataSourceKind.Sql, Connection, "Northwind", tables);

    [Fact]
    public void Reads_only_the_tables_that_were_named()
    {
        var source = Northwind();

        var tables = new SqlServerReader(source).Read(Spec("Customers"));

        Assert.Single(tables);
        Assert.Equal("Customers", tables[0].Name);
        Assert.Equal(["Customers"], source.RowsFetched);
    }

    [Fact]
    public void Takes_the_column_type_from_the_database_rather_than_inferring_it()
    {
        var tables = new SqlServerReader(Northwind()).Read(Spec("Orders"));

        var orders = tables.Single();
        Assert.Equal("int", orders.Columns.Single(c => c.Name == "OrderID").Type);
        Assert.Equal("DateTime", orders.Columns.Single(c => c.Name == "OrderDate").Type);
    }

    [Fact]
    public void Brings_every_row()
    {
        var tables = new SqlServerReader(Northwind()).Read(Spec("Customers"));

        Assert.Equal(2, tables.Single().Rows.Count);
    }

    [Fact]
    public void Carries_the_foreign_keys_that_point_between_the_tables_read()
    {
        var source = Northwind();
        source.Keys.Add(new TabularForeignKey("CustomerID", "Customers", "CustomerID"));
        source.ColumnsByTable["Orders"] =
        [
            new SqlColumnInfo("OrderID", "int", false),
            new SqlColumnInfo("CustomerID", "int", false),
        ];
        source.RowsByTable["Orders"] = [["10248", "1"]];

        var tables = new SqlServerReader(source).Read(Spec("Customers", "Orders"));

        var orders = tables.Single(t => t.Name == "Orders");
        Assert.Contains(orders.ForeignKeys, k => k.TargetTable == "Customers");
    }

    /// <summary>
    /// A key pointing at a table the user did not tick would become a navigation property typed
    /// as a class that is never generated, which does not compile.
    /// </summary>
    [Fact]
    public void A_foreign_key_to_a_table_that_was_not_read_is_not_carried()
    {
        var source = Northwind();
        source.Keys.Add(new TabularForeignKey("CustomerID", "Customers", "CustomerID"));
        source.ColumnsByTable["Orders"] =
        [
            new SqlColumnInfo("OrderID", "int", false),
            new SqlColumnInfo("CustomerID", "int", false),
        ];

        var tables = new SqlServerReader(source).Read(Spec("Orders"));

        Assert.Empty(tables.Single().ForeignKeys);
    }

    [Fact]
    public void A_named_table_that_is_not_there_is_refused_by_name()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => new SqlServerReader(Northwind()).Read(Spec("Suppliers")));

        Assert.Contains("Suppliers", error.Message);
    }

    /// <summary>Naming nothing is what a piped run does, and it can only mean everything.</summary>
    [Fact]
    public void Naming_no_tables_reads_them_all()
    {
        var tables = new SqlServerReader(Northwind())
            .Read(new DataSourceSpec(DataSourceKind.Sql, Connection, "Northwind"));

        Assert.Equal(2, tables.Count);
    }

    [Fact]
    public void A_lossy_column_type_is_reported_rather_than_hidden()
    {
        var source = Northwind();
        source.ColumnsByTable["Orders"] = [new SqlColumnInfo("OrderID", "bigint", false)];
        source.RowsByTable["Orders"] = [["10248"]];

        var reader = new SqlServerReader(source);
        reader.Read(Spec("Orders"));

        Assert.Contains(reader.LossNotes, note => note.Contains("OrderID"));
    }

    /// <summary>Two schemas may both hold Orders, so the label carries both parts.</summary>
    [Fact]
    public void A_table_is_labelled_with_its_two_part_name()
    {
        var tables = new SqlServerReader(Northwind()).Read(Spec("Customers"));

        Assert.Equal("dbo.Customers", tables.Single().Label);
    }
}
