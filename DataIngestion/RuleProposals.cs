using CodeGenerationEngine.Models;
using DataIngestion.Schema;

namespace DataIngestion;

/// <summary>
/// Turns one column's scan into the rules that hold in every row of it.
/// </summary>
/// <remarks>
/// Every threshold here is a counter-example test: a rule is proposed only when the number of
/// rows breaking it is ZERO. Nothing is proposed from a sample, and nothing proposed here is
/// enforced until a user takes it.
/// </remarks>
public static class RuleProposals
{
    /// <summary>The largest distinct count that can be a status.</summary>
    private const int MaxStatusValues = 50;

    /// <summary>The longest a status value may be, in characters.</summary>
    /// <remarks>What separates a status from a low-cardinality description, or a notes column
    /// whose few entries happen to repeat.</remarks>
    private const int MaxStatusValueLength = 30;

    /// <summary>The formats the rule catalog already has, and the name each is shown by.</summary>
    /// <remarks>
    /// ONLY these, and copied verbatim from the catalog's own parsers
    /// (<c>NamedFormatRuleParser.Patterns</c> and <c>EmailRuleParser</c> in
    /// CodeGenerationEngine/Core/Validation/Rules) so a value the catalog's own rule accepts is
    /// never rejected here. A regex derived from 12,000 references is a regex that rejects the
    /// 12,001st, and it is exactly the kind of rule a user cannot judge from a card: "matches
    /// ^[A-Z]{3}-\d{4}$, holds in all 12,431 rows" reads like evidence and is a trap. These six
    /// are deliberately loose where the real-world format is, which is what makes them safe to
    /// offer.
    /// </remarks>
    private static readonly (string Name, string Pattern)[] KnownFormats =
    [
        ("email address", @"^[^@\s]+@[^@\s]+\.[^@\s]+$"),
        ("phone number", @"^\+?[0-9 ().\-]{7,20}$"),
        ("web address", @"^(?:https?://)?[^\s.]+(?:\.[^\s.]+)+$"),
        ("postcode", @"^[A-Za-z]{1,2}[0-9][0-9A-Za-z]?\s*[0-9][A-Za-z]{2}$"),
        ("GUID", @"^\{[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\}$|^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$|^[0-9a-fA-F]{32}$"),
        ("IP address", @"^(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$|^::$|^(?=[0-9A-Fa-f:]*[0-9A-Fa-f])(?!.*:::)[0-9A-Fa-f]{0,4}(?::[0-9A-Fa-f]{0,4}){2,7}$")
    ];

    /// <summary>
    /// Every rule <paramref name="scan"/> supports for a property called
    /// <paramref name="fieldName"/> of type <paramref name="propertyType"/>.
    /// </summary>
    /// <param name="mayBeUnique">
    /// Whether a uniqueness proposal is allowed at all. False for an entity that references
    /// another — a transaction line's product code repeats by design, and a young table where it
    /// happens not to have yet would otherwise propose a rule forbidding a second sale of the
    /// same product.
    /// </param>
    public static IReadOnlyList<RuleProposal> For(
        ColumnScanResult scan, string fieldName, string propertyType, bool mayBeUnique)
    {
        var proposals = new List<RuleProposal>();

        if (scan.NonBlankRowCount == 0)
            return proposals;

        var rows = scan.NonBlankRowCount.ToString("N0");

        if (propertyType == "string"
            && !scan.ExceededDistinctCap
            && scan.DistinctValues.Count is > 0 and <= MaxStatusValues
            && scan.LongestValueLength <= MaxStatusValueLength)
        {
            proposals.Add(new RuleProposal
            {
                Kind = RuleProposalKind.Status,
                Rule = new PropertyRule
                {
                    PropertyName = fieldName,
                    Name = "allowedvalues",
                    AllowedValues = [.. scan.DistinctValues]
                },
                Evidence =
                    $"{fieldName} holds only {string.Join(", ", scan.DistinctValues)} "
                    + $"in all {rows} rows{scan.PartialNote}"
            });
        }

        // A bool has exactly two values, so with more than two rows a "no repeats yet" streak is
        // only luck: the very next row must equal one of the two already seen.
        if (mayBeUnique && propertyType != "bool"
            && scan.DuplicateValueCount == 0 && scan.NonBlankRowCount > 1)
        {
            proposals.Add(new RuleProposal
            {
                Kind = RuleProposalKind.Unique,
                Rule = new PropertyRule
                {
                    PropertyName = fieldName,
                    Name = "unique",
                    IsUnique = true
                },
                Evidence = $"no two of the {rows} rows share the same {fieldName}{scan.PartialNote}"
            });
        }

        if (propertyType == "string" && MatchedFormat(scan) is { } format)
        {
            proposals.Add(new RuleProposal
            {
                Kind = RuleProposalKind.Pattern,
                Rule = new PropertyRule
                {
                    PropertyName = fieldName,
                    Name = "pattern",
                    RegexPattern = format.Pattern,
                    CustomRule = format.Name
                },
                Evidence = $"every one of the {rows} values is a valid {format.Name}{scan.PartialNote}"
            });
        }

        return proposals;
    }

    /// <summary>The first known format every scanned value matches, or null.</summary>
    /// <remarks>
    /// Only the values the scan KEPT can be checked, so a column past the distinct cap is not
    /// offered a pattern: a format that holds in the first 51 of a million values is not a format
    /// that holds in the million.
    /// </remarks>
    private static (string Name, string Pattern)? MatchedFormat(ColumnScanResult scan)
    {
        if (scan.ExceededDistinctCap || scan.DistinctValues.Count == 0)
            return null;

        foreach (var format in KnownFormats)
        {
            var regex = new System.Text.RegularExpressions.Regex(format.Pattern);

            if (scan.DistinctValues.All(v => regex.IsMatch(v)))
                return format;
        }

        return null;
    }
}
