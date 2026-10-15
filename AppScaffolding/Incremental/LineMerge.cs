namespace AppScaffolding.Incremental;

/// <summary>One change an add makes to a file: lines taken out and lines put in, at one place.</summary>
public sealed record LineChange(IReadOnlyList<string> Removed, IReadOnlyList<string> Added);

/// <summary>What <see cref="LineMerge.Apply"/> made of a file.</summary>
/// <param name="Merged">The file with the changes in it, or null when one of them could not be
/// placed with certainty.</param>
/// <param name="Changes">Every change the add wanted to make, placed or not - the lines a user
/// has to copy by hand when <paramref name="Merged"/> is null.</param>
public sealed record LineMergeResult(string? Merged, IReadOnlyList<LineChange> Changes);

/// <summary>
/// Puts into a file the lines an add changes, and nothing else.
/// </summary>
/// <remarks>
/// A three-way merge. <c>before</c> is what the generator writes for the app as it is,
/// <c>after</c> what it writes with the new classes; the difference between the two is exactly
/// the new classes' lines (a <c>DbSet</c>, a <c>Seed&lt;T&gt;</c> call, a screen registration).
/// Those lines are placed into the file on disk by the lines around them, so whatever the user
/// changed elsewhere in it stays. Copying the whole generated file, as an add did before, threw
/// those changes away.
/// <para>
/// A change is placed only where its surrounding lines say, unambiguously, that it belongs. When
/// that is not certain the merge gives up rather than guess: a line put in the wrong place
/// compiles as often as not, and is far harder to find than a file left as it was.
/// </para>
/// </remarks>
public static class LineMerge
{
    /// <summary>How many lines on each side of a change are compared to find its place.</summary>
    private const int Context = 3;

    /// <summary>Above this many cells the comparison is not tried. The versions an add compares
    /// differ by a handful of lines; the limit only keeps the table bounded.</summary>
    private const long MaxCells = 4_000_000;

    public static LineMergeResult Apply(string before, string after, string existing)
    {
        var old = Lines(before);
        var @new = Lines(after);

        var hunks = Hunks(old, @new);

        if (hunks is null)
            return new LineMergeResult(null, [new LineChange(old, @new)]);
        var changes = hunks.Select(h => new LineChange(h.Removed, h.Added)).ToList();

        var newline = existing.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var endsWithNewline = existing.EndsWith('\n');
        var target = Lines(existing);

        var placements = new List<(int At, Hunk Hunk)>();
        var from = 0;

        foreach (var hunk in hunks)
        {
            var at = Place(hunk, old, target, from);

            if (at < 0)
                return new LineMergeResult(null, changes);

            placements.Add((at, hunk));
            from = at + hunk.Removed.Count;
        }

        var merged = new List<string>(target);

        // Back to front, so an earlier placement's index is not moved by a later edit.
        for (var i = placements.Count - 1; i >= 0; i--)
        {
            var (at, hunk) = placements[i];
            merged.RemoveRange(at, hunk.Removed.Count);
            merged.InsertRange(at, hunk.Added);
        }

        var text = string.Join(newline, merged);

        if (endsWithNewline)
            text += newline;

        return new LineMergeResult(text, changes);
    }

    /// <summary>All of <paramref name="text"/> as one change that adds it.</summary>
    public static LineChange Whole(string text) => new([], Lines(text));

    /// <summary>The lines of <paramref name="text"/> without their line ends. A final line end
    /// does not make an empty last line.</summary>
    private static List<string> Lines(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        if (lines.Count > 0 && lines[^1].Length == 0 && text.EndsWith('\n'))
            lines.RemoveAt(lines.Count - 1);

        return lines;
    }

    private static bool Same(string a, string b)
        => string.Equals(a.TrimEnd(), b.TrimEnd(), StringComparison.Ordinal);

    private sealed record Hunk(int Start, List<string> Removed, List<string> Added);

    /// <summary>The changes between <paramref name="old"/> and <paramref name="new"/>, in order,
    /// from a longest-common-subsequence alignment of their lines.</summary>
    private static List<Hunk>? Hunks(List<string> old, List<string> @new)
    {
        // The common head and tail are set aside first: the two versions differ by a few lines,
        // so the table below is tiny instead of lines-squared.
        var head = 0;
        while (head < old.Count && head < @new.Count && Same(old[head], @new[head]))
            head++;

        var tail = 0;
        while (tail < old.Count - head && tail < @new.Count - head
               && Same(old[old.Count - 1 - tail], @new[@new.Count - 1 - tail]))
            tail++;

        var n = old.Count - tail;
        var m = @new.Count - tail;

        if ((long)(n - head + 1) * (m - head + 1) > MaxCells)
            return null;

        var lcs = new int[n - head + 1, m - head + 1];

        for (var i = n - 1; i >= head; i--)
            for (var j = m - 1; j >= head; j--)
                lcs[i - head, j - head] = Same(old[i], @new[j])
                    ? lcs[i - head + 1, j - head + 1] + 1
                    : Math.Max(lcs[i - head + 1, j - head], lcs[i - head, j - head + 1]);

        var hunks = new List<Hunk>();
        Hunk? open = null;
        int a = head, b = head;

        while (a < n || b < m)
        {
            if (a < n && b < m && Same(old[a], @new[b]))
            {
                if (open is not null)
                {
                    hunks.Add(open);
                    open = null;
                }

                a++;
                b++;
                continue;
            }

            open ??= new Hunk(a, [], []);

            if (b < m && (a == n || lcs[a - head, b - head + 1] >= lcs[a - head + 1, b - head]))
            {
                open.Added.Add(@new[b]);
                b++;
            }
            else
            {
                open.Removed.Add(old[a]);
                a++;
            }
        }

        if (open is not null)
            hunks.Add(open);

        return hunks;
    }

    /// <summary>
    /// Where in <paramref name="target"/> <paramref name="hunk"/> goes, or -1 when that is not
    /// certain.
    /// </summary>
    /// <remarks>
    /// Every position at or after <paramref name="from"/> holding the lines the change removes is
    /// scored by how many of the lines around it in <paramref name="old"/> stand around it in the
    /// target too. The best score wins only if it is unique and is backed by at least two lines of
    /// context (or by all the context there is, in a file too short to have two).
    /// </remarks>
    private static int Place(Hunk hunk, List<string> old, List<string> target, int from)
    {
        var available = Math.Min(Context, hunk.Start)
                        + Math.Min(Context, old.Count - (hunk.Start + hunk.Removed.Count));
        var required = Math.Min(2, available);

        var best = -1;
        var bestScore = -1;
        var tied = false;

        for (var p = from; p + hunk.Removed.Count <= target.Count; p++)
        {
            if (!Holds(target, p, hunk.Removed))
                continue;

            var score = ContextScore(hunk, old, target, p);

            if (score > bestScore)
            {
                best = p;
                bestScore = score;
                tied = false;
            }
            else if (score == bestScore)
            {
                tied = true;
            }
        }

        return best >= 0 && !tied && bestScore >= required ? best : -1;
    }

    private static bool Holds(List<string> target, int at, List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
            if (!Same(target[at + i], lines[i]))
                return false;

        return true;
    }

    /// <summary>The lines just above and just below the change, as far as they agree, up to
    /// <see cref="Context"/> on each side.</summary>
    private static int ContextScore(Hunk hunk, List<string> old, List<string> target, int at)
    {
        var score = 0;

        for (var i = 1; i <= Context; i++)
        {
            var o = hunk.Start - i;
            var t = at - i;

            if (o < 0 || t < 0 || !Same(old[o], target[t]))
                break;

            score++;
        }

        for (var i = 0; i < Context; i++)
        {
            var o = hunk.Start + hunk.Removed.Count + i;
            var t = at + hunk.Removed.Count + i;

            if (o >= old.Count || t >= target.Count || !Same(old[o], target[t]))
                break;

            score++;
        }

        return score;
    }
}
