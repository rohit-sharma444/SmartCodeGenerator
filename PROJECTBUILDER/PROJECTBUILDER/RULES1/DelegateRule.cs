// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.DelegateRule
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using System.Text;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
/// Represents Delegate Rule.
/// </summary>
  public class DelegateRule : Sage.Common.Rules.Rule
  {
    private DelegateRule.ValidateAction _oValidator;

    public static DelegateRule Rule(DelegateRule.ValidateAction validator)
    {
      return new DelegateRule(validator);
    }

    public static DelegateRule Rule(DelegateRule.ValidateAction validator, object ruleIdentifier)
    {
      DelegateRule delegateRule = new DelegateRule(validator);
      delegateRule.Identifier = ruleIdentifier;
      return delegateRule;
    }

    protected DelegateRule(DelegateRule.ValidateAction validator) => this._oValidator = validator;

    protected override bool InternalIsTrue()
    {
      this.Reset();
      return this._oValidator((Sage.Common.Rules.Rule) this);
    }

    protected override bool InternalIsFalse()
    {
      this.Reset();
      return !this._oValidator((Sage.Common.Rules.Rule) this);
    }

    public override string ToString()
    {
      StringBuilder stringBuilder = new StringBuilder();
      stringBuilder.Append((object) this._oValidator);
      stringBuilder.Append(" ");
      return "VALIDATOR RULE: " + (object) stringBuilder + base.ToString();
    }

    public delegate bool ValidateAction(Sage.Common.Rules.Rule rule);
  }
}
