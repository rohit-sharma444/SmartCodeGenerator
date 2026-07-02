// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.ValidationRuleInfo
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using Sage.Common.ProgrammingByContract;
using System;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
/// Represents Validation Rule Info.
/// </summary>
  [Serializable]
  public class ValidationRuleInfo
  {
    private RuleActionType _eRuleActionType;
    private string _strFieldName = string.Empty;

    protected ValidationRuleInfo()
    {
    }

    protected ValidationRuleInfo(RuleActionType ruleActionType, string fieldName)
      : this()
    {
      this._eRuleActionType = ruleActionType;
      this._strFieldName = fieldName;
    }

    public RuleActionType RuleActionType
    {
      get => this._eRuleActionType;
      set => this._eRuleActionType = value;
    }

    public string FieldName
    {
      get => this._strFieldName;
      set => this._strFieldName = value;
    }

    public RuleActionType WarningRuleActionType
    {
      get
      {
        RuleActionType warningRuleActionType = this.RuleActionType;
        if (this.RuleActionType == RuleActionType.ErrorProperty)
          warningRuleActionType = RuleActionType.WarningProperty;
        else if (this.RuleActionType == RuleActionType.ErrorMethod)
          warningRuleActionType = RuleActionType.WarningMethod;
        return warningRuleActionType;
      }
    }

    public static ValidationRuleInfo CreateRuleInfo(RuleActionType ruleActionType, string fieldName)
    {
      Contract.PreCond(fieldName != null, "fieldName canot be null, but can be empty.");
      return new ValidationRuleInfo(ruleActionType, fieldName);
    }

    public static ValidationRuleInfo CreatErrorMethodRuleInfo()
    {
      return new ValidationRuleInfo(RuleActionType.ErrorMethod, string.Empty);
    }
  }
}
