// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.ExceptionFailureIndicator
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using System;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
/// Represents Exception Failure Indicator.
/// </summary>
  [Serializable]
  public class ExceptionFailureIndicator : IFailureIndicator
  {
    private Type _oExceptionType;
    private Exception _oException;
    private string[] _strExceptionParams;

    public ExceptionFailureIndicator(string FailureReason)
      : this(new Exception(FailureReason))
    {
    }

    public ExceptionFailureIndicator(Exception exception)
    {
      this._oException = exception;
      this._oExceptionType = exception.GetType();
    }

    public ExceptionFailureIndicator(Type exceptionType)
      : this(exceptionType, (string[]) null)
    {
    }

    public ExceptionFailureIndicator(Type exceptionType, params string[] str)
    {
      this._oExceptionType = exceptionType;
      this._strExceptionParams = str;
    }

    public Exception FailureException
    {
      get
      {
        if (this._oException == null)
          this._oException = this._strExceptionParams == null ? (Exception) Activator.CreateInstance(this._oExceptionType) : (Exception) Activator.CreateInstance(this._oExceptionType, (object[]) this._strExceptionParams);
        return this._oException;
      }
    }

    public string FailureReason => this.FailureException.Message;

    public override string ToString() => this.FailureReason + " " + this._oExceptionType.ToString();
  }
}
