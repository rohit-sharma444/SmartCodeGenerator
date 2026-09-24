namespace CodeGenerator.App.Resume;

/// <summary>What to do about an unfinished build found in this workspace.</summary>
internal enum ResumeChoice
{
    /// <summary>Pick it up where it was left.</summary>
    Resume,

    /// <summary>Start over. The snapshot is left where it is, so nothing is lost.</summary>
    Fresh,

    /// <summary>Delete it.</summary>
    Discard
}

/// <summary>
/// The question put when a workspace is holding an unfinished build.
/// </summary>
/// <remarks>
/// Pure and tested, as <see cref="OverwritePrompt"/> is, because the console loop that asks it
/// cannot be driven from a test.
/// </remarks>
internal static class ResumeOffer
{
    /// <summary>
    /// The choice <paramref name="answer"/> names, or null if it names none and the question
    /// should be put again.
    /// </summary>
    /// <remarks>
    /// Blank and end-of-input start FRESH. That is not the same reasoning as
    /// <see cref="OverwritePrompt"/>, where the effortless answer had to be the one that
    /// destroyed nothing: here nothing is destroyed either way, since starting fresh leaves the
    /// snapshot on disk for the next run to offer again. The hazard runs the other way — someone
    /// pressing Enter without reading, resuming a build they did not know was there, and
    /// generating an application from a stale model or from a colleague's abandoned session.
    /// Fresh is also exactly what this command did before any of this existed, so an Enter
    /// changes nothing anyone already relies on.
    /// <para>
    /// Anything unrecognised returns null rather than falling back to a default, because a
    /// default would let a typo choose.
    /// </para>
    /// </remarks>
    public static ResumeChoice? Parse(string? answer)
    {
        var text = (answer ?? string.Empty).Trim();

        if (text.Length == 0)
            return ResumeChoice.Fresh;

        return text.ToLowerInvariant() switch
        {
            "r" or "resume" => ResumeChoice.Resume,
            "f" or "fresh" => ResumeChoice.Fresh,
            "d" or "discard" => ResumeChoice.Discard,
            _ => null
        };
    }

    /// <summary>
    /// Said after "start fresh", because it changes what this run does.
    /// </summary>
    /// <remarks>
    /// A workspace keeps one unfinished build, and "start fresh" promises to keep the old one. So
    /// the new run is not saved as it goes - which is worth knowing before closing its tab.
    /// </remarks>
    public const string KeptAsideNotice =
        "  The unfinished build is kept for next time. This one is not saved as you go - "
        + "answer d next time to replace it.";

    /// <summary>What is waiting, and what the answers do.</summary>
    public static IReadOnlyList<string> Lines(BuildSnapshot snapshot, DateTimeOffset now)
    {
        var what = new List<string> { Described(snapshot) };

        if (snapshot.AppType is { Length: > 0 } appType)
            what.Add(appType.ToUpperInvariant());

        // Only when there IS a model. "0 classes reviewed" on a build abandoned before discovery
        // ran claims a review that never happened.
        if (snapshot.Entities is { Count: > 0 } entities)
            what.Add($"{entities.Count} {(entities.Count == 1 ? "class" : "classes")} reviewed");

        return
        [
            "An unfinished build is waiting in this workspace:",
            "  " + string.Join("  -  ", what),
            $"  left off at {snapshot.Stage}, {Ago(snapshot.SavedAt, now)}",
            "",
            "  [r]esume   start [f]resh   [d]iscard",
        ];
    }

    private static string Described(BuildSnapshot snapshot)
        => snapshot.Description is { Length: > 0 } description
            ? $"\"{description}\""
            : "no description yet";

    /// <summary>
    /// How long ago, roughly.
    /// </summary>
    /// <remarks>
    /// Rough on purpose. The question it answers is "is this mine, from just now, or someone
    /// else's from last week", and a timestamp to the second reads as precision nobody asked for.
    /// </remarks>
    private static string Ago(DateTimeOffset then, DateTimeOffset now)
    {
        var elapsed = now - then;

        if (elapsed < TimeSpan.FromMinutes(1))
            return "moments ago";

        if (elapsed < TimeSpan.FromHours(1))
            return $"{(int)elapsed.TotalMinutes} minutes ago";

        if (elapsed < TimeSpan.FromDays(1))
            return $"{(int)elapsed.TotalHours} hours ago";

        return $"{(int)elapsed.TotalDays} days ago";
    }
}
