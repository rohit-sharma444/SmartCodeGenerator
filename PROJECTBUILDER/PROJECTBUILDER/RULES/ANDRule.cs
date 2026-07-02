namespace PROJECTBUILDER.RULES;
/// <summary>
/// Represents ANDRule.
/// </summary>

public class ANDRule : CombinativeRule
{
    public static ANDRule Rule(params IRule[] rules) => new ANDRule(rules);

    public static ANDRule Rule(object identifier, params IRule[] rules)
    {
        ANDRule andRule = new ANDRule(rules);
        andRule.Identifier = identifier;
        return andRule;
    }

    protected ANDRule(params IRule[] rules) : base(rules) { }

    public override bool IsTrue()
    {
        bool flag = true;

        if (this.RuleList.Count == 0) return false;

        foreach (IRule rule in this.RuleList)
        {
            this.TerminatingRule = rule;
            if (!rule.IsTrue())
            {
                flag = false;
                this.FailureIndicator = rule.FailureIndicator;
                break;
            }
        }
        return flag;
    }

    public override bool IsFalse()
    {
        bool flag = false;

        if (this.RuleList.Count == 0) return true;

        foreach (IRule rule in this.RuleList)
        {
            this.TerminatingRule = rule;
            if (rule.IsFalse())
            {
                flag = true;
                break;
            }
        }
        return flag;
    }

    public string Name => "ANDRule";
}
