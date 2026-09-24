using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>Business rule: one property has to equal an arithmetic combination of two others.</summary>
/// <remarks>
/// Compared as decimals with a small tolerance rather than for exact equality: the operands may be
/// doubles, and a rule that fires because 0.1 + 0.2 is not 0.3 is a rule people switch off.
/// </remarks>
public sealed class ArithmeticRule : IValidationRule
{
    private readonly string _target;
    private readonly string _left;
    private readonly string _op;      // the word, as typed
    private readonly string _right;

    public ArithmeticRule(string target, string left, string op, string right)
    {
        _target = target;
        _left = left;
        _op = op;
        _right = right;
    }

    public ValidationCategory Category => ValidationCategory.Business;

    public IReadOnlyList<string> Properties => [_target, _left, _right];

    /// <summary>The operator word, as typed — what it takes to build this rule again, with
    /// <see cref="Properties"/>.</summary>
    public string Operator => _op;

    public string Describe() => $"{_target} must equal {_left} {_op} {_right}";

    public void Emit(ValidationEmitContext ctx)
    {
        if (!ctx.HasProperty(_target) || !ctx.HasProperty(_left) || !ctx.HasProperty(_right))
        {
            ctx.Line($"// business rule skipped: '{Describe()}' references an unknown property");
            return;
        }

        var symbol = _op.ToLowerInvariant() switch
        {
            "times" or "multiplied by" => "*",
            "plus" => "+",
            "minus" => "-",
            "divided by" => "/",
            _ => throw new System.InvalidOperationException($"Unhandled arithmetic operator: {_op}")
        };

        var message = SymbolDisplay.FormatLiteral(Describe() + ".", quote: true);
        const string Parse = "System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture";

        ctx.Line($"if (decimal.TryParse(((object)this.{_target})?.ToString(), {Parse}, out var __t)");
        ctx.Line($"    && decimal.TryParse(((object)this.{_left})?.ToString(), {Parse}, out var __l)");
        ctx.Line($"    && decimal.TryParse(((object)this.{_right})?.ToString(), {Parse}, out var __r))");
        ctx.Line("{");

        if (symbol == "/")
        {
            // A zero divisor makes the identity uncomputable, not violated: this check stays
            // silent rather than reporting an arithmetic mismatch that would misdiagnose the real
            // problem (the divisor being zero, not the target). A complaint about a zero divisor
            // belongs in its own rule, not this one.
            ctx.Line("    // skipped when the divisor is 0: uncomputable, not a violation");
            ctx.Line($"    if (__r != 0m && System.Math.Abs(__t - (__l / __r)) > 0.0001m) errors.Add({message});");
        }
        else
            ctx.Line($"    if (System.Math.Abs(__t - (__l {symbol} __r)) > 0.0001m) errors.Add({message});");

        ctx.Line("}");
    }
}

public sealed class ArithmeticRuleParser : IValidationRuleParser
{
    private static readonly Regex Pattern = new(
        @"^(?<target>\w+)\s+must\s+equal\s+(?<left>\w+)\s+(?<op>times|multiplied\s+by|plus|minus|divided\s+by)\s+(?<right>\w+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 15;
    public ValidationCategory Category => ValidationCategory.Business;

    public IReadOnlyList<string> Examples =>
        ["Total must equal Quantity times Price", "Net must equal Gross minus Tax"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        rule = new ArithmeticRule(
            m.Groups["target"].Value,
            m.Groups["left"].Value,
            Regex.Replace(m.Groups["op"].Value.Trim(), @"\s+", " ").ToLowerInvariant(),
            m.Groups["right"].Value);
        return true;
    }
}
