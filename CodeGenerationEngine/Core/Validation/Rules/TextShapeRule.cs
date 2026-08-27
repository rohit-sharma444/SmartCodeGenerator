using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Validation.Rules;

public enum TextShape
{
    StartsWith,
    EndsWith,
    Contains,
    DoesNotContain,
    Uppercase,
    Lowercase,
    Digits,
    Letters,
    Alphanumeric
}

/// <summary>Data rule: what a string looks like, short of a full regex.</summary>
/// <remarks>
/// These describe themselves in the words that were typed rather than as the pattern they
/// compile to — unlike the named formats, which are openly sugar over <see cref="RegexRule"/>.
/// "Sku must be digits only" is what someone wants to read back, not <c>^[0-9]+$</c>.
/// </remarks>
public sealed class TextShapeRule : IValidationRule
{
    private readonly string _p;
    private readonly TextShape _shape;
    private readonly string _operand;

    public TextShapeRule(string property, TextShape shape, string operand = "")
    {
        _p = property;
        _shape = shape;
        _operand = operand;
    }

    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Properties => [_p];

    public string Describe() => _shape switch
    {
        TextShape.StartsWith => $"{_p} must start with {_operand}",
        TextShape.EndsWith => $"{_p} must end with {_operand}",
        TextShape.Contains => $"{_p} must contain {_operand}",
        TextShape.DoesNotContain => $"{_p} must not contain {_operand}",
        TextShape.Uppercase => $"{_p} must be uppercase",
        TextShape.Lowercase => $"{_p} must be lowercase",
        TextShape.Digits => $"{_p} must be digits only",
        TextShape.Letters => $"{_p} must be letters only",
        TextShape.Alphanumeric => $"{_p} must be alphanumeric",
        _ => throw new System.ArgumentOutOfRangeException(nameof(_shape), _shape, "Unhandled TextShape.")
    };

    public void Emit(ValidationEmitContext ctx)
    {
        var message = SymbolDisplay.FormatLiteral(Describe() + ".", quote: true);
        var operand = SymbolDisplay.FormatLiteral(_operand, quote: true);

        ctx.Line($"var __s = ((object)this.{_p})?.ToString();");

        var violation = _shape switch
        {
            TextShape.StartsWith => $"!__s.StartsWith({operand}, System.StringComparison.Ordinal)",
            TextShape.EndsWith => $"!__s.EndsWith({operand}, System.StringComparison.Ordinal)",
            TextShape.Contains => $"!__s.Contains({operand}, System.StringComparison.Ordinal)",
            TextShape.DoesNotContain => $"__s.Contains({operand}, System.StringComparison.Ordinal)",
            TextShape.Uppercase => "__s != __s.ToUpperInvariant()",
            TextShape.Lowercase => "__s != __s.ToLowerInvariant()",
            TextShape.Digits => "!System.Text.RegularExpressions.Regex.IsMatch(__s, @\"^[0-9]+$\")",
            TextShape.Letters => "!System.Text.RegularExpressions.Regex.IsMatch(__s, @\"^[A-Za-z]+$\")",
            TextShape.Alphanumeric => "!System.Text.RegularExpressions.Regex.IsMatch(__s, @\"^[A-Za-z0-9]+$\")",
            _ => throw new System.ArgumentOutOfRangeException(nameof(_shape), _shape, "Unhandled TextShape.")
        };

        // A blank value is the required rule's business, not this one's. Two rules on one property
        // is how you say "present, and shaped like this".
        ctx.Line($"if (!string.IsNullOrEmpty(__s) && {violation}) errors.Add({message});");
    }
}

/// <summary>Shared by the parsers that take a quoted or bare operand.</summary>
internal static class TextOperand
{
    public static string Unquote(string raw) => raw.Trim().Trim('"', '\'');
}

/// <remarks>
/// Sits at priority 62 alongside <see cref="ContainsRuleParser"/>, but the two patterns are
/// disjoint on the verb phrase — "start with"/"end with" here, "contain" there — so no sentence
/// can ever match both. The shared priority never has to break a tie.
/// </remarks>
public sealed class AffixRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+(?<edge>start|end)\s+with\s+(?<text>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 62;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
        ["Sku must start with \"AB\"", "Sku must end with \"Z\""];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var shape = m.Groups["edge"].Value.Equals("start", System.StringComparison.OrdinalIgnoreCase)
            ? TextShape.StartsWith
            : TextShape.EndsWith;

        rule = new TextShapeRule(
            m.Groups["prop"].Value, shape, TextOperand.Unquote(m.Groups["text"].Value));
        return true;
    }
}

/// <remarks>
/// Sits at priority 62 alongside <see cref="AffixRuleParser"/>, but the two patterns are
/// disjoint on the verb phrase — "contain" here, "start with"/"end with" there — so no sentence
/// can ever match both. The shared priority never has to break a tie.
/// </remarks>
public sealed class ContainsRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+(?<neg>not\s+)?contain\s+(?<text>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 62;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
        ["Sku must contain \"-\"", "Notes must not contain \"TODO\""];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var shape = m.Groups["neg"].Success ? TextShape.DoesNotContain : TextShape.Contains;

        rule = new TextShapeRule(
            m.Groups["prop"].Value, shape, TextOperand.Unquote(m.Groups["text"].Value));
        return true;
    }
}

public sealed class CaseRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+(?<case>uppercase|lowercase|upper\s+case|lower\s+case)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 63;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples => ["Code must be uppercase", "Slug must be lowercase"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var upper = m.Groups["case"].Value.StartsWith("upper", System.StringComparison.OrdinalIgnoreCase);

        rule = new TextShapeRule(
            m.Groups["prop"].Value, upper ? TextShape.Uppercase : TextShape.Lowercase);
        return true;
    }
}

public sealed class CharacterClassRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<prop>\w+)\s+must\s+be\s+(?<class>digits\s+only|numbers\s+only|letters\s+only|alphanumeric)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 64;
    public ValidationCategory Category => ValidationCategory.Data;

    public IReadOnlyList<string> Examples =>
    [
        "Pin must be digits only",
        "Surname must be letters only",
        "Code must be alphanumeric"
    ];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var raw = Regex.Replace(m.Groups["class"].Value.Trim(), @"\s+", " ").ToLowerInvariant();

        var shape = raw switch
        {
            "digits only" or "numbers only" => TextShape.Digits,
            "letters only" => TextShape.Letters,
            "alphanumeric" => TextShape.Alphanumeric,
            _ => throw new System.ArgumentOutOfRangeException(nameof(raw), raw, "Unhandled character class.")
        };

        rule = new TextShapeRule(m.Groups["prop"].Value, shape);
        return true;
    }
}
