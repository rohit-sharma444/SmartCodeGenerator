using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class RequiredWhenRule : IValidationRule
{
    private readonly string _target; private readonly string _cond; private readonly string _value;
    public RequiredWhenRule(string target, string cond, string value)
    {
        _target = target; _cond = cond; _value = value;
    }

    public ValidationCategory Category => ValidationCategory.Business;
    public IReadOnlyList<string> Properties => [_target, _cond];
    public string Describe() => $"{_target} is required when {_cond} is {_value}";

    public void Emit(ValidationEmitContext ctx)
    {
        if (!ctx.HasProperty(_target) || !ctx.HasProperty(_cond))
        {
            ctx.Line($"// business rule skipped: '{Describe()}' references an unknown property");
            return;
        }

        var valueLiteral = SymbolDisplay.FormatLiteral(_value, quote: true);
        var messageLiteral = SymbolDisplay.FormatLiteral(Describe() + ".", quote: true);
        ctx.Line($"var __c = (object)this.{_cond};");
        ctx.Line($"if (__c != null && string.Equals(__c.ToString(), {valueLiteral}, System.StringComparison.OrdinalIgnoreCase))");
        ctx.Line("{");
        ctx.Line($"    var __t = (object)this.{_target};");
        ctx.Line("    if (__t == null || string.IsNullOrWhiteSpace(__t.ToString()))");
        ctx.Line($"        errors.Add({messageLiteral});");
        ctx.Line("}");
    }
}

public sealed class RequiredWhenRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<target>\w+)\s+is\s+required\s+when\s+(?<cond>\w+)\s+is\s+(?<val>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 10;
    public ValidationCategory Category => ValidationCategory.Business;

    public IReadOnlyList<string> Examples => ["ShippedDate is required when Status is Shipped"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new RequiredWhenRule(
                m.Groups["target"].Value, m.Groups["cond"].Value,
                m.Groups["val"].Value.Trim().Trim('\'', '"'));
            return true;
        }
        rule = null; return false;
    }
}
