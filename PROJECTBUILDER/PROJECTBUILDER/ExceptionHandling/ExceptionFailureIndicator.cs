namespace PROJECTBUILDER.RULES;
/// <summary>
/// Represents Exception Failure Indicator.
/// </summary>

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
      : this(exceptionType, (string[])null)
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
                this._oException = this._strExceptionParams == null ? (Exception)Activator.CreateInstance(this._oExceptionType) : (Exception)Activator.CreateInstance(this._oExceptionType, (object[])this._strExceptionParams);
            return this._oException;
        }
    }

    public string FailureReason => this.FailureException.Message;

    public override string ToString() => this.FailureReason + " " + this._oExceptionType.ToString();
}
