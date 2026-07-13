using System.Diagnostics;
using DomainDiscovery.Wikipedia.Dump;
using WikipediaDumpImporter;

var dumpPath = args.Length > 0
    ? args[0]
    : @"D:\SmartCodeGenerator-Repository\Dump\enwiki-latest-pages-articles.xml.bz2";

var databasePath = args.Length > 1
    ? args[1]
    : @"D:\SmartCodeGenerator-Repository\Dump\enwiki.db";

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
