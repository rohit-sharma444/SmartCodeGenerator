using System.Linq;
using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class AllowedValuesRule : IValidationRule
{
    private readonly string _p; private readonly IReadOnlyList<string> _values;
    public AllowedValuesRule(string p, IReadOnlyList<string> values) { _p = p; _values = values; }
    public ValidationCategory Category => ValidationCategory.Data;
    public string Describe() => $"{_p} must be one of: {string.Join(", ", _values)}";
    public void Emit(ValidationEmitContext ctx)
    {
        var joined = string.Join(", ", _values.Select(v => "@\"" + v.Replace("\"", "\"\"") + "\""));
        ctx.Line($"var __s = ((object)this.{_p})?.ToString();");
        ctx.Line($"var __allowed = new[] {{ {joined} }};");
        ctx.Line("if (__s != null && !System.Array.Exists(__allowed, __a => string.Equals(__a, __s, System.StringComparison.OrdinalIgnoreCase)))");
        ctx.Line($"    errors.Add(\"{_p} contains an invalid value.\");");
    }
}

public sealed class AllowedValuesRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+(?<values>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public int Priority => 100; // catch-all: runs last
    public ValidationCategory Category => ValidationCategory.Data;
    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var raw = m.Groups["values"].Value;
        if (!raw.Contains(',') && !raw.Contains(" or ") && !raw.Contains(" and "))
            return false; // a single token is not an allowed-values list

        var parts = raw.Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(p => p.Split(new[] { " or ", " and " }, System.StringSplitOptions.RemoveEmptyEntries))
            .Select(x => x.Trim().Trim('"', '\''))
            .Where(x => x.Length > 0)
            .ToList();

        if (parts.Count == 0) return false;
        rule = new AllowedValuesRule(m.Groups["prop"].Value, parts);
        return true;
    }
}
