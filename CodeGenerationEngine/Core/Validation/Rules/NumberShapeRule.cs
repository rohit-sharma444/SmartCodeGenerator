using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>What shape a number has to be, as opposed to what value it has to be near.</summary>
public enum NumberShape
{
    Positive,
    Negative,
    NonNegative,
    NonZero,
    Integral,
    Numeric
}

/// <summary>Data rule: sign, zero, and whether the value is a number at all.</summary>
public sealed class NumberShapeRule : IValidationRule
{
    private readonly string _p;
    private readonly NumberShape _shape;

    public NumberShapeRule(string property, NumberShape shape)
    {
        _p = property;
        _shape = shape;
    }

    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Properties => [_p];

    public string Describe() => _shape switch
    {
        NumberShape.Positive => $"{_p} must be positive",
        NumberShape.Negative => $"{_p} must be negative",
        NumberShape.NonNegative => $"{_p} must not be negative",
        NumberShape.NonZero => $"{_p} must not be zero",
        NumberShape.Integral => $"{_p} must be a whole number",
        _ => $"{_p} must be numeric"
    };

    public void Emit(ValidationEmitContext ctx)
    {
        var message = Microsoft.CodeAnalysis.CSharp.SymbolDisplay
            .FormatLiteral(Describe() + ".", quote: true);

        ctx.Line($"var __t = ((object)this.{_p})?.ToString();");
        ctx.Line("var __parsed = decimal.TryParse(__t, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var __n);");

        // "Numeric" is the one shape where failing to parse IS the violation. Every other shape
        // is a statement about a number, so a value that is not one is left to the rule whose
        // job that is — two rules on one property is how you say both things.
        if (_shape == NumberShape.Numeric)
        {
            ctx.Line($"if (!string.IsNullOrWhiteSpace(__t) && !__parsed) errors.Add({message});");
            return;
        }

        var violation = _shape switch
        {
            NumberShape.Positive => "__n <= 0m",
            NumberShape.Negative => "__n >= 0m",
            NumberShape.NonNegative => "__n < 0m",
            NumberShape.NonZero => "__n == 0m",
            _ => "decimal.Truncate(__n) != __n"
        };

        ctx.Line($"if (__parsed && {violation}) errors.Add({message});");
    }
}

public sealed class SignRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+(?<neg>not\s+)?be\s+(?<shape>positive|negative|zero\s+or\s+greater|zero)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 36;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
    [
        "Price must be positive",
        "Quantity must not be negative",
        "Total must not be zero"
    ];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var negated = m.Groups["neg"].Success;
        var shape = Regex.Replace(m.Groups["shape"].Value.Trim(), @"\s+", " ").ToLowerInvariant();

        var resolved = (shape, negated) switch
        {
            ("positive", false) => NumberShape.Positive,
            ("negative", false) => NumberShape.Negative,
            ("negative", true) => NumberShape.NonNegative,
            ("zero or greater", false) => NumberShape.NonNegative,
            ("zero", true) => NumberShape.NonZero,
            _ => (NumberShape?)null
        };

        if (resolved is null) return false;

        rule = new NumberShapeRule(m.Groups["prop"].Value, resolved.Value);
        return true;
    }
}

public sealed class NumberFormRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+(?:a\s+whole\s+number|an\s+integer|numeric|a\s+number)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 37;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
        ["Quantity must be a whole number", "Amount must be numeric"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var whole = ctx.Fragment.Contains("whole", System.StringComparison.OrdinalIgnoreCase)
                 || ctx.Fragment.Contains("integer", System.StringComparison.OrdinalIgnoreCase);

        rule = new NumberShapeRule(
            m.Groups["prop"].Value, whole ? NumberShape.Integral : NumberShape.Numeric);
        return true;
    }
}
