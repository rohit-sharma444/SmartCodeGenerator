namespace DataIngestion.Normalization;

/// <summary>
/// Shows what the analyser found and asks what to do about it. Rendering is separate from the
/// loop so the display can be asserted as a value and only the loop needs a reader — the shape
/// <c>BuildAppCommand.ResolveTarget</c> established, for the same reason: a method that reads the
/// console cannot be driven from a test.
/// </summary>
public static class NormalizationPrompt
{
    /// <summary>
    /// The summary block: one numbered line per finding with its proposal, then any notes. The
    /// evidence is deliberately NOT here — it is what `d` is for, and inlining it would bury the
    /// list the user has to scan.
    /// </summary>
    public static IReadOnlyList<string> Lines(
        NormalizationReport report, string label, int rowCount)
    {
        var lines = new List<string>
        {
            string.Empty,
            $"Normalization check on {label} ({rowCount} rows):",
            string.Empty,
        };

        if (report.Findings.Count == 0)
        {
            lines.Add("  No normalization problems found.");
        }
        else
        {
            for (var i = 0; i < report.Findings.Count; i++)
            {
                lines.Add($"  {i + 1} ! {report.Findings[i].Headline}");
                lines.Add($"        -> {report.Findings[i].Proposal}");
            }
        }

        // A check that could not run says so. Silence here would read as "nothing to report".
        foreach (var note in report.Notes)
        {
            lines.Add(string.Empty);
            lines.Add("  " + note);
        }

        return lines;
    }

    /// <summary>
    /// Asks which findings to apply and returns them, in the order the analyser found them.
    /// Returns empty for `k`, for closed input, and when there is nothing to decide.
    /// </summary>
    public static IReadOnlyList<NormalizationFinding> Choose(
        NormalizationReport report, TextReader reader, TextWriter writer)
    {
        // Nothing to decide, so nothing is asked — and nothing is read, which matters: the caller's
        // reader is shared with the review loop that runs next.
        if (report.Findings.Count == 0)
            return [];

        while (true)
        {
            writer.WriteLine();
            writer.Write("  [n]ormalize   n <1 3> subset   [k]eep as-is   [d]etail <n>: ");

            var input = reader.ReadLine();

            // Closed stdin. The safe reading is "change nothing" — never a busy loop, and never
            // an unasked-for rewrite of the user's data.
            if (input is null)
                return [];

            var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            switch (parts[0].ToLowerInvariant())
            {
                case "k":
                case "keep":
                    return [];

                case "n":
                case "normalize":
                    if (parts.Length == 1)
                        return report.Findings;

                    if (TryResolve(report, parts.Skip(1), writer, out var subset))
                        return subset;

                    break;

                case "d":
                case "detail":
                    if (TryResolve(report, parts.Skip(1), writer, out var detail)
                        && detail.Count == 1)
                    {
                        foreach (var line in detail[0].Evidence)
                            writer.WriteLine(line);
                    }
                    else if (parts.Length < 2)
                    {
                        writer.WriteLine($"  Usage: d <1-{report.Findings.Count}>. "
                                         + "There is no finding without a number.");
                    }

                    break;

                default:
                    writer.WriteLine("  Unrecognized. Type n, n with numbers, k, or d <n>.");
                    break;
            }
        }
    }

    /// <summary>
    /// Resolves 1-based finding numbers. Reports anything out of range rather than dropping it:
    /// silently ignoring `n 1 9` would tell the user two splits were applied when one was.
    /// </summary>
    private static bool TryResolve(
        NormalizationReport report,
        IEnumerable<string> tokens,
        TextWriter writer,
        out IReadOnlyList<NormalizationFinding> resolved)
    {
        var chosen = new List<NormalizationFinding>();
        var indices = new SortedSet<int>();

        foreach (var token in tokens)
        {
            if (!int.TryParse(token, out var number)
                || number < 1
                || number > report.Findings.Count)
            {
                writer.WriteLine(
                    $"  There is no finding '{token}'. Valid: 1-{report.Findings.Count}.");
                resolved = [];
                return false;
            }

            indices.Add(number);
        }

        if (indices.Count == 0)
        {
            resolved = [];
            return false;
        }

        // Analyser order, not the order the user typed the numbers in, so the applied set is
        // deterministic however it was asked for.
        foreach (var index in indices)
            chosen.Add(report.Findings[index - 1]);

        resolved = chosen;
        return true;
    }
}
