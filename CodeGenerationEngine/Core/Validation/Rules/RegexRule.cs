using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class RegexRule : IValidationRule
{
    private readonly string _p; private readonly string _pattern;
    public RegexRule(string p, string pattern) { _p = p; _pattern = pattern; }
    public ValidationCategory Category => ValidationCategory.Data;
    public string Describe() => $"{_p} must match {_pattern}";
    public void Emit(ValidationEmitContext ctx)
    {
        var escaped = _pattern.Replace("\"", "\"\""); // for a verbatim @"..." literal
        ctx.Line($"var __s = ((object)this.{_p})?.ToString();");
        ctx.Line($"if (__s != null && !System.Text.RegularExpressions.Regex.IsMatch(__s, @\"{escaped}\"))");
        ctx.Line($"    errors.Add(\"{_p} format is invalid.\");");
    }
}

public sealed class EmailRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+(?:a\s+)?valid\s+email",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public int Priority => 60;
    public ValidationCategory Category => ValidationCategory.Data;
    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new RegexRule(m.Groups["prop"].Value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
            return true;
        }
        rule = null; return false;
    }
}

public sealed class MatchRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+match\s+(?<pattern>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public int Priority => 61;
    public ValidationCategory Category => ValidationCategory.Data;
    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new RegexRule(m.Groups["prop"].Value,
                m.Groups["pattern"].Value.Trim().Trim('\'', '"'));
            return true;
        }
        rule = null; return false;
    }
}
