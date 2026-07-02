// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.RuleFailureException
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using System;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
/// Represents Rule Failure Exception.
/// </summary>
  [Serializable]
  public class RuleFailureException : ApplicationException
  {
    private object _oFailureIndicator;
    private object _oRuleIdentifier;

    public static RuleFailureException Create(IRule rule)
    {
      RuleFailureException failureException;
      if (rule != null)
      {
        failureException = new RuleFailureException("INVALID RULE: rule failed ->" + (object) rule);
        failureException.FailureIndicator = (object) rule.FailureIndicator;
        failureException.RuleIdentifier = rule.Identifier;
      }
      else
        failureException = new RuleFailureException("INVALID RULE: rule is NULL");
      return failureException;
    }

    public RuleFailureException()
    {
    }

    public RuleFailureException(string str)
      : base(str)
    {
    }

    public RuleFailureException(string str, Exception exp)
      : base(str, exp)
    {
    }

    public object RuleIdentifier
    {
      get => this._oRuleIdentifier;
      set => this._oRuleIdentifier = value;
    }

    public object FailureIndicator
    {
      get => this._oFailureIndicator;
      set => this._oFailureIndicator = value;
    }
  }
}
