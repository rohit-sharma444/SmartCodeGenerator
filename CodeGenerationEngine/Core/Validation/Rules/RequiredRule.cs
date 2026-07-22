using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core.Validation.Rules;

/// <summary>Data rule: a property must not be null/blank. First rule type, and the
/// minimum needed to prove the catalog + registry + emitter pipeline end-to-end.</summary>
public sealed class RequiredRule : IValidationRule
{
    private readonly string _property;
    public RequiredRule(string property) => _property = property;

    public ValidationCategory Category => ValidationCategory.Data;
    public string Describe() => $"{_property} is required";

    public void Emit(ValidationEmitContext ctx)
    {
        ctx.Line($"var __v = (object)this.{_property};");
        ctx.Line("if (__v == null || string.IsNullOrWhiteSpace(__v.ToString()))");
        ctx.Line($"    errors.Add(\"{_property} is required.\");");
    }
}

public sealed class RequiredRuleParser : IValidationRuleParser
{
    // "<prop> is required" plus the blank/empty/null phrasings the command catalog
    // documented before the rule registry replaced the original if-chain.
    private static readonly Regex Pattern =
        new(@"^(?<prop>\w+)\s+(?:is\s+required$"
          + @"|(?:is|should|shall|must|can)\s*(?:not|n't)\s+be\s+(?:blank|empty|null)$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Priority => 10;
    public ValidationCategory Category => ValidationCategory.Data;

    public bool TryParse(ValidationParseContext ctx, out IValidationRule? rule)
    {
        var m = Pattern.Match(ctx.Fragment);
        if (m.Success)
        {
            rule = new RequiredRule(m.Groups["prop"].Value);
            return true;
        }
        rule = null;
        return false;
    }
}
