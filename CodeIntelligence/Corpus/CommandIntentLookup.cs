using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeIntelligence.Corpus;

/// <summary>Maps a help-catalog command name to its intent by asking the real parser.
/// Duplicating the mapping here would let the corpus drift from recognition; this
/// cannot. Commands the parser does not recognize are skipped by the caller.</summary>
internal static class CommandIntentLookup
{
    internal static IntentType Resolve(string commandName)
    {
        try
        {
            return CommandFront.Parse(commandName, Directory.GetCurrentDirectory()).Intent;
        }
        catch (Exception)
        {
            return IntentType.Unknown;
        }
    }
}
