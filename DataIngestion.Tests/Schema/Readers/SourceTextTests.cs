using System.Text;
using DataIngestion;
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

public class SourceTextTests
{
    /// <summary>"Zürich" as Windows-1252 writes it: the ü is the single byte 0xFC, which is
    /// not valid UTF-8 on its own.</summary>
    private static readonly byte[] Cp1252Zurich =
        [.. "Code,City\r\nA1,Z"u8, 0xFC, .. "rich\r\nA2,"u8, 0x80, .. "5\r\n"u8];

    [Fact]
    public void Utf8WithoutBom_IsReadAsUtf8_WithNoNote()
    {
        var text = SourceText.Decode(Encoding.UTF8.GetBytes("City\nZürich\n"), "a.csv", out var note);

        Assert.Equal("City\nZürich\n", text);
        Assert.Null(note);
    }

    [Fact]
    public void Utf8Bom_IsRemoved()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("Id\n1\n")).ToArray();

        var text = SourceText.Decode(bytes, "a.csv", out var note);

        Assert.Equal("Id\n1\n", text);
        Assert.Null(note);
    }

    [Fact]
    public void Utf16LittleEndianBom_IsReadAsUtf16()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("City\nZürich\n")).ToArray();

        var text = SourceText.Decode(bytes, "a.csv", out var note);

        Assert.Equal("City\nZürich\n", text);
        Assert.Null(note);
    }

    [Fact]
    public void Cp1252Bytes_AreReadAsWindows1252_AndSaidSo()
    {
        var text = SourceText.Decode(Cp1252Zurich, "latin.csv", out var note);

        Assert.Contains("Zürich", text);
        Assert.Contains("€5", text);              // 0x80 is the euro sign in 1252, not in Latin-1
        Assert.DoesNotContain("�", text);
        Assert.NotNull(note);
        Assert.Contains("latin.csv", note);
        Assert.Contains("Windows-1252", note);
    }

    [Fact]
    public void CsvReader_KeepsALatin1City()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sff-latin-{Guid.NewGuid():N}.csv");
        File.WriteAllBytes(path, Cp1252Zurich);

        try
        {
            var table = new CsvReader().Read(new DataSourceSpec(DataSourceKind.Csv, path))[0];

            Assert.Equal("Zürich", table.Value(0, "City"));
            Assert.Contains(table.Notes, n => n.Contains("Windows-1252"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DelimitedTextReader_KeepsALatin1City()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sff-latin-{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(path, Cp1252Zurich);

        try
        {
            var table = new DelimitedTextReader().Read(new DataSourceSpec(DataSourceKind.Txt, path))[0];

            Assert.Equal("Zürich", table.Value(0, "City"));
            Assert.Contains(table.Notes, n => n.Contains("Windows-1252"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
