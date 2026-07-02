namespace PROJECTBUILDER.RULES;
/// <summary>
/// Represents Less Than Rule.
/// </summary>

public sealed class LessThanRule : Rule
{
    private static IComparable _iValueToCheck = (IComparable)0;
    private static IComparable _iUpperBound = (IComparable)0;

    protected LessThanRule(IComparable value, IComparable upperBound)
    {
        ValueToCheck = value;
        UpperBound = upperBound;
    }

    private static IComparable ValueToCheck
    {
        get => _iValueToCheck;
        set => _iValueToCheck = value;
    }

    private static IComparable UpperBound
    {
        get => _iUpperBound;
        set => _iUpperBound = value;
    }

    public static bool IsTrue() => ValueToCheck.CompareTo((object)UpperBound) < 0;

    public static bool IsFalse() => ValueToCheck.CompareTo((object)UpperBound) >= 0;

    public static string Name => "LessThanRule";
}
