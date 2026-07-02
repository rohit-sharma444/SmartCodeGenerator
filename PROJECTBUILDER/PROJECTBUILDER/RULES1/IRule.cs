// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.IRule
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
            /// Defines IRule.
            /// </summary>  public interface IRule
  {
/// <summary>
/// Gets or sets Is True.
/// </summary>
    bool IsTrue { get; }
/// <summary>
/// Gets or sets Is False.
/// </summary>

    bool IsFalse { get; }
/// <summary>
/// Gets or sets Failure Indicator.
/// </summary>

    IFailureIndicator FailureIndicator { get; set; }
/// <summary>
/// Gets or sets Identifier.
/// </summary>

    object Identifier { get; set; }
  }
}
