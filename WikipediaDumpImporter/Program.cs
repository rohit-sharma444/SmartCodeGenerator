using System.Diagnostics;
using DomainDiscovery.Wikipedia.Dump;
using WikipediaDumpImporter;

// The import builds the index at the end, so a run that is KILLED leaves an unindexed store, in
// which every read is a full table scan. This recovers it without decoding 25 GB of bz2 again.
var indexOnly = args.Contains("--index-only", StringComparer.OrdinalIgnoreCase);
var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

var dumpPath = positional.Length > 0
    ? positional[0]
    : @"D:\SmartCodeGenerator-Repository\Dump\enwiki-latest-pages-articles.xml.bz2";

var databasePath = positional.Length > 1
    ? positional[1]
    : @"D:\SmartCodeGenerator-Repository\Dump\enwiki.db";

if (indexOnly)
{
    Console.WriteLine($"Store: {databasePath}");
    Console.WriteLine("Building the search index over the store as it stands...");

    var clockOnly = Stopwatch.StartNew();
    var storeOnly = new WikipediaStore(databasePath);
    storeOnly.CreateSchema();

    // Drop first, so this REBUILDS. CreateSearchIndexes is IF NOT EXISTS, so without this an index
    // left by an older version of the schema would simply be kept, and the fix would silently
    // not apply.
    storeOnly.DropSearchIndexes();
    storeOnly.CreateSearchIndexes();
    clockOnly.Stop();

    Console.WriteLine($"Indexed {storeOnly.ArticleCount:N0} articles in {clockOnly.Elapsed:hh\\:mm\\:ss}.");
    return 0;
}

if (!File.Exists(dumpPath))
{
    Console.Error.WriteLine($"Dump not found: {dumpPath}");
    return 1;
}

Console.WriteLine($"Dump:  {dumpPath}");
Console.WriteLine($"Store: {databasePath}");
Console.WriteLine();

// Measured, not estimated. The design doc predicted 1.5-2 hours on the assumption that bz2 decode
// (23.6 MB/s, and that was NATIVE bzip2 -- SharpZipLib manages ~16) was the ceiling. It is not: the
// cost is per-page stripping, which is why the strip is spread across cores.
Console.WriteLine("This takes hours, not the 1.5-2 the design doc predicted: the ceiling is");
Console.WriteLine("per-page stripping, not bz2 decode. The run is resumable -- after a crash or a");
Console.WriteLine("deliberate stop it continues from the last committed page id.");
Console.WriteLine();
Console.WriteLine("The search index is built at the END. If this run is killed, the store is left");
Console.WriteLine("unindexed and every read is a table scan: re-run with --index-only to fix that");
Console.WriteLine("without decoding the dump again.");
Console.WriteLine();

var store = new WikipediaStore(databasePath);
store.CreateSchema();

if (store.GetLastPageId() > 0)
    Console.WriteLine($"Resuming after page id {store.GetLastPageId():N0}.");

var clock = Stopwatch.StartNew();

var stats = new ImportRunner(store, message =>
        Console.WriteLine($"[{clock.Elapsed:hh\\:mm\\:ss}] {message}"))
    .Run(DumpPageReader.ReadBz2(dumpPath));

clock.Stop();

var sizeGb = new FileInfo(databasePath).Length / 1024d / 1024d / 1024d;

Console.WriteLine();
Console.WriteLine($"Done in {clock.Elapsed:hh\\:mm\\:ss}.");
Console.WriteLine($"  articles:  {stats.Articles:N0}");
Console.WriteLine($"  redirects: {stats.Redirects:N0}");
Console.WriteLine($"  skipped:   {stats.Skipped:N0}");
Console.WriteLine($"  store:     {sizeGb:F1} GB");

return 0;
