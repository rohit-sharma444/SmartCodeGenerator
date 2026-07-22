using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class NumericRangeRule : IValidationRule
{
    private readonly string _p; private readonly decimal _min; private readonly decimal _max;
    public NumericRangeRule(string p, decimal min, decimal max) { _p = p; _min = min; _max = max; }
    public ValidationCategory Category => ValidationCategory.Data;
    public string Describe() =>
        $"{_p} must be between {_min.ToString(CultureInfo.InvariantCulture)} and {_max.ToString(CultureInfo.InvariantCulture)}";
    public void Emit(ValidationEmitContext ctx)
    {
        var min = _min.ToString(CultureInfo.InvariantCulture);
        var max = _max.ToString(CultureInfo.InvariantCulture);
        ctx.Line($"if (decimal.TryParse(((object)this.{_p})?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var __n))");
        ctx.Line("{");
        ctx.Line($"    if (__n < {min}m) errors.Add(\"{_p} must be >= {min}.\");");
        ctx.Line($"    if (__n > {max}m) errors.Add(\"{_p} must be <= {max}.\");");
        ctx.Line("}");
    }
}

public sealed class NumericRangeRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+between\s+(?<min>[-+]?[0-9]*\.?[0-9]+)\s+and\s+(?<max>[-+]?[0-9]*\.?[0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public int Priority => 30;
    public ValidationCategory Category => ValidationCategory.Data;
    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new NumericRangeRule(m.Groups["prop"].Value,
                decimal.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture),
                decimal.Parse(m.Groups["max"].Value, CultureInfo.InvariantCulture));
            return true;
        }
        rule = null; return false;
    }
}
