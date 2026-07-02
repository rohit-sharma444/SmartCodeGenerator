// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.Rule
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using Sage.Common.ProgrammingByContract;
using System;
using System.Text;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
/// Represents Rule.
/// </summary>
  [Serializable]
  public abstract class Rule : IRule
  {
    private IFailureIndicator _oFailureIndicator;
    private object _oRuleIdentifier;

    public bool IsTrue
    {
      get
      {
        int num = this.InternalIsTrue() ? 1 : 0;
        if (num != 0)
          return num != 0;
        this.SetFailureIndicator("Rule failed IsTrue, no failure indicator present");
        return num != 0;
      }
    }

    public bool IsFalse
    {
      get
      {
        int num = this.InternalIsFalse() ? 1 : 0;
        if (num != 0)
          return num != 0;
        this.SetFailureIndicator("Rule failed IsFalse, no failure indicator present");
        return num != 0;
      }
    }

    public IFailureIndicator FailureIndicator
    {
      get => this._oFailureIndicator;
      set
      {
        if (this.FailureIndicator != null)
          return;
        this._oFailureIndicator = value;
      }
    }

    public void SetFailureIndicator(string failureReason)
    {
      if (this.FailureIndicator != null)
        return;
      this.FailureIndicator = (IFailureIndicator) new ExceptionFailureIndicator(failureReason);
    }

    public object Identifier
    {
      get
      {
        Contract.PreCond(this._oRuleIdentifier != null, "Identifier should not be null");
        return this._oRuleIdentifier;
      }
      set
      {
        Contract.PreCond(value != null, "Identifier should not be null");
        Contract.PreCond(!(value is IFailureIndicator), "The Failure Indicator has been put into the wrong property");
        this._oRuleIdentifier = value;
      }
    }

    protected void Reset() => this.ExtraResetAction();

    protected virtual void ExtraResetAction()
    {
    }

    protected virtual bool InternalIsTrue()
    {
      this.Reset();
      return false;
    }

    protected virtual bool InternalIsFalse()
    {
      this.Reset();
      return true;
    }

    public override string ToString()
    {
      StringBuilder stringBuilder = new StringBuilder();
      stringBuilder.Append("Rule:  ");
      stringBuilder.Append(this._oRuleIdentifier);
      if (this.FailureIndicator != null)
      {
        stringBuilder.Append(" failed with ");
        stringBuilder.Append((object) this.FailureIndicator);
      }
      return stringBuilder.ToString();
    }
  }
}
