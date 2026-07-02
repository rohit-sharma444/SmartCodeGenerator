namespace DocumentIndexer;

public sealed class SearchResult
{
    public string FilePath { get; set; } = "";
    public string FileType { get; set; } = "";
    public string MatchPreview { get; set; } = "";
}