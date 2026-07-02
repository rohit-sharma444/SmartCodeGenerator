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

var engineObj = EngineBuilder.Build(options);
var engine = engineObj is CachedSemanticAnalyzer cached
    ? cached
    : engineObj as ISemanticAnalyzer ?? throw new InvalidOperationException("Failed to build engine");

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
if (options.EnableLogging)
{
    Console.WriteLine("\n" + new string('=', 60));
    Console.WriteLine("PERFORMANCE SUMMARY");
    Console.WriteLine(new string('=', 60));
    
    // Note: In a real scenario, you'd extract the logger from the pipeline
    // For now, this is a placeholder for future enhancement
    Console.WriteLine("(Logging enabled - detailed metrics available via PipelineLogger)");
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
        return typed.ToArray();

    return new[]
    {
        "Sarah booked a flight to London tomorrow.",
        "The big red car is very fast.",
        "Send the report to John on Monday.",
        "I want to buy a new computer.",
        "Email john@example.com about the meeting at 14:30 on 2025-06-30.",
        "Visit https://example.com for details.",
    };
}
