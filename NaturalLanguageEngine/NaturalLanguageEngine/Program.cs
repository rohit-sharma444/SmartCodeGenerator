using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Core;
using NaturalLanguageEngine.Core.Prompts;
using NaturalLanguageEngine.Models.Semantics;

// Build engine with logging and caching
var options = new EngineBuilderOptions
{
    EnableLogging = true,
    EnableCaching = true,
    MaxCacheSize = 100
};

var buildResult = EngineBuilder.Build(options);
var engine = buildResult.Analyzer;

var promptStore = new PromptStore();

// Every prompt (sentence) analyzed in this run is appended here as JSON,
// alongside any prompts saved by previous runs.
const string promptLogPath = "prompts.json";

string[] promptsToAnalyze = GetPrompts(args);

foreach (var prompt in promptsToAnalyze)
{
    Console.WriteLine(new string('=', 60));
    Console.WriteLine($"Input: {prompt}");

    SemanticResult result = engine.Analyze(prompt);

    Console.WriteLine($"Intent    : {result.Intent?.Name ?? "(none)"}");
    Console.WriteLine($"Confidence: {result.Confidence:P0}");

    Console.WriteLine("Entities:");
    if (result.Entities.Count == 0)
        Console.WriteLine("  (none)");
    foreach (var e in result.Entities)
        Console.WriteLine($"  - [{e.Type}] \"{e.Text}\" ({(double)e.Confidence:P0})");

    Console.WriteLine("Relationships:");
    if (result.Relationships.Count == 0)
        Console.WriteLine("  (none)");
    foreach (var r in result.Relationships)
        Console.WriteLine($"  - {r.Source.Text} --{r.Type}--> {r.Target.Text}");

    if (result.Diagnostics.Count > 0)
    {
        Console.WriteLine("Diagnostics:");
        foreach (var d in result.Diagnostics)
            Console.WriteLine($"  - [{d.Severity}] {d.Code}: {d.Message}");
    }

    promptStore.Append(promptLogPath, new PromptRecord
    {
        Prompt = prompt,
        Timestamp = DateTimeOffset.UtcNow,
        Result = result
    });

    Console.WriteLine();
}

// Display performance metrics if logging was enabled
if (buildResult.Logger != null)
{
    Console.WriteLine("\n" + new string('=', 60));
    Console.WriteLine("PERFORMANCE SUMMARY");
    Console.WriteLine(new string('=', 60));
    
    var stats = buildResult.Logger.GetStats();
    Console.WriteLine($"Total Pipeline Executions: {stats.TotalExecutions}");
    Console.WriteLine($"Total Elapsed Time:       {stats.TotalElapsedMilliseconds}ms");
    Console.WriteLine($"Average Time Per Run:     {stats.AverageElapsedMilliseconds:F2}ms");
    
    Console.WriteLine("\nPer-Stage Metrics:");
    foreach (var stage in buildResult.Logger.GetLogs().GroupBy(l => l.StageName).OrderBy(g => g.First().Order))
    {
        var stageLogs = stage.ToList();
        var totalStageTime = stageLogs.Sum(l => l.ElapsedMilliseconds);
        var avgStageTime = stageLogs.Average(l => l.ElapsedMilliseconds);
        Console.WriteLine($"  {stage.Key,-30} | Executions: {stageLogs.Count,4} | Total: {totalStageTime,8}ms | Avg: {avgStageTime,8:F2}ms");
    }
    
    Console.WriteLine($"\nDetailed JSON Log:");
    Console.WriteLine(buildResult.Logger.ExportAsJson());
}

Console.WriteLine($"\nSaved {promptsToAnalyze.Length} prompt(s) to {Path.GetFullPath(promptLogPath)}");

// Resolves the sentences to analyze for this run:
//   1. Command-line arguments, one prompt per argument, if any were given.
//   2. Otherwise, sentences typed interactively (one per line, blank line to stop).
//   3. If neither yields anything (e.g. piped-in empty input), fall back to
//      a small set of built-in demo sentences so the program always has
//      something to show.
static string[] GetPrompts(string[] args)
{
    if (args.Length > 0)
        return args;

    Console.WriteLine("Enter a sentence to analyze (blank line to finish):");

    var typed = new List<string>();
    string? line;
    while (!string.IsNullOrWhiteSpace(line = Console.ReadLine()))
        typed.Add(line);

    if (typed.Count > 0)
        return [.. typed];

    // Fallback demo sentences
    return
    [
        "The quick brown fox jumps over the lazy dog.",
        "Machine learning algorithms process data efficiently.",
        "Natural language processing enables computers to understand human speech."
    ];
}
