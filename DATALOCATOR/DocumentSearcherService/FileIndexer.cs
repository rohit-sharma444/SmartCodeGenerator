namespace DocumentIndexer;

public sealed class FileIndexer
{
    private readonly DocumentIndexService _indexService;

    public FileIndexer(DocumentIndexService indexService)
    {
        _indexService = indexService;
    }

    public void IndexFolder(string folderPath)
    {
        var files = Directory.EnumerateFiles(
            folderPath,
            "*.*",
            SearchOption.AllDirectories)
            .Where(IsSupportedFile);

        Parallel.ForEach(files, IndexFile);
    }

    public void IndexFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return;

            string content =
                DocumentParser.ExtractText(filePath);

            if (string.IsNullOrWhiteSpace(content))
                return;

            _indexService.UpsertDocument(
                filePath,
                Path.GetExtension(filePath),
                File.GetLastWriteTimeUtc(filePath),
                content);

            Console.WriteLine($"Indexed: {filePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Failed indexing {filePath}: {ex.Message}");
        }
    }

    public void RemoveFile(string filePath)
    {
        _indexService.DeleteDocument(filePath);

        Console.WriteLine($"Removed: {filePath}");
    }

    private static bool IsSupportedFile(string file)
    {
        string ext =
            Path.GetExtension(file).ToLower();

        return ext is
            ".txt" or
            ".pdf" or
            ".docx" or
            ".xls" or
            ".xlsx";
    }
}