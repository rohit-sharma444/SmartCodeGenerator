// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.NOTRule
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using System;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
/// Represents NOTRule.
/// </summary>
  [Serializable]
  public class NOTRule : Sage.Common.Rules.Rule
  {
    private IRule _oRuleToNot;

    public static NOTRule Rule(IRule rule) => new NOTRule(rule);

    protected NOTRule(IRule rule) => this._oRuleToNot = rule;

    protected override bool InternalIsTrue()
    {
      this.Reset();
      return this._oRuleToNot.IsFalse;
    }

    protected override bool InternalIsFalse()
    {
      this.Reset();
      return this._oRuleToNot.IsTrue;
    }

    public override string ToString()
    {
      return "NOT (" + (object) this._oRuleToNot + ")" + base.ToString();
    }
  }
}
