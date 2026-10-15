using DataIngestion.Schema;
using DataIngestion.Schema.Readers;
using DomainDiscovery.Model;
using Xunit;

namespace DataIngestion.Tests;

/// <summary>
/// Two columns that end up with one name used to keep only the first: Notes,Notes read the first
/// Notes' values twice, and e-mail beside Email, or "Order Date" beside OrderDate, dropped the
/// second with no record, so `why Email` said no decision was recorded.
/// </summary>
public class DuplicateColumnTests
{
    private static TabularTable Read(string csv)
        => CsvReader.FromText("customers", "customers.csv", csv, ',');

    [Fact]
    public void AnExactDuplicateHeader_IsNumbered_AndKeepsItsOwnValues()
    {
        var table = Read("Name,Notes,Notes\nAda,first,second\n");

        Assert.Equal(new[] { "Name", "Notes", "Notes2" }, table.Columns.Select(c => c.Name));
        Assert.Equal("first", table.Value(0, "Notes"));
        Assert.Equal("second", table.Value(0, "Notes2"));
        Assert.Equal("Notes", table.Columns[2].DuplicateOf);
        Assert.Contains(table.Notes, n => n.Contains("Notes2"));
    }

    [Fact]
    public void AHeaderDifferingOnlyInCase_IsNumbered()
    {
        var table = Read("Email,email\na@x.com,b@y.org\n");

        Assert.Equal("email2", table.Columns[1].Name);
        Assert.Equal("b@y.org", table.Value(0, "email2"));
    }

    [Fact]
    public void ColumnsThatNormalizeToOneName_AreBothKept_WithTheirOwnValues()
    {
        var table = Read("Name,e-mail,Email,Order Date,OrderDate\nAda,a@x.com,b@y.org,2024-01-01,2025-01-01\n");

        var model = DataModelBuilder.Build([table]);
        var fields = model.Entities[0].Fields.Select(f => f.Name).ToList();

        Assert.Equal(new[] { "Name", "EMail", "Email2", "OrderDate", "OrderDate2" }, fields);

        var row = TabularImport.ToImportedData([table]).For("Customer")[0];
        Assert.Equal("a@x.com", row["EMail"]);
        Assert.Equal("b@y.org", row["Email2"]);
        Assert.Equal("2024-01-01", row["OrderDate"]);
        Assert.Equal("2025-01-01", row["OrderDate2"]);
    }

    [Fact]
    public void ANumberedDuplicate_IsRecordedForWhy_UnderItsSourceName()
    {
        var table = Read("Name,e-mail,Email,Notes,Notes\nAda,a@x.com,b@y.org,n1,n2\n");

        var model = DataModelBuilder.Build([table]);

        var email = Assert.Single(model.Decisions, d => d.Name == "Email");
        Assert.Equal(DecisionReasons.ColumnRenamedOffADuplicate, email.Reason);
        Assert.Contains("Email2", email.Evidence);

        var notes = Assert.Single(model.Decisions, d => d.Name == "Notes");
        Assert.Contains("Notes2", notes.Evidence);
    }
}
