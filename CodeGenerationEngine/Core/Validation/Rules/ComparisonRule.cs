using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

public sealed class ComparisonRule : IValidationRule
{
    private readonly string _left;
    private readonly string _right;
    private readonly string _violationOp;    // the CompareTo(...) comparison that means "violated"
    private readonly string _original;       // the verbatim parsed fragment, for Describe()

    public ComparisonRule(string left, string right, string violationOp, string original)
    {
        _left = left; _right = right; _violationOp = violationOp; _original = original;
    }

    public ValidationCategory Category => ValidationCategory.Business;
    public IReadOnlyList<string> Properties => [_left, _right];
    // Echo the original fragment verbatim rather than reconstructing it: reconstructing
    // via "{left} must be {phrase} {right}" is ungrammatical for phrases like "not exceed"
    // (produces "must be not exceed"), and this text is user-visible via `show validation`
    // and the runtime error message.
    public string Describe() => _original;

    public void Emit(ValidationEmitContext ctx)
    {
        if (!ctx.HasProperty(_left) || !ctx.HasProperty(_right))
        {
            ctx.Line($"// business rule skipped: '{Describe()}' references an unknown property");
            return;
        }

        ctx.Line($"var __l = (object)this.{_left};");
        ctx.Line($"var __r = (object)this.{_right};");
        ctx.Line($"if (__l is System.IComparable __lc && __r != null && __lc.CompareTo(__r) {_violationOp})");
        ctx.Line($"    errors.Add(\"{Describe()}.\");");
    }
}

public sealed class ComparisonRuleParser : IValidationRuleParser
{
    // <right> refuses an all-digit operand: it names a property, and "at least 18" is a bound on a
    // number, which MinValueRuleParser owns on the data side.
    private static readonly Regex Pattern = new(
        @"^(?<left>\w+)\s+must\s+(?:be\s+)?(?<op>after|before|greater\s+than\s+or\s+equal\s+to|less\s+than\s+or\s+equal\s+to|greater\s+than|less\s+than|at\s+least|at\s+most|not\s+exceed)\s+(?<right>(?!\d+$)\w+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 20;
    public ValidationCategory Category => ValidationCategory.Business;

    public IReadOnlyList<string> Examples =>
        ["EndDate must be after StartDate", "Discount must not exceed Total"];

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        rule = null;
        if (!m.Success) return false;

        var op = Regex.Replace(m.Groups["op"].Value.Trim(), @"\s+", " ").ToLowerInvariant();
        // Violation operator: the CompareTo(left,right) result that means the rule is broken.
        var violation = op switch
        {
            "after" or "greater than" => "<= 0",
            "before" or "less than" => ">= 0",
            "at least" or "greater than or equal to" => "< 0",
            "at most" or "less than or equal to" or "not exceed" => "> 0",
            _ => "<= 0"
        };

        rule = new ComparisonRule(
            m.Groups["left"].Value, m.Groups["right"].Value, violation, ctx.Fragment);
        return true;
    }
}
