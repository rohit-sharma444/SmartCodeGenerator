namespace DomainDiscovery.Review;

/// <summary>
/// The tail of a review-loop `field` command: a field name with an optional explicit type.
/// </summary>
/// <remarks>
/// The type is marked with a colon rather than inferred from position. A field name may be
/// several words ("prep time"), so a bare trailing word is ambiguous — treating it as a type
/// silently turns the typo `Price decimel` into a field named PriceDecimel.
/// </remarks>
public readonly record struct FieldSpec(string Name, string? Type)
{
    /// <summary>Parses <c>"prep time"</c> or <c>"Servings:int"</c>.</summary>
    public static FieldSpec Parse(string tail)
    {
        if (string.IsNullOrWhiteSpace(tail))
            return new FieldSpec(string.Empty, null);

        var colon = tail.IndexOf(':');
        return colon < 0
            ? new FieldSpec(tail.Trim(), null)
            : new FieldSpec(tail[..colon].Trim(), tail[(colon + 1)..].Trim());
    }
}
