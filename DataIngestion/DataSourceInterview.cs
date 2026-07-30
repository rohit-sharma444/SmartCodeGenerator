using DataIngestion.Schema.Readers;

namespace DataIngestion;

/// <summary>What the interview concluded.</summary>
public enum InterviewDecision
{
    /// <summary>The user has no data. The caller runs the unchanged Wikipedia path.</summary>
    NoData,

    /// <summary>The user named a source. <see cref="InterviewOutcome.Source"/> holds it.</summary>
    Source,

    /// <summary>
    /// The user backed out, or stdin closed mid-interview. Deliberately not
    /// <see cref="NoData"/>: someone who abandons the interview must not be handed a Wikipedia
    /// build they never asked for.
    /// </summary>
    Cancelled
}

public sealed record InterviewOutcome(InterviewDecision Decision, DataSourceSpec? Source);

/// <summary>
/// Asks whether the user has data, in what format, and where. Reads and writes through the
/// <see cref="TextReader"/>/<see cref="TextWriter"/> it is given rather than the console, so the
/// whole flow is drivable from a test — the pattern <c>BuildAppCommand.ResolveTarget</c>
/// established for exactly this reason.
/// </summary>
/// <remarks>
/// The caller decides whether to run this at all: redirected stdin never prompts. See
/// <see cref="InlineDataSource"/> for the scriptable form.
/// </remarks>
public static class DataSourceInterview
{
    private static readonly InterviewOutcome Cancelled = new(InterviewDecision.Cancelled, null);

    public static InterviewOutcome Resolve(TextReader reader, TextWriter writer)
    {
        writer.Write("Do you have existing data? [y/N]: ");
        var answer = reader.ReadLine();

        // EOF is the absence of an answer, not the default answer. Conflating the two would run a
        // whole discovery pipeline off a closed pipe.
        if (answer is null)
            return Cancelled;

        var trimmed = answer.Trim();

        if (trimmed.Length == 0
            || trimmed.StartsWith("n", StringComparison.OrdinalIgnoreCase))
            return new InterviewOutcome(InterviewDecision.NoData, null);

        return ResolveFormat(reader, writer);
    }

    private static InterviewOutcome ResolveFormat(TextReader reader, TextWriter writer)
    {
        while (true)
        {
            writer.Write($"Format [{string.Join("/", DataSourceReaders.Keys)}] (c to cancel): ");
            var line = reader.ReadLine();

            if (line is null)
                return Cancelled;

            var text = line.Trim();

            if (IsCancel(text))
                return Cancelled;

            if (!DataSourceReaders.TryParseKind(text, out var kind))
            {
                writer.WriteLine(
                    "  Unknown format. Valid: " + string.Join(", ", DataSourceReaders.Keys));
                continue;
            }

            // On the menu, but with no reader in this build. Say why, say what works, ask again.
            if (!DataSourceReaders.TryCreate(kind, out _))
            {
                writer.WriteLine("  " + DataSourceReaders.NotSupportedMessage(kind));
                continue;
            }

            return ResolveLocation(kind, reader, writer);
        }
    }

    private static InterviewOutcome ResolveLocation(
        DataSourceKind kind, TextReader reader, TextWriter writer)
    {
        while (true)
        {
            writer.Write($"{kind.ToString().ToUpperInvariant()} file path (c to cancel): ");
            var line = reader.ReadLine();

            if (line is null)
                return Cancelled;

            var text = line.Trim();

            if (IsCancel(text))
                return Cancelled;

            if (text.Length == 0)
                continue;

            // A path pasted from Explorer arrives quoted.
            var path = text.Length >= 2 && text[0] == '"' && text[^1] == '"'
                ? text[1..^1]
                : text;

            // A mistyped path is the likeliest thing to go wrong here, so re-ask rather than
            // abort — the same way ResolveTarget re-asks on an unknown target key.
            if (!File.Exists(path))
            {
                writer.WriteLine($"  No file at {path}.");
                continue;
            }

            return new InterviewOutcome(
                InterviewDecision.Source, new DataSourceSpec(kind, path));
        }
    }

    private static bool IsCancel(string text)
        => string.Equals(text, "c", StringComparison.OrdinalIgnoreCase)
           || string.Equals(text, "cancel", StringComparison.OrdinalIgnoreCase);
}
