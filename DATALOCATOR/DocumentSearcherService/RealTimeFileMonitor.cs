namespace DocumentIndexer;

public sealed class RealTimeFileMonitor
{
    private readonly FileIndexer _indexer;
    private readonly FileSystemWatcher _watcher;

    public RealTimeFileMonitor(
        string folderPath,
        FileIndexer indexer)
    {
        _indexer = indexer;

        _watcher = new FileSystemWatcher(folderPath)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,

            NotifyFilter =
                NotifyFilters.FileName |
                NotifyFilters.LastWrite |
                NotifyFilters.Size
        };

        _watcher.Created += OnChanged;
        _watcher.Changed += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Deleted += OnDeleted;
    }

    private void OnChanged(
        object sender,
        FileSystemEventArgs e)
    {
        if (!File.Exists(e.FullPath))
            return;

        Task.Delay(500).Wait();

        _indexer.IndexFile(e.FullPath);
    }

    private void OnDeleted(
        object sender,
        FileSystemEventArgs e)
    {
        _indexer.RemoveFile(e.FullPath);
    }

    private void OnRenamed(
        object sender,
        RenamedEventArgs e)
    {
        _indexer.RemoveFile(e.OldFullPath);

        if (File.Exists(e.FullPath))
        {
            _indexer.IndexFile(e.FullPath);
        }
    }
}