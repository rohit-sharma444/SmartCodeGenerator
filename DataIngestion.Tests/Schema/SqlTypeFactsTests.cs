using CodeGenerationEngine.Models;
using DataIngestion.Schema;
using DataIngestion.Schema.Readers.Sql;
using Xunit;

namespace DataIngestion.Tests.Schema;

/// <summary>A schema source that answers from memory, so these tests need no database.</summary>
internal sealed class FakeSchemaSource : ISqlSchemaSource
{
    public required IReadOnlyList<SqlColumnInfo> ColumnList { get; init; }

    public IReadOnlyList<SqlColumnInfo> Columns(string connectionString, string schema, string table)
        => ColumnList;

    public IReadOnlyList<SqlTableInfo> Tables(string connectionString) =>
        [new SqlTableInfo("dbo", "Customer", ColumnList.Count, null)];

    public IReadOnlyList<TabularForeignKey> ForeignKeys(string connectionString) => [];

    public IReadOnlyList<string?[]> Rows(string connectionString, string schema, string table) => [];
}

public class SqlTypeFactsTests
{
    [Fact]
    public void NvarcharFifty_CarriesItsRealLength()
    {
        // The defect this whole phase exists to fix: today every string is bounded at 200,
        // so a generated app accepts 120 characters into a column that holds 50.
        var facts = FactsFor(new SqlColumnInfo("Surname", "nvarchar", Nullable: false, MaxLength: 50));

        Assert.Equal(50, facts.MaxLength);
        Assert.False(facts.IsUnbounded);
        Assert.False(facts.IsNullable!.Value);
    }

    [Fact]
    public void NvarcharMax_IsUnbounded_NotLengthMinusOne()
    {
        // INFORMATION_SCHEMA reports -1 for max. Carried through raw it would emit
        // "must be -1 characters or fewer", which rejects every value.
        var facts = FactsFor(new SqlColumnInfo("Notes", "nvarchar", Nullable: true, MaxLength: -1));

        Assert.True(facts.IsUnbounded);
        Assert.Null(facts.MaxLength);
    }

    [Fact]
    public void TinyintAndSmallint_KeepTheirDeclaredType()
    {
        // SqlTypeMap collapses both to "int". The declared name is the only thing
        // that can produce a 0-255 or a -32768-32767 bound.
        Assert.Equal("tinyint", FactsFor(new SqlColumnInfo("Rating", "tinyint", false)).DeclaredType);
        Assert.Equal("smallint", FactsFor(new SqlColumnInfo("Year", "smallint", false)).DeclaredType);
    }

    [Fact]
    public void Decimal_KeepsPrecisionAndScale()
    {
        var facts = FactsFor(
            new SqlColumnInfo("Price", "decimal", Nullable: false, Precision: 18, Scale: 2));

        Assert.Equal(18, facts.Precision);
        Assert.Equal(2, facts.Scale);
    }

    private static TypeFacts FactsFor(SqlColumnInfo column)
    {
        var reader = new SqlServerReader(new FakeSchemaSource { ColumnList = [column] });
        var table = reader.Read(new DataSourceSpec(DataSourceKind.Sql, "irrelevant"))[0];
        return table.Columns[0].Facts!;
    }
}
