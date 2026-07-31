namespace DataIngestion.Schema.Readers;

public sealed class ExcelNumberFormats
{
    public static ExcelNumberFormats Load(System.IO.Compression.ZipArchive zip) => new();

    public bool IsDate(int styleIndex) => false;

    public int? PadWidth(int styleIndex) => null;
}
