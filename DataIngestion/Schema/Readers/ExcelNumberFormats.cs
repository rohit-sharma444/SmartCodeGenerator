namespace DataIngestion.Schema.Readers;

public sealed class ExcelNumberFormats
{
    public static ExcelNumberFormats Load(System.IO.Compression.ZipArchive zip) => new();
}
