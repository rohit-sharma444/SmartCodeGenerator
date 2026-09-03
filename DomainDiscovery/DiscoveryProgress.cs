namespace DomainDiscovery;

/// <summary>
/// What a discovery has finished, what it still has to do, and what it is doing now.
/// </summary>
/// <remarks>
/// A record rather than a bare list of names because the two-phase discovery has something the
/// one-shot never did: it knows the whole class list within about forty seconds, long before it
/// knows any of the detail. Reporting that as <see cref="Pending"/> is the point of the split —
/// the user sees every class the model intends to produce, and then watches them fill in.
/// <para>
/// <see cref="Done"/> and <see cref="Pending"/> are kept apart rather than merged into one list
/// with a flag because the panel draws them differently, and drawing a finished tick beside a
/// class that has not been written yet is exactly the kind of claim this codebase has had to fix
/// before.
/// </para>
/// </remarks>
public sealed record DiscoveryProgress(
    IReadOnlyList<string> Done,
    IReadOnlyList<string> Pending,
    string? Step)
{
    /// <summary>Nothing known yet — what a discovery reports before its first answer.</summary>
    public static readonly DiscoveryProgress None = new([], [], null);

    /// <summary>Everything the discovery has settled on, finished or not.</summary>
    public int Total => Done.Count + Pending.Count;
}
