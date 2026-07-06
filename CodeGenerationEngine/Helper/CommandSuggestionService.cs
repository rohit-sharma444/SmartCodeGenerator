namespace CodeGenerationEngine.Helper;

/// <summary>
/// Provides suggestions for command inputs based on a predefined command catalog.
/// </summary>
public static class CommandSuggestionService
{
    public static IEnumerable<string> GetSuggestions(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];

        return CommandCatalog.Commands
            .Where(x => x.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            .Take(10);
    }
}