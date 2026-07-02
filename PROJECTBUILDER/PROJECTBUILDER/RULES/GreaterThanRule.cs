namespace PROJECTBUILDER.RULES;
/// <summary>
/// Represents Greater Than Rule.
/// </summary>

public class GreaterThanRule : Rule
{
    private IComparable _iValueToCheck = (IComparable)0;
    private IComparable _iLowerBound = (IComparable)0;

    protected GreaterThanRule(IComparable value, IComparable lowerBound)
    {
        this.ValueToCheck = value;
        this.LowerBound = lowerBound;
    }

    private IComparable ValueToCheck
    {
        get => this._iValueToCheck;
        set => this._iValueToCheck = value;
    }

    private IComparable LowerBound
    {
        get => this._iLowerBound;
        set => this._iLowerBound = value;
    }

    public override bool IsTrue() => this.ValueToCheck.CompareTo((object)this.LowerBound) > 0;

    public override bool IsFalse() => this.ValueToCheck.CompareTo((object)this.LowerBound) <= 0;

    public string Name => "GreaterThanRule";
}
