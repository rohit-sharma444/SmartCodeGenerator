// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.BooleanRule
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using System;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
/// Represents Boolean Rule.
/// </summary>
  [Serializable]
  public class BooleanRule : Sage.Common.Rules.Rule
  {
    private bool _bIsTrue;

    public static BooleanRule Rule(bool condition) => new BooleanRule(condition);

    public static BooleanRule Rule(bool condition, object ruleIdentifier)
    {
      BooleanRule booleanRule = BooleanRule.Rule(condition);
      booleanRule.Identifier = ruleIdentifier;
      return booleanRule;
    }

    protected BooleanRule(bool condition) => this._bIsTrue = condition;

    protected override bool InternalIsTrue() => this._bIsTrue;

    protected override bool InternalIsFalse() => !this._bIsTrue;

    public override string ToString() => base.ToString() + " IS " + this._bIsTrue.ToString();
  }
}
