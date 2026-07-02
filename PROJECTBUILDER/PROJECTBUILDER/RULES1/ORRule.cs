// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.ORRule
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using System;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
/// Represents ORRule.
/// </summary>
  [Serializable]
  public class ORRule : CombinativeRule
  {
    public static ORRule Rule(params IRule[] rules) => new ORRule(rules);

    public static ORRule Rule(object identifier, params IRule[] rules)
    {
      ORRule orRule = new ORRule(rules);
      orRule.Identifier = identifier;
      return orRule;
    }

    protected ORRule(params IRule[] rules)
      : base(rules)
    {
    }

    protected override bool InternalIsTrue()
    {
      bool flag = false;
      if (this.RuleList.Count == 0)
        return false;
      foreach (IRule rule in this.RuleList)
      {
        this.TerminatingRule = rule;
        if (rule.IsTrue)
        {
          flag = true;
          break;
        }
      }
      return flag;
    }

    protected override bool InternalIsFalse()
    {
      bool flag = true;
      if (this.RuleList.Count == 0)
        return true;
      foreach (IRule rule in this.RuleList)
      {
        this.TerminatingRule = rule;
        if (!rule.IsFalse)
        {
          flag = false;
          this.FailureIndicator = rule.FailureIndicator;
          break;
        }
      }
      return flag;
    }

    protected override string ExpressionStringSeparator => " OR ";
  }
}
