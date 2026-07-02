using System;
using System.Collections;

namespace PROJECTBUILDER.RULES;
/// <summary>
/// Represents Combinative Rule.
/// </summary>

public abstract class CombinativeRule : Rule
{
    protected ArrayList RuleList;
    protected IRule TerminatingRule;

    protected CombinativeRule(params IRule[] rules)
    {
        this.RuleList = new ArrayList();
        foreach (object rule in rules)
            this.RuleList.Add(rule);
    }

    public void Add(IRule rule) => this.RuleList.Add((object)rule);

    public string Name => "CombinativeRule";
}
