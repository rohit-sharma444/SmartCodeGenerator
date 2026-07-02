// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.CombinativeRule
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using System;
using System.Collections;
using System.Text;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
/// Represents Combinative Rule.
/// </summary>
  [Serializable]
  public abstract class CombinativeRule : Rule
  {
    protected ArrayList RuleList;
    protected IRule TerminatingRule;

    protected CombinativeRule(params IRule[] rules)
    {
      this.RuleList = new ArrayList();
      foreach (object rule in rules)
        this.RuleList.Add(rule);
    }

    public void Add(IRule rule) => this.RuleList.Add((object) rule);

    protected virtual string ExpressionStringSeparator => "; ";

    public override string ToString()
    {
      StringBuilder stringBuilder1 = new StringBuilder();
      stringBuilder1.Append(" [ ");
      foreach (IRule rule in this.RuleList)
      {
        if (rule.Equals((object) this.TerminatingRule))
          stringBuilder1.Append("TERMINATOR - ");
        stringBuilder1.Append(rule.ToString());
        stringBuilder1.Append("\n");
        stringBuilder1.Append(this.ExpressionStringSeparator);
      }
      if (stringBuilder1.Length > 1)
      {
        StringBuilder stringBuilder2 = stringBuilder1;
        stringBuilder2.Remove(stringBuilder2.Length - this.ExpressionStringSeparator.Length, this.ExpressionStringSeparator.Length);
      }
      stringBuilder1.Append("] ");
      stringBuilder1.Append(base.ToString());
      return stringBuilder1.ToString();
    }
  }
}
