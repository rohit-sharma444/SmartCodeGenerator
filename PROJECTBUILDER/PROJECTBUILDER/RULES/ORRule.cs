namespace PROJECTBUILDER.RULES;
/// <summary>
/// Represents ORRule.
/// </summary>

public class ORRule : CombinativeRule
{
    public static ORRule Rule(params IRule[] rules) => new ORRule(rules);

    public static ORRule Rule(object identifier, params IRule[] rules)
    {
        ORRule orRule = new ORRule(rules);
        orRule.Identifier = identifier;
        return orRule;
    }

    protected ORRule(params IRule[] rules)
      : base(rules)
    {
    }

    public override bool IsTrue()
    {
        bool flag = false;
        if (this.RuleList.Count == 0)
            return false;
        foreach (IRule rule in this.RuleList)
        {
            this.TerminatingRule = rule;
            if (rule.IsTrue())
            {
                flag = true;
                break;
            }
        }
        return flag;
    }

    public override bool IsFalse()
    {
        bool flag = true;
        if (this.RuleList.Count == 0)
            return true;
        foreach (IRule rule in this.RuleList)
        {
            this.TerminatingRule = rule;
            if (!rule.IsFalse())
            {
                flag = false;
                this.FailureIndicator = rule.FailureIndicator;
                break;
            }
        }
        return flag;
    }

    public string Name => "ORRule";
}
