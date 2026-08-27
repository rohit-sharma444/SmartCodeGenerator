using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>Data rule: a number at or beyond a bound. The inclusive counterpart of
/// <see cref="GreaterThanRule"/> and <see cref="LessThanRule"/>, which are strict-only —
/// so "at least 18" had nowhere to go before this.</summary>
public sealed class InclusiveBoundRule : IValidationRule
{
    private readonly string _p;
    private readonly decimal _value;
    private readonly bool _atLeast;

    public InclusiveBoundRule(string property, decimal value, bool atLeast)
    {
        _p = property;
        _value = value;
        _atLeast = atLeast;
    }

    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Properties => [_p];

    public string Describe() =>
        $"{_p} must be {(_atLeast ? "at least" : "at most")} " +
        _value.ToString(CultureInfo.InvariantCulture);

    public void Emit(ValidationEmitContext ctx)
    {
        var value = _value.ToString(CultureInfo.InvariantCulture);
        var violation = _atLeast ? "<" : ">";
        var message = Microsoft.CodeAnalysis.CSharp.SymbolDisplay
            .FormatLiteral(Describe() + ".", quote: true);

        ctx.Line($"if (decimal.TryParse(((object)this.{_p})?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var __n))");
        ctx.Line($"    if (__n {violation} {value}m) errors.Add({message});");
    }
}

public sealed class MinValueRuleParser : IValidationRuleParser
{
    // Anchored on the number, so "at most 50 characters" falls through to the length parsers.
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+(?:at\s+least|no\s+less\s+than|greater\s+than\s+or\s+equal\s+to)\s+(?<value>[-+]?[0-9]*\.?[0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 35;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
        ["Age must be at least 18", "Age must be greater than or equal to 18"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new InclusiveBoundRule(
            m.Groups["prop"].Value,
            decimal.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture),
            atLeast: true);
        return true;
    }
}

public sealed class MaxValueRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+(?:be\s+(?:at\s+most|no\s+more\s+than|less\s+than\s+or\s+equal\s+to)|not\s+exceed)\s+(?<value>[-+]?[0-9]*\.?[0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 35;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
        ["Salary must be at most 100000", "Salary must not exceed 100000"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new InclusiveBoundRule(
            m.Groups["prop"].Value,
            decimal.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture),
            atLeast: false);
        return true;
    }
}
