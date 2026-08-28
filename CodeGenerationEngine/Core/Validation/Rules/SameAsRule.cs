using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>Business rule: two properties have to agree, or have to differ.</summary>
public sealed class SameAsRule : IValidationRule
{
    private readonly string _a;
    private readonly string _b;
    private readonly bool _same;

    public SameAsRule(string a, string b, bool same)
    {
        _a = a;
        _b = b;
        _same = same;
    }

    public ValidationCategory Category => ValidationCategory.Business;

    public IReadOnlyList<string> Properties => [_a, _b];

    public string Describe() =>
        _same ? $"{_a} must be the same as {_b}" : $"{_a} must be different from {_b}";

    public void Emit(ValidationEmitContext ctx)
    {
        if (!ctx.HasProperty(_a) || !ctx.HasProperty(_b))
        {
            ctx.Line($"// business rule skipped: '{Describe()}' references an unknown property");
            return;
        }

        var message = SymbolDisplay.FormatLiteral(Describe() + ".", quote: true);

        ctx.Line($"var __a = ((object)this.{_a})?.ToString();");
        ctx.Line($"var __b = ((object)this.{_b})?.ToString();");
        ctx.Line($"var __equal = string.Equals(__a, __b, System.StringComparison.Ordinal);");
        ctx.Line($"if ({(_same ? "!__equal" : "__equal")}) errors.Add({message});");
    }
}

public sealed class SameAsRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<a>\w+)\s+must\s+(?<neg>not\s+)?(?:be\s+the\s+same\s+as|equal|be\s+(?<diff>different\s+from))\s+(?<b>\w+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 16;
    public ValidationCategory Category => ValidationCategory.Business;

    public IReadOnlyList<string> Examples =>
    [
        "BillingPostcode must be the same as ShippingPostcode",
        "BillingPostcode must be different from ShippingPostcode"
    ];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var same = !m.Groups["neg"].Success && !m.Groups["diff"].Success;

        rule = new SameAsRule(m.Groups["a"].Value, m.Groups["b"].Value, same);
        return true;
    }
}
