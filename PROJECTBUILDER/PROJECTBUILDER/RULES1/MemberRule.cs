// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.MemberRule
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
/// Represents Member Rule.
/// </summary>
  [Serializable]
  public class MemberRule : Sage.Common.Rules.Rule
  {
    private const string IS_MEMBER = "in list";
    private const string IS_NOT_MEMBER = "not in list";
    private IList _oList;
    private object _oValue;

    public static MemberRule Rule(object value, IList list) => new MemberRule(value, list);

    public static MemberRule Rule(object value, IList list, object ruleIdentifier)
    {
      MemberRule memberRule = new MemberRule(value, list);
      memberRule.Identifier = ruleIdentifier;
      return memberRule;
    }

    protected MemberRule(object value, IList list)
    {
      this._oValue = value;
      this._oList = list;
    }

    protected override bool InternalIsTrue()
    {
      this.Reset();
      bool flag = true;
      if (!this._oList.Contains(this._oValue))
      {
        if (this.FailureIndicator != null)
          this.SetFailureIndicator("not in list");
        flag = false;
      }
      return flag;
    }

    protected override bool InternalIsFalse()
    {
      this.Reset();
      bool flag = true;
      if (this._oList.Contains(this._oValue))
      {
        if (this.FailureIndicator != null)
          this.SetFailureIndicator("in list");
        flag = false;
      }
      return flag;
    }

    public override string ToString()
    {
      StringBuilder stringBuilder = new StringBuilder();
      stringBuilder.Append(this._oValue);
      stringBuilder.Append(" IS_IN? ");
      stringBuilder.Append((object) this._oList);
      stringBuilder.Append(" ");
      return "MEMBER RULE: " + (object) stringBuilder + base.ToString();
    }
  }
}
