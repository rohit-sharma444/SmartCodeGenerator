// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.LessThanOrEqualToRule
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using Sage.Common.ProgrammingByContract;
using System;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
/// Represents Less Than Or Equal To Rule.
/// </summary>
  [Serializable]
  public class LessThanOrEqualToRule : Sage.Common.Rules.Rule
  {
    private IComparable _iValueToCheck = (IComparable) 0;
    private IComparable _iUpperBound = (IComparable) 0;

    public static LessThanOrEqualToRule Rule(IComparable value, IComparable upperBound)
    {
      Contract.PreCond(value.GetType().Equals(upperBound.GetType()), "The types of the parameters must be the same.");
      return new LessThanOrEqualToRule(value, upperBound);
    }

    protected LessThanOrEqualToRule(IComparable value, IComparable upperBound)
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

    protected override bool InternalIsTrue()
    {
      return this.ValueToCheck.CompareTo((object) this.UpperBound) <= 0;
    }

    protected override bool InternalIsFalse()
    {
      return this.ValueToCheck.CompareTo((object) this.UpperBound) > 0;
    }
  }
}
