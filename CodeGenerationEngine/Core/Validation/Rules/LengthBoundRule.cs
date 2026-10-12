using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public enum LengthBound
{
    Min,
    Max,
    Exact
}

/// <summary>Data rule: one edge of a string's length. The standalone counterpart of
/// <see cref="LengthRangeRule"/>, which needs both edges named.</summary>
public sealed class LengthBoundRule : IValidationRule
{
    private readonly string _p;
    private readonly int _length;
    private readonly LengthBound _bound;

    public LengthBoundRule(string property, int length, LengthBound bound)
    {
        _p = property;
        _length = length;
        _bound = bound;
    }

    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Properties => [_p];

    public string Describe() => _bound switch
    {
        LengthBound.Min => $"{_p} must be at least {_length} characters",
        LengthBound.Max => $"{_p} must be at most {_length} characters",
        LengthBound.Exact => $"{_p} must be exactly {_length} characters",
        _ => throw new System.ArgumentOutOfRangeException(nameof(_bound))
    };

    public void Emit(ValidationEmitContext ctx)
    {
        var violation = _bound switch
        {
            LengthBound.Min => $"__s.Length < {_length}",
            LengthBound.Max => $"__s.Length > {_length}",
            LengthBound.Exact => $"__s.Length != {_length}",
            _ => throw new System.ArgumentOutOfRangeException(nameof(_bound))
        };

        var message = Microsoft.CodeAnalysis.CSharp.SymbolDisplay
            .FormatLiteral(Describe() + ".", quote: true);

        ctx.Line($"var __s = ((object)this.{_p})?.ToString();");
        ctx.Line($"if (__s != null && {violation}) errors.Add({message});");
    }
}

public sealed class MinLengthRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+(?:must\s+be\s+|is\s+)?(?:at\s+least|no\s+shorter\s+than)\s+(?<n>\d+)\s+characters?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 18;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples => ["Name must be at least 2 characters"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new LengthBoundRule(
            m.Groups["prop"].Value, int.Parse(m.Groups["n"].Value), LengthBound.Min);
        return true;
    }
}

public sealed class MaxLengthRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+(?:(?:must\s+be\s+|is\s+)?(?:at\s+most|no\s+longer\s+than)|must\s+not\s+exceed)\s+(?<n>\d+)\s+characters?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 18;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
        ["Name must be at most 50 characters", "Name must not exceed 50 characters"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new LengthBoundRule(
            m.Groups["prop"].Value, int.Parse(m.Groups["n"].Value), LengthBound.Max);
        return true;
    }
}

public sealed class ExactLengthRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+exactly\s+(?<n>\d+)\s+characters?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 18;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples => ["Code must be exactly 8 characters"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new LengthBoundRule(
            m.Groups["prop"].Value, int.Parse(m.Groups["n"].Value), LengthBound.Exact);
        return true;
    }
}
