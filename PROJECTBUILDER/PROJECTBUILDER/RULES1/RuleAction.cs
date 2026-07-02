// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.RuleAction
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using Sage.Common.ProgrammingByContract;
using System;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
/// Represents Rule Action.
/// </summary>
  [Serializable]
  public class RuleAction : IRuleAction
  {
    private IRule _oRule;
    private bool _bWasValid;
    private bool _bHasBeenRun;
    private bool _bAllowableWarningStatus = true;

    public RuleAction(IRule rule) => this.Rule = rule;

    public IRule Rule
    {
      get => this._oRule;
      set
      {
        Contract.PreCond(value != null, "Rule cannot be null on a RuleAction object");
        this._oRule = value;
        this._bWasValid = false;
        this._bHasBeenRun = false;
      }
    }

    public bool WasValid
    {
      get
      {
        Contract.PreCond(this._bHasBeenRun, "Check has not been called");
        return this._bWasValid;
      }
    }

    public void Check()
    {
      this.Check((RuleAction.ValidRuleAction) null, (RuleAction.InvalidRuleAction) null);
    }

    public void Check(RuleAction.ValidRuleAction validRuleAction)
    {
      this.Check(validRuleAction, (RuleAction.InvalidRuleAction) null);
    }

    public void Check(RuleAction.InvalidRuleAction invalidRuleAction)
    {
      this.Check((RuleAction.ValidRuleAction) null, invalidRuleAction);
    }

    public void Check(
      RuleAction.ValidRuleAction validRuleAction,
      RuleAction.InvalidRuleAction invalidRuleAction)
    {
      this.PerformAssociatedAction(validRuleAction, invalidRuleAction);
    }

    protected virtual void DefaultValidRuleAction()
    {
    }

    protected virtual void DefaultInvalidRuleAction()
    {
      throw RuleFailureException.Create(this.Rule);
    }

    private void PerformAssociatedAction(
      RuleAction.ValidRuleAction validRuleAction,
      RuleAction.InvalidRuleAction invalidRuleAction)
    {
      this._bWasValid = this.Rule.IsTrue;
      this._bHasBeenRun = true;
      if (this.WasValid)
      {
        if (validRuleAction != null)
          validRuleAction(this.Rule);
        else
          this.DefaultValidRuleAction();
      }
      else if (invalidRuleAction != null)
        invalidRuleAction(this.Rule);
      else
        this.DefaultInvalidRuleAction();
    }

    public bool AllowableWarningStatus
    {
      get => this._bAllowableWarningStatus;
      protected set => this._bAllowableWarningStatus = value;
    }

    public delegate void ValidRuleAction(IRule rule);

    public delegate void InvalidRuleAction(IRule rule);
  }
}
