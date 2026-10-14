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
    /// A key belongs to the table that declares it, not to every table with a column of the same
    /// name: Customers has its own CustomerID, and was handed the Orders key as a relationship to
    /// itself. Two tables keyed to different targets by the same column name were each given both.
    /// </summary>
    [Fact]
    public void A_foreign_key_is_carried_only_by_the_table_that_declares_it()
    {
        var source = Northwind();
        source.TableList.Add(new SqlTableInfo("dbo", "Regions", 2, 1));
        source.TableList.Add(new SqlTableInfo("sales", "Regions", 2, 1));
        source.TableList.Add(new SqlTableInfo("dbo", "Stores", 2, 1));
        source.ColumnsByTable["Regions"] = [new SqlColumnInfo("RegionID", "int", false)];
        source.ColumnsByTable["Stores"] = [new SqlColumnInfo("RegionID", "int", false)];
        source.ColumnsByTable["Orders"] =
        [
            new SqlColumnInfo("OrderID", "int", false),
            new SqlColumnInfo("CustomerID", "int", false),
            new SqlColumnInfo("RegionID", "int", false),
        ];
        source.Keys.Add(new TabularForeignKey("CustomerID", "Customers", "CustomerID", "Orders", "dbo", "dbo"));
        source.Keys.Add(new TabularForeignKey("RegionID", "Regions", "RegionID", "Orders", "dbo", "sales"));

        var tables = new SqlServerReader(source).Read(Spec("Customers", "Orders", "dbo.Regions", "Stores"));

        Assert.Empty(tables.Single(t => t.Name == "Customers").ForeignKeys);
        Assert.Empty(tables.Single(t => t.Name == "Stores").ForeignKeys);

        // Its target is sales.Regions, which was not picked; dbo.Regions is another table.
        var orders = tables.Single(t => t.Name == "Orders");
        Assert.Equal(["CustomerID"], orders.ForeignKeys.Select(k => k.Column));
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

    /// <summary>
    /// Reported to the USER, not merely kept: <see cref="SqlServerReader.LossNotes"/> was read by
    /// nothing but the test above, so a bigint key past int's range was lost without a word. The
    /// import notes are what both the command line and the workflow page print, and a
    /// normalization rewrite must not shed the note on its way there.
    /// </summary>
    [Fact]
    public void A_lossy_column_type_reaches_the_import_notes_the_user_is_shown()
    {
        var source = Northwind();
        source.ColumnsByTable["Orders"] =
        [
            new SqlColumnInfo("OrderID", "bigint", false),
            new SqlColumnInfo("Total", "decimal", false),
        ];
        source.RowsByTable["Orders"] = [["10248", "1"]];

        var tables = new SqlServerReader(source).Read(Spec("Orders"));

        var dropTotal = new DataIngestion.Normalization.NormalizationFinding
        {
            Kind = DataIngestion.Normalization.NormalizationFindingKind.DerivedColumn,
            TableName = "Orders",
            SourceLabel = "dbo.Orders",
            Columns = ["Total"],
            Headline = "Derived column: Total",
            Proposal = "drop it",
            Evidence = [],
        };

        foreach (var set in new[] { tables, DataIngestion.Normalization.NormalizationPlan.Apply(tables, [dropTotal]) })
        {
            var notes = TabularImport.ToImportedData(set).Notes;
            Assert.Contains(notes, n => n.Contains("OrderID") && n.Contains("bigint") && n.Contains("dbo.Orders"));
        }
    }

    /// <summary>Two schemas may both hold Orders, so the label carries both parts.</summary>
    [Fact]
    public void A_table_is_labelled_with_its_two_part_name()
    {
        var tables = new SqlServerReader(Northwind()).Read(Spec("Customers"));

        Assert.Equal("dbo.Customers", tables.Single().Label);
    }

    /// <summary>A table the catalogue says holds 47 million rows, of which the fake has five.</summary>
    private static FakeSqlSchemaSource Huge(long? catalogued)
    {
        var source = new FakeSqlSchemaSource();

        source.TableList.Add(new SqlTableInfo("TRADING", "BhavCopy", 1, catalogued));
        source.ColumnsByTable["BhavCopy"] = [new SqlColumnInfo("Symbol", "nvarchar", false)];
        source.RowsByTable["BhavCopy"] = [["A"], ["B"], ["C"], ["D"], ["E"]];

        return source;
    }

    /// <summary>
    /// Issue #131: a 47-million-row table was read into memory whole. The reader stops at its cap
    /// and the table says it is partial, so nothing downstream can claim to have seen every row.
    /// </summary>
    [Fact]
    public void A_table_past_the_cap_is_read_up_to_it_and_marked_partial()
    {
        var source = Huge(catalogued: 47_000_000);

        var table = new SqlServerReader(source, maxRowsPerTable: 3).Read(Spec("BhavCopy")).Single();

        Assert.Equal(3, table.Rows.Count);
        Assert.True(table.IsPartial);
        Assert.Equal(47_000_000, table.SourceRowCount);

        // One past the cap: how the reader KNOWS there was more, whatever the catalogue says.
        Assert.Equal(4, source.LastLimit);
    }

    [Fact]
    public void A_table_within_the_cap_is_whole()
    {
        var table = new SqlServerReader(Northwind(), maxRowsPerTable: 3).Read(Spec("Customers")).Single();

        Assert.Equal(2, table.Rows.Count);
        Assert.False(table.IsPartial);
    }

    /// <summary>
    /// The catalogue's count is an estimate. One smaller than the rows actually found is stale,
    /// and quoting it ("read 3 of 2 rows") would be nonsense -- so it is dropped, not quoted.
    /// </summary>
    [Fact]
    public void A_stale_catalogue_count_is_not_quoted()
    {
        var table = new SqlServerReader(Huge(catalogued: 2), maxRowsPerTable: 3).Read(Spec("BhavCopy")).Single();

        Assert.True(table.IsPartial);
        Assert.Null(table.SourceRowCount);
    }
}
