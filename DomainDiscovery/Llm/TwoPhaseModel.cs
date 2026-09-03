namespace DomainDiscovery.Llm;

/// <summary>
/// Puts the two discovery passes back together: the roster decides which classes exist, the detail
/// pass decides what is in them.
/// </summary>
/// <remarks>
/// The roster is the authority on membership, and that asymmetry is deliberate. It is the cheap
/// pass — about forty seconds against two and a half minutes — so it almost always finishes, and
/// it is the list the user has already been shown. The detail pass is the long one and the one
/// that gets cut short, so treating it as authoritative would let a truncation silently delete
/// classes off the end of a list the user was looking at.
/// </remarks>
public static class TwoPhaseModel
{
    /// <summary>
    /// Every class the roster named, carrying whatever detail the second pass produced for it.
    /// </summary>
    /// <param name="detail">
    /// Null when the detail pass produced nothing at all — a run stopped before its first complete
    /// class. The roster alone is still a usable model: the normalizer gives each class a key, and
    /// a list of correctly named and classified classes with no fields is a far better starting
    /// point than the failure this used to be.
    /// </param>
    public static LlmDomainModel Merge(LlmDomainModel roster, LlmDomainModel? detail)
    {
        var detailed = ByName(detail);

        var merged = new LlmDomainModel();

        foreach (var named in roster.Classes)
        {
            if (!detailed.TryGetValue(named.Name, out var filled))
            {
                merged.Classes.Add(named);
                continue;
            }

            // The roster's own name, role and purpose are kept: they were agreed in the first pass
            // and drawn on screen, and a second pass that quietly reclassifies Sale from a
            // Transaction to an Item would change what the generator writes for it. Only the parts
            // the second pass was actually asked for are taken from it.
            merged.Classes.Add(new LlmClass
            {
                Name = named.Name,
                Role = named.Role,
                Purpose = string.IsNullOrWhiteSpace(named.Purpose) ? filled.Purpose : named.Purpose,
                Properties = filled.Properties,
                Relationships = filled.Relationships
            });
        }

        return merged;
    }

    /// <summary>
    /// The roster classes the detail pass has not covered yet — what the panel lists as still to
    /// come while the second pass runs.
    /// </summary>
    public static IReadOnlyList<LlmClass> WithoutDetail(LlmDomainModel roster, LlmDomainModel? detail)
    {
        var detailed = ByName(detail);

        return roster.Classes.Where(c => !detailed.ContainsKey(c.Name)).ToList();
    }

    /// <summary>
    /// The detail pass's classes by name, case-insensitively — a 3B model told to return the same
    /// names will still hand back "product" for "Product" often enough to matter. A duplicate keeps
    /// the first, since a later one is the model repeating itself rather than correcting itself.
    /// </summary>
    private static Dictionary<string, LlmClass> ByName(LlmDomainModel? detail)
    {
        var byName = new Dictionary<string, LlmClass>(StringComparer.OrdinalIgnoreCase);

        foreach (var filled in detail?.Classes ?? [])
        {
            if (!string.IsNullOrWhiteSpace(filled.Name))
                byName.TryAdd(filled.Name, filled);
        }

        return byName;
    }
}
