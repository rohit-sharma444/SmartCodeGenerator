namespace PROJECTBUILDER.RULES;
/// <summary>
            /// Defines IFailureIndicator.
            /// </summary>

public interface IFailureIndicator
{
/// <summary>
/// Gets or sets Failure Reason.
/// </summary>
    string FailureReason { get; }
/// <summary>
/// Gets or sets Failure Exception.
/// </summary>

    Exception FailureException { get; }
}
/// <summary>
            /// Defines IRule.
            /// </summary>
public interface IRule
{
/// <summary>
/// Is True.
/// </summary>
/// <returns>bool</returns>

    bool IsTrue();
/// <summary>
/// Is False.
/// </summary>
/// <returns>bool</returns>

    bool IsFalse();
/// <summary>
/// Gets or sets Failure Indicator.
/// </summary>
    IFailureIndicator FailureIndicator { get; set; }
/// <summary>
/// Gets or sets Identifier.
/// </summary>
    object Identifier { get; set; }
/// <summary>
/// Gets or sets Name.
/// </summary>
    string Name { get; }
}
/// <summary>
/// Represents Rule.
/// </summary>

public abstract class Rule : IRule
{
    public virtual bool IsTrue() => throw new NotImplementedException();

    public virtual bool IsFalse() => throw new NotImplementedException();

    public object Identifier { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public string Name => "Rule";

    public void SetFailureIndicator(string failureReason)
    {
        if (this.FailureIndicator != null)
            return;
        this.FailureIndicator = (IFailureIndicator)new ExceptionFailureIndicator(failureReason);
    }

    private IFailureIndicator _oFailureIndicator;
    public IFailureIndicator FailureIndicator
    {
        get => this._oFailureIndicator;
        set
        {
            if (this.FailureIndicator != null)
                return;
            this._oFailureIndicator = value;
        }
    }
}
