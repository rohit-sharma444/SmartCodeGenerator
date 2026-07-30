using DataIngestion;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

public class DelimitedTextReaderTests
{
    [Theory]
    [InlineData('\t', "Id\tName\n1\tAda\n2\tGrace\n")]
    [InlineData('|', "Id|Name\n1|Ada\n2|Grace\n")]
    [InlineData(';', "Id;Name\n1;Ada\n2;Grace\n")]
    [InlineData(',', "Id,Name\n1,Ada\n2,Grace\n")]
    public void Sniffs_TheDelimiterThatIsConsistentAcrossLines(char expected, string text)
        => Assert.Equal(expected, DelimitedTextReader.SniffDelimiter(text));

    /// <summary>
    /// Prose contains commas and full stops but no consistent field count. Guessing a delimiter
    /// here would produce a one-column table of sentence fragments and call it a domain model.
    /// </summary>
    [Fact]
    public void Prose_SniffsNoDelimiter()
    {
        var prose = "We lend books to members, and we track loans.\n"
                  + "Every loan has a due date.\nMembers can be suspended.\n";

        Assert.Null(DelimitedTextReader.SniffDelimiter(prose));
    }

    [Fact]
    public void InconsistentFieldCounts_SniffNoDelimiter()
        => Assert.Null(DelimitedTextReader.SniffDelimiter("a,b\nc,d,e\nf\n"));

    /// <summary>Tab wins over a comma that also appears, because tab is unambiguous in text
    /// where a comma may be punctuation inside a field.</summary>
    [Fact]
    public void TabWinsOverComma_WhenBothAreConsistent()
        => Assert.Equal('\t',
            DelimitedTextReader.SniffDelimiter("Name\tCity\nWills, Ada\tLondon\nHopper, G\tNY\n"));

    [Fact]
    public void ReadsATabSeparatedFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
        File.WriteAllText(path, "Ref\tQty\nORD-1\t2\nORD-2\t3\n");

        try
        {
            var table = Assert.Single(
                new DelimitedTextReader().Read(new DataSourceSpec(DataSourceKind.Txt, path)));

            Assert.Equal(new[] { "Ref", "Qty" }, table.Columns.Select(c => c.Name));
            Assert.Equal("int", table.Columns[1].Type);
            Assert.Equal("ORD-2", table.Value(1, "Ref"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NonTabularFile_IsRejectedWithAMessageThatSaysWhatToDo()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
        File.WriteAllText(path, "We lend books to members.\nEvery loan has a due date.\n");

        try
        {
            var error = Assert.Throws<InvalidDataException>(
                () => new DelimitedTextReader().Read(new DataSourceSpec(DataSourceKind.Txt, path)));

            Assert.Contains("could not find a delimiter", error.Message);
            Assert.Contains("build app:", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
