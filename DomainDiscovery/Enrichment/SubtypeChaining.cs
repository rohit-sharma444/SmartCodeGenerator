using DomainDiscovery.SeedExtraction;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// One candidate as the shaper sees it before deciding its fate: its class name, the head noun
/// its own Wikipedia lead defined it as, and what <see cref="ArchetypeLexicon"/> made of that
/// head.
/// </summary>
public readonly record struct ShapeCandidate(string Name, string Head, ArchetypeResult Result);

/// <summary>
/// Resolves a candidate whose definition names ANOTHER candidate in the same model: "a motel is
/// a hotel", where Hotel is already shaped, makes Motel the same kind of thing.
/// </summary>
/// <remarks>
/// This is the half of the shaping problem no curation can reach. <see cref="ArchetypeLexicon"/>
/// is domain-independent by design, so it can hold "establishment" but never every subtype of
/// every establishment — and a real article set is full of them. Measured across twelve topic
/// articles, the heads hotel(5), hospital(4), school(4), airline(4) and bank(3) each named
/// another candidate in the very same model and blocked about twenty-five candidates between
/// them.
/// <para>
/// It compounds with the table rather than competing with it: once "institution" is an entry,
/// School shapes from the table, and every candidate defined as "a school" then chains off
/// School. Widening the table therefore widens this too, without a line of extra curation.
/// </para>
/// <para>
/// Only a <see cref="ShapeVerdict.Shaped"/> parent is inherited. An
/// <see cref="ShapeVerdict.Abstract"/> parent is a REJECTION, and propagating it would delete
/// the child rather than offer it — the expensive mistake, where leaving it
/// <see cref="ShapeVerdict.Unknown"/> merely offers it to the user.
/// </para>
/// </remarks>
public static class SubtypeChaining
{
    /// <summary>
    /// Returns each candidate's final <see cref="ArchetypeResult"/>, keyed by its name: the one
    /// it came in with, or the one it inherits from the candidate its head names. Every input
    /// candidate appears in the result exactly once.
    /// </summary>
    public static IReadOnlyDictionary<string, ArchetypeResult> Resolve(
        IReadOnlyList<ShapeCandidate> candidates)
    {
        var byName = new Dictionary<string, ShapeCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
            byName[Key(candidate.Name)] = candidate;

        var resolved = new Dictionary<string, ArchetypeResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            resolved[candidate.Name] =
                Chase(candidate, byName, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        return resolved;
    }

    /// <summary>
    /// Follows a candidate's head to the candidate it names, and that one's head in turn, until
    /// something shapes or the chain runs out.
    /// </summary>
    /// <remarks>
    /// <paramref name="visiting"/> is what makes this terminate. "An inn is a hostel" and "a
    /// hostel is an inn" are both real Wikipedia leads, and following them without a guard is a
    /// stack overflow rather than a bad answer.
    /// </remarks>
    private static ArchetypeResult Chase(
        ShapeCandidate candidate,
        Dictionary<string, ShapeCandidate> byName,
        HashSet<string> visiting)
    {
        // Already decided, one way or the other — a head only matters when nothing is known.
        if (candidate.Result.Verdict != ShapeVerdict.Unknown || candidate.Head.Length == 0)
            return candidate.Result;

        if (!visiting.Add(Key(candidate.Name)))
            return candidate.Result;

        if (!byName.TryGetValue(Key(candidate.Head), out var parent))
            return candidate.Result;

        // "A hotel is a hotel" defines nothing; without this a self-reference would burn the
        // one visit the guard allows and look like a cycle.
        if (Key(parent.Name) == Key(candidate.Name))
            return candidate.Result;

        var inherited = Chase(parent, byName, visiting);

        return inherited.Verdict == ShapeVerdict.Shaped ? inherited : candidate.Result;
    }

    /// <summary>
    /// Folds a name or a head onto one spelling so "hotels", "hotel" and the class name "Hotel"
    /// are the same key. Reuses the codebase's single singularizer rather than adding a second;
    /// a word it declines to fold (it is deliberately conservative) simply keeps its own
    /// spelling, which is still a usable key.
    /// </summary>
    private static string Key(string text)
    {
        var normalized = NameNormalizer.ToEntityName(text);
        return normalized.Length == 0 ? text : normalized;
    }
}
