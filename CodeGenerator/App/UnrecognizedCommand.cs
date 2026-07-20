namespace CodeGenerator.App;

/// <summary>
/// Builds the message shown when the pipeline does not recognize a command, with
/// an optional "did you mean" line from <see cref="CommandSuggester"/>.
/// </summary>
internal static class UnrecognizedCommand
{
    internal static string Describe(string input, IReadOnlyList<string> suggestions)
    {
        var head = $"Command not recognized: '{(input ?? string.Empty).Trim()}'. " +
                   "Type 'help' for the command list.";

        if (suggestions is null || suggestions.Count == 0)
            return head;

        return head + Environment.NewLine +
               $"Did you mean: {string.Join(", ", suggestions)}?";
    }
}
