namespace PROJECTBUILDER.RULES;
/// <summary>
/// Represents Boolean Rule.
/// </summary>

public class BooleanRule : Rule
{
    private bool _bIsTrue;

    public static BooleanRule Rule(bool condition) => new BooleanRule(condition);

    public static BooleanRule Rule(bool condition, object ruleIdentifier)
    {
        BooleanRule booleanRule = BooleanRule.Rule(condition);
        booleanRule.Identifier = ruleIdentifier;
        return booleanRule;
    }

    protected BooleanRule(bool condition) => this._bIsTrue = condition;

    public override bool IsTrue() => this._bIsTrue;

    public override bool IsFalse() => !this._bIsTrue;

    public string Name => "BooleanRule";
}