namespace DocumentIndexer;

public sealed class IndexedDocument
{
    public string FilePath { get; set; } = "";
    public string FileType { get; set; } = "";
    public DateTime LastModifiedUtc { get; set; }
    public string Content { get; set; } = "";
}