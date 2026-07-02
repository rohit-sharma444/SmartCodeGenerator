namespace PROJECTBUILDER.RULES;
/// <summary>
/// Represents Less Than Equal To Rule.
/// </summary>

public class LessThanEqualToRule : Rule
{
    private IComparable _iValueToCheck = (IComparable)0;
    private IComparable _iUpperBound = (IComparable)0;

    protected LessThanEqualToRule(IComparable value, IComparable upperBound)
    {
        this.ValueToCheck = value;
        this.UpperBound = upperBound;
    }

    private IComparable ValueToCheck
    {
        get => this._iValueToCheck;
        set => this._iValueToCheck = value;
    }

    private IComparable UpperBound
    {
        get => this._iUpperBound;
        set => this._iUpperBound = value;
    }

    public override bool IsTrue() => this.ValueToCheck.CompareTo((object)this.UpperBound) <= 0;

    public override bool IsFalse() => this.ValueToCheck.CompareTo((object)this.UpperBound) > 0;

    public string Name => "LessThanEqualToRule";
}
