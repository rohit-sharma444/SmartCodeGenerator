// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.IFailureIndicator
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using System;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
            /// Defines IFailureIndicator.
            /// </summary>  public interface IFailureIndicator
  {
/// <summary>
/// Gets or sets Failure Reason.
/// </summary>
    string FailureReason { get; }
/// <summary>
/// Gets or sets Failure Exception.
/// </summary>

    Exception FailureException { get; }
  }
}
