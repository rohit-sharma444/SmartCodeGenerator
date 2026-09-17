using DataIngestion.Schema;
using Xunit;

namespace DataIngestion.Tests;

public class ColumnScanTests
{
    [Fact]
    public void A_column_of_repeating_values_reports_them_all()
    {
        var table = TableOf("Status", "Draft", "Posted", "Draft", "Cancelled", "Posted");

        var scan = ColumnScan.Of(table, "Status");

        Assert.Equal(
            new[] { "Draft", "Posted", "Cancelled" }.Order(), scan.DistinctValues.ToArray().Order());
        Assert.False(scan.ExceededDistinctCap);
        Assert.Equal(9, scan.LongestValueLength);      // "Cancelled"
        Assert.Equal(2, scan.DuplicateValueCount);     // Draft and Posted each occur twice
        Assert.Equal(5, scan.NonBlankRowCount);
    }

    [Fact]
    public void The_distinct_list_stops_at_the_cap()
    {
        var values = Enumerable.Range(1, 200).Select(i => $"v{i}").ToArray();
        var table = TableOf("Reference", values);

        var scan = ColumnScan.Of(table, "Reference");

        // 51 is how "more than 50" is answered without holding a million strings for a column that
        // was never going to be a status.
        Assert.Equal(ColumnScan.DistinctCap, scan.DistinctValues.Count);
        Assert.True(scan.ExceededDistinctCap);
    }

    [Fact]
    public void A_repeat_past_the_cap_is_still_counted()
    {
        var values = Enumerable.Range(1, 200).Select(i => $"v{i}").ToList();
        values.Add("v60");

        var scan = ColumnScan.Of(TableOf("Reference", [.. values]), "Reference");

        // A column of a million references is exactly where uniqueness is most wanted, and it is
        // always past the cap. Counting repeats only among the first 51 values would propose
        // uniqueness for a column whose 60th value is duplicated — a rule the generated app then
        // refuses the source's own rows against. So the repeat count is tracked over EVERY value,
        // and only the distinct LIST is capped.
        Assert.True(scan.ExceededDistinctCap);
        Assert.Equal(1, scan.DuplicateValueCount);
    }

    [Fact]
    public void A_fifth_value_at_row_fifty_thousand_is_still_found()
    {
        var values = new List<string>();
        for (var i = 0; i < 50_000; i++)
            values.Add(i % 2 == 0 ? "Draft" : "Posted");
        values.Add("Reversed");

        var scan = ColumnScan.Of(TableOf("Status", [.. values]), "Status");

        // The whole reason the scan reads the complete table. A 200-value sample, or the 1000-row
        // ImportedData cap, would have produced a whitelist that rejects the source's own data.
        Assert.Contains("Reversed", scan.DistinctValues.ToArray());
        Assert.Equal(50_001, scan.NonBlankRowCount);
    }

    [Fact]
    public void Blank_cells_count_as_rows_but_not_as_values()
    {
        var table = TableOf("Status", "Draft", null, "   ", "Posted");

        var scan = ColumnScan.Of(table, "Status");

        // Whether a column may be blank is the Field stage's question, answered from the declared
        // nullability. A blank is not a status value.
        Assert.Equal(2, scan.DistinctValues.Count);
        Assert.Equal(2, scan.NonBlankRowCount);
        Assert.Equal(4, scan.RowCount);
    }

    [Fact]
    public void A_column_that_is_not_there_scans_nothing()
    {
        var scan = ColumnScan.Of(TableOf("Status", "Draft"), "NoSuchColumn");

        Assert.Empty(scan.DistinctValues);
        Assert.Equal(0, scan.RowCount);
    }

    private static TabularTable TableOf(string column, params string?[] values)
    {
        var table = new TabularTable { Name = "T" };
        table.Columns.Add(new TabularColumn { Name = column });

        foreach (var value in values)
            table.Rows.Add([value]);

        return table;
    }
}
