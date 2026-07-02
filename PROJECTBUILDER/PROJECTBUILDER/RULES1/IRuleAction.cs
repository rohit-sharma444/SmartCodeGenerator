// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.IRuleAction
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
            /// Defines IRuleAction.
            /// </summary>  public interface IRuleAction
  {
/// <summary>
/// Gets or sets Rule.
/// </summary>
    IRule Rule { get; set; }
/// <summary>
/// Gets or sets Was Valid.
/// </summary>

    bool WasValid { get; }
/// <summary>
/// Check.
/// </summary>


    void Check();
/// <summary>
/// Check.
/// </summary>
/// <param name="validRuleAction">valid Rule Action.</param>


    void Check(RuleAction.ValidRuleAction validRuleAction);
/// <summary>
/// Check.
/// </summary>
/// <param name="invalidRuleAction">invalid Rule Action.</param>


    void Check(RuleAction.InvalidRuleAction invalidRuleAction);
/// <summary>
/// Check.
/// </summary>
/// <param name="validRuleAction">valid Rule Action.</param>
/// <param name="invalidRuleAction">invalid Rule Action.</param>


    void Check(
      RuleAction.ValidRuleAction validRuleAction,
      RuleAction.InvalidRuleAction invalidRuleAction);
  }
}
