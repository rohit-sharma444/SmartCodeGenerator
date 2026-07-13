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
// (23.6 MB/s) was the ceiling. It is not: the cost is per-page work, and the writer slows as the
// table grows -- see the note on WriteArticles. Two hours in, this had done ~1.75M of ~24M pages.
Console.WriteLine("This takes many hours, not the 1.5-2 the design doc predicted, and it gets");
Console.WriteLine("slower as the store grows. The run is resumable: re-running after a crash or a");
Console.WriteLine("deliberate stop continues from the last committed page id.");
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
