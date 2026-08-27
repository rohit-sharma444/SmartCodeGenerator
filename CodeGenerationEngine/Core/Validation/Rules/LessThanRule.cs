using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class LessThanRule : IValidationRule
{
    private readonly string _p; private readonly decimal _v;
    public LessThanRule(string p, decimal v) { _p = p; _v = v; }
    public ValidationCategory Category => ValidationCategory.Data;
    public IReadOnlyList<string> Properties => [_p];
    public string Describe() => $"{_p} must be less than {_v.ToString(CultureInfo.InvariantCulture)}";
    public void Emit(ValidationEmitContext ctx)
    {
        var v = _v.ToString(CultureInfo.InvariantCulture);
        ctx.Line($"if (decimal.TryParse(((object)this.{_p})?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var __n))");
        ctx.Line($"    if (__n >= {v}m) errors.Add(\"{_p} must be less than {v}.\");");
    }
}

public sealed class LessThanRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+less\s+than\s+(?<value>[-+]?[0-9]*\.?[0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public int Priority => 50;
    public ValidationCategory Category => ValidationCategory.Data;
    public IReadOnlyList<string> Examples => ["Discount must be less than 100"];
    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new LessThanRule(m.Groups["prop"].Value,
                decimal.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture));
            return true;
        }
        rule = null; return false;
    }
}
