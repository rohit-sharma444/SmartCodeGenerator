using CodeGenerationEngine.Core.Validation.Rules;
using CodeGenerationEngine.Models;
using DataIngestion.Schema;
using DomainDiscovery.Model;

namespace DataIngestion;

/// <summary>
/// Turns one entity's complete rows into the Cross-Field and Calculation rules they support —
/// the entity-wide counterpart to <see cref="RuleProposals"/>, which scans one column at a time.
/// </summary>
/// <remarks>
/// Every threshold here is a counter-example test over the FULL table, never a sample, for the
/// same reason RuleProposals is: a rule inferred from a sample is a guess about rows not yet
/// seen, and the app it generates would reject data its own source contains.
/// </remarks>
public static class EntityRuleProposals
{
    /// <summary>Above this many numeric columns, the Calculation scan's triple search is
    /// skipped for the entity rather than left to grow cubically. Twelve columns is 1,320
    /// candidate triples; fifty would be well over a hundred thousand.</summary>
    private const int MaxNumericColumnsForCalculationScan = 12;

    public static IReadOnlyList<EntityRuleProposal> For(CandidateEntity entity, TabularTable table)
    {
        var proposals = new List<EntityRuleProposal>();

        var dateFields = entity.Fields.Where(f => f.Type == "DateTime").ToList();
        var numericFields = entity.Fields.Where(f => f.Type is "int" or "decimal").ToList();

        AddCrossFieldProposals(proposals, table, dateFields);
        AddCrossFieldProposals(proposals, table, numericFields);

        return proposals;
    }

    private static void AddCrossFieldProposals(
        List<EntityRuleProposal> proposals, TabularTable table, List<CandidateField> fields)
    {
        foreach (var left in fields)
        foreach (var right in fields)
        {
            if (left.Name == right.Name)
                continue;

            if (!HoldsForEveryRow(table, left.Name, right.Name, out var sawStrictlyLess))
                continue;

            if (!sawStrictlyLess)
                continue;

            var rows = table.Rows.Count.ToString("N0");

            proposals.Add(new EntityRuleProposal
            {
                Kind = EntityRuleProposalKind.CrossField,
                Properties = [left.Name, right.Name],
                Rule = new ComparisonRule(
                    left.Name, right.Name, "> 0", $"{left.Name} must not be after {right.Name}"),
                Evidence = $"{left.Name} is always on or before {right.Name}, in all {rows} rows"
            });
        }
    }

    /// <summary>Whether left &lt;= right in every row where both are present and parseable.
    /// <paramref name="sawStrictlyLess"/> guards against two columns that are always equal,
    /// which would otherwise pass in both directions and propose two redundant rules.</summary>
    private static bool HoldsForEveryRow(
        TabularTable table, string leftColumn, string rightColumn, out bool sawStrictlyLess)
    {
        sawStrictlyLess = false;
        var compared = 0;

        for (var row = 0; row < table.Rows.Count; row++)
        {
            if (!TryParseComparable(table.Value(row, leftColumn), out var left)
                || !TryParseComparable(table.Value(row, rightColumn), out var right))
                continue;

            compared++;
            var order = left.CompareTo(right);

            if (order > 0)
                return false;

            if (order < 0)
                sawStrictlyLess = true;
        }

        return compared > 0;
    }

    private static bool TryParseComparable(string? value, out System.IComparable comparable)
    {
        comparable = 0m;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (decimal.TryParse(
                value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            comparable = number;
            return true;
        }

        if (System.DateTime.TryParse(
                value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
        {
            comparable = date;
            return true;
        }

        return false;
    }
}
