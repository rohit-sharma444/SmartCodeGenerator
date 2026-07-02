using PROJECTBUILDER.RULES;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents Rule Failure Exception.
/// </summary>

public class RuleFailureException : ApplicationException
{
    private object _oFailureIndicator;
    private object _oRuleIdentifier;

    public static RuleFailureException Create(IRule rule)
    {
        RuleFailureException failureException;
        if (rule != null)
        {
            failureException = new RuleFailureException("INVALID RULE: rule failed ->" + rule);
            failureException.FailureIndicator = rule.FailureIndicator;
            failureException.RuleIdentifier = rule.Identifier;
        }
        else
            failureException = new RuleFailureException("INVALID RULE: rule is NULL");
        return failureException;
    }

    public RuleFailureException()
    {
    }

    public RuleFailureException(string str)
      : base(str)
    {
    }

    public RuleFailureException(string str, Exception exp)
      : base(str, exp)
    {
    }

    public object RuleIdentifier
    {
        get => _oRuleIdentifier;
        set => _oRuleIdentifier = value;
    }

    public object FailureIndicator
    {
        get => _oFailureIndicator;
        set => _oFailureIndicator = value;
    }
}
