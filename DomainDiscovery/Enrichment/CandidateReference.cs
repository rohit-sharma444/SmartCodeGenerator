using DomainDiscovery.Model;

namespace DomainDiscovery.Enrichment;

/// <summary>
/// Records that one candidate entity references another: the relationship AND the
/// <c>&lt;Target&gt;Id</c> field that makes it visible to everything downstream.
/// </summary>
/// <remarks>
/// Both halves, or neither. AppScaffolding's <c>RelationshipConvention</c> spells out the triple a
/// relationship has to produce -- a <c>&lt;Target&gt;Id</c>, a forward reference and an inverse --
/// and warns that anything emitting only part of it is invisible to <c>ForeignKeys.TargetsOf</c>,
/// and so to the seeder, the generated validators, the console picker and the screen split.
/// DomainDiscovery has no project reference to that assembly, so the field half is spelled here
/// instead, in the one place both discovery paths call.
/// <para>
/// The two paths drifted exactly once and it was expensive: the language-model normalizer turned a
/// declared <c>SupplierId</c> into a relationship and dropped the scalar as a duplicate, so the
/// mapper added a <c>Supplier</c> navigation property with no foreign key beside it. Not one key
/// resolved, and <c>build app: purchase ordering system for a wholesaler</c> came out as seven
/// masters and zero transactions -- no document screen, no lines grid, no totals band, and nothing
/// in the emitted diff to say why.
/// </para>
/// </remarks>
public static class CandidateReference
{
    /// <summary>Adds the relationship and its foreign key. The caller decides whether the
    /// reference is allowed; this decides what a recorded one is made of.</summary>
    public static void Record(CandidateEntity referencing, CandidateEntity target, string role)
    {
        referencing.Relationships.Add(
            new CandidateRelationship { Target = target.Name, Role = role });

        AddForeignKey(referencing, target);
    }

    /// <summary>
    /// Adds the <c>&lt;Target&gt;Id</c> field, unless one of the three shapes that cannot be
    /// declared stops it.
    /// </summary>
    /// <remarks>
    /// The field is skipped -- never the relationship -- when it would collide with a field the
    /// entity already has, would be literally named "Id" (which is generated, not declared: see
    /// <c>Review.ModelEditor.AddField</c>), or would equal the referencing entity's own name
    /// (<c>class Order { int Order }</c> is CS0542). These are the same guards
    /// <c>Review.ModelEditor.AddField</c> applies to a name the user types; a field that arrives
    /// with a relationship is not exempt from any of them.
    /// </remarks>
    public static void AddForeignKey(CandidateEntity referencing, CandidateEntity target)
    {
        var fieldName = $"{target.Name}Id";

        if (referencing.Fields.Any(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase)))
            return;

        if (string.Equals(fieldName, "Id", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(fieldName, referencing.Name, StringComparison.OrdinalIgnoreCase))
            return;

        referencing.Fields.Add(new CandidateField
        {
            Name = fieldName,
            Type = "int",
            Provenance = referencing.Provenance
        });
    }
}
