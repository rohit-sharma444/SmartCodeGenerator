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

        // Each file the add left as it was because the user had changed it where the new lines
        // go: the new version is beside it, and the lines are spelled out so they can be copied
        // without diffing two files.
        foreach (var aside in result.NotMerged)
        {
            lines.Add(string.Empty);
            // Not "your changes": the file also differs when the reviewed model is not the one the
            // app was generated from (a discovery run rarely gives the same properties twice), and
            // a live run said "changes of yours" about files nobody had touched.
            lines.Add($"  {aside.Path} is not what the generator would write for this app as it is now "
                + "(it was edited, or the app was generated from a different model), so the new lines "
                + "could not be placed with certainty and it was left as it is.");
            lines.Add($"  The version with the new classes is {aside.NewVersionPath}. To finish the add, make these changes in {aside.Path}:");

            foreach (var change in aside.Changes)
            {
                foreach (var removed in change.Removed)
                    lines.Add("    - " + removed);

                foreach (var added in change.Added)
                    lines.Add("    + " + added);
            }
        }

        return lines;
    }
}
