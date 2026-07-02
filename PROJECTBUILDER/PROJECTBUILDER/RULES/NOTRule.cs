namespace PROJECTBUILDER.RULES;
/// <summary>
/// Represents NOTRule.
/// </summary>

public class NOTRule : Rule
{
    private IRule _oRuleToNot;

    public static NOTRule Rule(IRule rule) => new NOTRule(rule);

    protected NOTRule(IRule rule) => this._oRuleToNot = rule;

    public override  bool IsTrue() => this._oRuleToNot.IsFalse();

    public override bool IsFalse() => this._oRuleToNot.IsTrue();

    public string Name => "NOTRule";
}
