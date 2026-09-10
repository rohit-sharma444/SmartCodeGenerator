using AppScaffolding.Incremental;

namespace CodeGenerator.App;

/// <summary>
/// What an add did, for the console.
/// </summary>
/// <remarks>
/// Its own type so the wording is testable — the loop that prints it is not. The untouched count
/// is here deliberately: it is the half a user wants verified and the half a generator never
/// usually says.
/// </remarks>
internal static class AddReport
{
    public static IReadOnlyList<string> Lines(IncrementalResult result, string projectFolder)
    {
        if (!result.Success)
            return [string.Empty, $"  Nothing was added: {result.Message}"];

        var lines = new List<string> { string.Empty, "  Written:" };

        foreach (var file in result.Written)
            lines.Add("    " + Path.GetRelativePath(projectFolder, file));

        lines.Add(string.Empty);
        lines.Add($"  {result.UntouchedFiles} file(s) untouched.");

        // Last, and after the count, because it is the one thing here that needs doing rather
        // than reading: the regenerated half of these validators declares a hook the kept
        // .Custom.cs half does not, and the project will not compile until they are changed.
        if (result.OutdatedValidatorStubs.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("  Written before the validation pipeline, and kept:");

            foreach (var stub in result.OutdatedValidatorStubs)
                lines.Add("    " + stub);
        }

        return lines;
    }
}
