// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.RangeRule
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using Sage.Common.ProgrammingByContract;
using System;
using System.Text;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
/// Represents Range Rule.
/// </summary>
  [Serializable]
  public class RangeRule : Sage.Common.Rules.Rule
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
      Contract.PreCond(upperBound.GetType().Equals(lowerBound.GetType()), "The boundary parameters must be the same type.");
      Contract.PreCond(value.GetType().Equals(lowerBound.GetType()), "The value type doesn't match the boundary type.");
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

    protected override bool InternalIsTrue()
    {
      this.Reset();
      bool flag = true;
      if (this._oLowerBound.CompareTo((object) this._oValue) > 0)
      {
        this.SetFailureIndicator("below range");
        flag = false;
      }
      else if (this._oValue.CompareTo((object) this._oUpperBound) > 0)
      {
        this.SetFailureIndicator("above range");
        flag = false;
      }
      return flag;
    }

    protected override bool InternalIsFalse()
    {
      this.Reset();
      bool flag = true;
      if (this._oLowerBound.CompareTo((object) this._oValue) <= 0 && this._oValue.CompareTo((object) this._oUpperBound) <= 0)
      {
        this.SetFailureIndicator("in range");
        flag = false;
      }
      return flag;
    }

    public override string ToString()
    {
      StringBuilder stringBuilder = new StringBuilder();
      stringBuilder.Append((object) this._oLowerBound);
      stringBuilder.Append(" <= ");
      stringBuilder.Append((object) this._oValue);
      stringBuilder.Append(" <= ");
      stringBuilder.Append((object) this._oUpperBound);
      stringBuilder.Append(" ");
      return "RANGE RULE: " + (object) stringBuilder + base.ToString();
    }
  }
}
