using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class LengthRangeRule : IValidationRule
{
    private readonly string _p; private readonly int _min; private readonly int _max;
    public LengthRangeRule(string p, int min, int max) { _p = p; _min = min; _max = max; }
    public ValidationCategory Category => ValidationCategory.Data;
    public string Describe() => $"{_p} length must be between {_min} and {_max}";
    public void Emit(ValidationEmitContext ctx)
    {
        ctx.Line($"var __s = ((object)this.{_p})?.ToString();");
        ctx.Line($"if (__s != null && __s.Length < {_min}) errors.Add(\"{_p} minimum length is {_min}.\");");
        ctx.Line($"if (__s != null && __s.Length > {_max}) errors.Add(\"{_p} maximum length is {_max}.\");");
    }
}

public sealed class LengthRangeRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+length\s+must\s+be\s+between\s+(?<min>\d+)\s+and\s+(?<max>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public int Priority => 20;
    public ValidationCategory Category => ValidationCategory.Data;
    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new LengthRangeRule(m.Groups["prop"].Value,
                int.Parse(m.Groups["min"].Value), int.Parse(m.Groups["max"].Value));
            return true;
        }
        rule = null; return false;
    }
}
