namespace DocumentIndexer;

using System.Collections.Concurrent;

public sealed class DocumentIndexService
{
    private readonly ConcurrentDictionary<string, IndexedDocument>
        _documents = new();

    public void UpsertDocument(
        string filePath,
        string fileType,
        DateTime lastModifiedUtc,
        string content)
    {
        var document = new IndexedDocument
        {
            FilePath = filePath,
            FileType = fileType,
            LastModifiedUtc = lastModifiedUtc,
            Content = content
        };

        _documents.AddOrUpdate(
            filePath,
            document,
            (_, _) => document);
    }

    public void DeleteDocument(string filePath)
    {
        _documents.TryRemove(filePath, out _);
    }

    public List<SearchResult> Search(string searchText)
    {
        var results = new List<SearchResult>();

        foreach (var document in _documents.Values)
        {
            if (document.Content.Contains(
                searchText,
                StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new SearchResult
                {
                    FilePath = document.FilePath,
                    FileType = document.FileType,
                    MatchPreview =
                        ExtractPreview(
                            document.Content,
                            searchText)
                });
            }
        }

        return results;
    }

    public int TotalDocuments()
        => _documents.Count;

    private static string ExtractPreview(
        string text,
        string searchText,
        int radius = 50)
    {
        int index = text.IndexOf(
            searchText,
            StringComparison.OrdinalIgnoreCase);

        if (index < 0)
            return "";

        int start = Math.Max(0, index - radius);

        int length = Math.Min(
            text.Length - start,
            radius * 2 + searchText.Length);

        return text.Substring(start, length)
            .Replace(Environment.NewLine, " ");
    }
}