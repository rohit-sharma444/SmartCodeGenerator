// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.FormatRule
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using System;
using System.Text;
using System.Text.RegularExpressions;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
/// Represents Format Rule.
/// </summary>
  [Serializable]
  public class FormatRule : Sage.Common.Rules.Rule
  {
    private const string IS_FORMATTED = "is of format";
    private const string IS_NOT_FORMATTED = "is not of format";
    private string _strFormat;
    private string _strValue;

    public static FormatRule Rule(string value, string format) => new FormatRule(value, format);

    public static FormatRule Rule(string value, string format, object ruleIdentifier)
    {
      FormatRule formatRule = new FormatRule(value, format);
      formatRule.Identifier = ruleIdentifier;
      return formatRule;
    }

    protected FormatRule(string value, string format)
    {
      this._strValue = value;
      this._strFormat = format;
    }

    protected override bool InternalIsTrue()
    {
      this.Reset();
      bool flag = true;
      if (!Regex.Match(this._strValue, this._strFormat).Success)
      {
        if (this.FailureIndicator == null)
          this.SetFailureIndicator("is not of format");
        flag = false;
      }
      return flag;
    }

    protected override bool InternalIsFalse()
    {
      this.Reset();
      bool flag = true;
      if (Regex.Match(this._strValue, this._strFormat).Success)
      {
        if (this.FailureIndicator == null)
          this.SetFailureIndicator("is of format");
        flag = false;
      }
      return flag;
    }

    public override string ToString()
    {
      StringBuilder stringBuilder = new StringBuilder();
      stringBuilder.Append(this._strValue);
      stringBuilder.Append(" IS_OF_FORMAT? ");
      stringBuilder.Append(this._strFormat);
      stringBuilder.Append(" ");
      return "FORMAT RULE: " + (object) stringBuilder + base.ToString();
    }
  }
}
