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

    /// <summary>A single-type cell parser: a DateTime-only parser can never hand back a decimal,
    /// and vice versa, so two same-typed fields can never be compared as different runtime
    /// types.</summary>
    private delegate bool TryParse(string? value, out System.IComparable comparable);

    public static IReadOnlyList<EntityRuleProposal> For(CandidateEntity entity, TabularTable table)
    {
        var proposals = new List<EntityRuleProposal>();

        var dateFields = entity.Fields.Where(f => f.Type == "DateTime").ToList();
        var numericFields = entity.Fields.Where(f => f.Type is "int" or "decimal").ToList();

        // The raw source column for each field, not the normalized field name: a source header
        // that was renamed off its class name (see DataModelBuilder.ResolveColumns) never appears
        // in the table verbatim, so looking cells up by field.Name would silently find nothing.
        var columnByField = entity.Fields.ToDictionary(
            f => f.Name,
            f => DataModelBuilder.ColumnFor(table, f.Name) ?? f.Name);

        AddCrossFieldProposals(proposals, table, dateFields, columnByField, TryParseDate);
        AddCrossFieldProposals(proposals, table, numericFields, columnByField, TryParseDecimal);

        if (numericFields.Count <= MaxNumericColumnsForCalculationScan)
            AddCalculationProposals(proposals, table, numericFields, columnByField);

        return proposals;
    }

    private static void AddCrossFieldProposals(
        List<EntityRuleProposal> proposals, TabularTable table, List<CandidateField> fields,
        IReadOnlyDictionary<string, string> columnByField, TryParse tryParse)
    {
        foreach (var left in fields)
        foreach (var right in fields)
        {
            if (left.Name == right.Name)
                continue;

            if (!HoldsForEveryRow(
                    table, columnByField[left.Name], columnByField[right.Name], tryParse,
                    out var sawStrictlyLess))
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
                Evidence = $"{left.Name} is always on or before {right.Name}, in all {rows} rows{table.PartialNote}"
            });
        }
    }

    /// <summary>Whether left &lt;= right in every row where both are present and parseable.
    /// <paramref name="sawStrictlyLess"/> guards against two columns that are always equal,
    /// which would otherwise pass in both directions and propose two redundant rules.</summary>
    private static bool HoldsForEveryRow(
        TabularTable table, string leftColumn, string rightColumn, TryParse tryParse,
        out bool sawStrictlyLess)
    {
        sawStrictlyLess = false;
        var compared = 0;

        for (var row = 0; row < table.Rows.Count; row++)
        {
            if (!tryParse(table.Value(row, leftColumn), out var left)
                || !tryParse(table.Value(row, rightColumn), out var right))
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

    private static void AddCalculationProposals(
        List<EntityRuleProposal> proposals, TabularTable table, List<CandidateField> numericFields,
        IReadOnlyDictionary<string, string> columnByField)
    {
        foreach (var target in numericFields)
        {
            var others = numericFields.Where(f => f.Name != target.Name).ToList();

            for (var i = 0; i < others.Count; i++)
            for (var j = i + 1; j < others.Count; j++)
            {
                TryProposeSumOrProduct(proposals, table, columnByField, target, others[i], others[j]);
                TryProposePercentage(proposals, table, columnByField, target, others[i], others[j]);
                TryProposePercentage(proposals, table, columnByField, target, others[j], others[i]);
            }
        }
    }

    private static void TryProposeSumOrProduct(
        List<EntityRuleProposal> proposals, TabularTable table, IReadOnlyDictionary<string, string> columnByField,
        CandidateField target, CandidateField left, CandidateField right)
    {
        if (ZeroCounterExamples(
                table, columnByField[target.Name], columnByField[left.Name], columnByField[right.Name],
                (l, r) => l + r, out var sumRows))
            proposals.Add(new EntityRuleProposal
            {
                Kind = EntityRuleProposalKind.Calculation,
                Properties = [target.Name, left.Name, right.Name],
                Rule = new ArithmeticRule(target.Name, left.Name, "plus", right.Name),
                Evidence = $"{target.Name} always equals {left.Name} plus {right.Name}, in all {sumRows:N0} rows{table.PartialNote}"
            });

        if (ZeroCounterExamples(
                table, columnByField[target.Name], columnByField[left.Name], columnByField[right.Name],
                (l, r) => l * r, out var productRows))
            proposals.Add(new EntityRuleProposal
            {
                Kind = EntityRuleProposalKind.Calculation,
                Properties = [target.Name, left.Name, right.Name],
                Rule = new ArithmeticRule(target.Name, left.Name, "times", right.Name),
                Evidence = $"{target.Name} always equals {left.Name} times {right.Name}, in all {productRows:N0} rows{table.PartialNote}"
            });
    }

    private static void TryProposePercentage(
        List<EntityRuleProposal> proposals, TabularTable table, IReadOnlyDictionary<string, string> columnByField,
        CandidateField target, CandidateField net, CandidateField rate)
    {
        if (!ZeroCounterExamples(
                table, columnByField[target.Name], columnByField[net.Name], columnByField[rate.Name],
                (n, r) => n * (1 + r), out var rows))
            return;

        proposals.Add(new EntityRuleProposal
        {
            Kind = EntityRuleProposalKind.Calculation,
            Properties = [target.Name, net.Name, rate.Name],
            Rule = new PercentageRule(target.Name, net.Name, rate.Name),
            Evidence = $"{target.Name} always equals {net.Name} times (1 plus {rate.Name}), in all {rows:N0} rows{table.PartialNote}"
        });
    }

    private static bool ZeroCounterExamples(
        TabularTable table, string targetColumn, string leftColumn, string rightColumn,
        System.Func<decimal, decimal, decimal> combine, out int rowsCompared)
    {
        rowsCompared = 0;

        for (var row = 0; row < table.Rows.Count; row++)
        {
            if (!TryParseAmount(table.Value(row, targetColumn), out var t)
                || !TryParseAmount(table.Value(row, leftColumn), out var l)
                || !TryParseAmount(table.Value(row, rightColumn), out var r))
                continue;

            rowsCompared++;

            if (System.Math.Abs(t - combine(l, r)) > 0.0001m)
                return false;
        }

        return rowsCompared > 0;
    }

    /// <summary>The Calculation scan's own decimal parser — distinct from <see cref="TryParseDecimal"/>
    /// (which hands back <see cref="System.IComparable"/> for the Cross-Field scan's ordering compare)
    /// because that method's out-parameter type differs only in what it is declared as: a call written
    /// as `TryParseDecimal(value, out var x)` cannot pick between them, so this scan gets its own,
    /// differently-named parser instead of overloading the existing one.</summary>
    private static bool TryParseAmount(string? value, out decimal result)
    {
        result = 0m;
        return !string.IsNullOrWhiteSpace(value)
            && decimal.TryParse(
                value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseDate(string? value, out System.IComparable comparable)
    {
        comparable = System.DateTime.MinValue;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (System.DateTime.TryParse(
                value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
        {
            comparable = date;
            return true;
        }

        return false;
    }

    private static bool TryParseDecimal(string? value, out System.IComparable comparable)
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

        return false;
    }
}
