using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>Business rule: at least one of two properties has to carry a value.</summary>
public sealed class EitherOrRule : IValidationRule
{
    private readonly string _a;
    private readonly string _b;

    public EitherOrRule(string a, string b)
    {
        _a = a;
        _b = b;
    }

    public ValidationCategory Category => ValidationCategory.Business;

    public IReadOnlyList<string> Properties => [_a, _b];

    public string Describe() => $"Either {_a} or {_b} must be provided";

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
        ctx.Line($"if (string.IsNullOrWhiteSpace(__a) && string.IsNullOrWhiteSpace(__b)) errors.Add({message});");
    }
}

public sealed class EitherOrRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^either\s+(?<a>\w+)\s+or\s+(?<b>\w+)\s+must\s+be\s+(?:provided|present|supplied|set)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 12;
    public ValidationCategory Category => ValidationCategory.Business;

    public IReadOnlyList<string> Examples => ["Either Email or Phone must be provided"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new EitherOrRule(m.Groups["a"].Value, m.Groups["b"].Value);
        return true;
    }
}
