using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class GreaterThanRule : IValidationRule
{
    private readonly string _p; private readonly decimal _v;
    public GreaterThanRule(string p, decimal v) { _p = p; _v = v; }
    public ValidationCategory Category => ValidationCategory.Data;
    public string Describe() => $"{_p} must be greater than {_v.ToString(CultureInfo.InvariantCulture)}";
    public void Emit(ValidationEmitContext ctx)
    {
        var v = _v.ToString(CultureInfo.InvariantCulture);
        ctx.Line($"if (decimal.TryParse(((object)this.{_p})?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var __n))");
        ctx.Line($"    if (__n <= {v}m) errors.Add(\"{_p} must be greater than {v}.\");");
    }
}

public sealed class GreaterThanRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+greater\s+than\s+(?<value>[-+]?[0-9]*\.?[0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public int Priority => 40;
    public ValidationCategory Category => ValidationCategory.Data;
    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new GreaterThanRule(m.Groups["prop"].Value,
                decimal.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture));
            return true;
        }
        rule = null; return false;
    }
}
