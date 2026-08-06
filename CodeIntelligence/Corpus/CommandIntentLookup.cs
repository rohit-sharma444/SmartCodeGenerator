using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeIntelligence.Corpus;

/// <summary>Maps a help-catalog command name to its intent by asking the real parser,
/// rather than duplicating the recognition rules here — a second, hand-maintained
/// mapping would inevitably drift out of sync with what the parser actually
/// recognizes. Commands the parser does not recognize are skipped by the caller.</summary>
internal static class CommandIntentLookup
{
    internal static IntentType Resolve(string commandName)
    {
        try
        {
            return CommandFront.Parse(commandName, Directory.GetCurrentDirectory()).Intent;
        }
        catch (Exception ex)
        {
            // CommandFront.Parse's call graph (regex guards, the NLE analysis
            // pipeline, the intent mapper) has no single documented set of
            // exception types, so narrowing the catch risks under-catching a
            // real fault. The catch stays broad, but the failure is made visible
            // here instead of silently shrinking the corpus by one entry — a
            // genuine parser defect should not be mistaken for "command not
            // recognized" (which returns Unknown without reaching this branch).
            Console.Error.WriteLine(
                $"CommandIntentLookup.Resolve('{commandName}') failed: {ex.GetType().Name}: {ex.Message}");
            return IntentType.Unknown;
        }
    }
}
