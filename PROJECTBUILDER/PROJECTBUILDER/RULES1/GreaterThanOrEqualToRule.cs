// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.GreaterThanOrEqualToRule
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using Sage.Common.ProgrammingByContract;
using System;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
/// Represents Greater Than Or Equal To Rule.
/// </summary>
  [Serializable]
  public class GreaterThanOrEqualToRule : Sage.Common.Rules.Rule
  {
    private IComparable _iValueToCheck = (IComparable) 0;
    private IComparable _iLowerBound = (IComparable) 0;

    public static GreaterThanOrEqualToRule Rule(IComparable value, IComparable lowerBound)
    {
      Contract.PreCond(value.GetType().Equals(lowerBound.GetType()), "The types of the parameters must be the same.");
      return new GreaterThanOrEqualToRule(value, lowerBound);
    }

    protected GreaterThanOrEqualToRule(IComparable value, IComparable lowerBound)
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

    protected override bool InternalIsTrue()
    {
      return this.ValueToCheck.CompareTo((object) this.LowerBound) >= 0;
    }

    protected override bool InternalIsFalse()
    {
      return this.ValueToCheck.CompareTo((object) this.LowerBound) < 0;
    }
  }
}
