namespace DomainDiscovery.Wikipedia;

/// <summary>
/// A tiny file cache keyed by an arbitrary string. Makes repeated Wikipedia calls
/// (and re-runs) offline and deterministic.
/// </summary>
public sealed class WikipediaCache
{
    private readonly string _directory;

    public WikipediaCache(string directory) => _directory = directory;

    public string KeyToPath(string key)
    {
        var safe = new string(key.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        if (safe.Length > 120)
            safe = safe[..120];
        return Path.Combine(_directory, safe + ".cache");
    }

    public bool TryGet(string key, out string value)
    {
        var path = KeyToPath(key);
        if (File.Exists(path))
        {
            try
            {
                value = File.ReadAllText(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // unreadable/locked cache entry — treat as a miss, never throw
            }
        }

        value = string.Empty;
        return false;
    }

    public void Put(string key, string value)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(KeyToPath(key), value);
    }
}
