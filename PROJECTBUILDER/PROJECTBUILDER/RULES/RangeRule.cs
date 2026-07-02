namespace PROJECTBUILDER.RULES;
/// <summary>
/// Represents Range Rule.
/// </summary>

public class RangeRule : Rule
{
    private const string TOO_SMALL = "below range";
    private const string TOO_LARGE = "above range";
    private const string IN_RANGE = "in range";
    private IComparable _oLowerBound;
    private IComparable _oUpperBound;
    private IComparable _oValue;

    public static RangeRule Rule(IComparable value, IComparable lowerBound, IComparable upperBound)
    {
        return new RangeRule(value, lowerBound, upperBound);
    }

    public static RangeRule Rule(
      IComparable value,
      IComparable lowerBound,
      IComparable upperBound,
      object ruleIdentifier)
    {
        RangeRule rangeRule = new RangeRule(value, lowerBound, upperBound);
        rangeRule.Identifier = ruleIdentifier;
        return rangeRule;
    }

    protected RangeRule(IComparable value, IComparable lowerBound, IComparable upperBound)
    {
        this._oValue = value;
        this._oLowerBound = lowerBound;
        this._oUpperBound = upperBound;
    }

    public override bool IsTrue()
    {
        bool flag = true;
        if (this._oLowerBound.CompareTo((object)this._oValue) > 0)
        {
            this.SetFailureIndicator("below range");
            flag = false;
        }
        else if (this._oValue.CompareTo((object)this._oUpperBound) > 0)
        {
            this.SetFailureIndicator("above range");
            flag = false;
        }
        return flag;
    }

    public override bool IsFalse()
    {
        bool flag = true;
        if (this._oLowerBound.CompareTo((object)this._oValue) <= 0 && this._oValue.CompareTo((object)this._oUpperBound) <= 0)
        {
            this.SetFailureIndicator("in range");
            flag = false;
        }
        return flag;
    }
    public string Name => "RangeRule";
}
