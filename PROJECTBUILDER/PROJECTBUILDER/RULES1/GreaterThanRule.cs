using System;

namespace PROJECTBUILDER.RULES;
/// <summary>
/// Represents Greater Than Rule.
/// </summary>

public class GreaterThanRule : Rule
{
private IComparable _iValueToCheck = (IComparable) 0;
private IComparable _iLowerBound = (IComparable) 0;

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

protected override bool IsTrue()
{
  return this.ValueToCheck.CompareTo((object) this.LowerBound) > 0;
}

protected override bool IsFalse()
{
  return this.ValueToCheck.CompareTo((object) this.LowerBound) <= 0;
}
}
