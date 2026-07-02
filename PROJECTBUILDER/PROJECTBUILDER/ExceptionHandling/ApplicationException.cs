using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents Application Exception.
/// </summary>

[Serializable]
public abstract class ApplicationException : System.ApplicationException
{
    public ApplicationException()
    {
    }

    public ApplicationException(string message)
      : base(message)
    {
    }

    public ApplicationException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public ApplicationException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public ApplicationException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public ApplicationException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected ApplicationException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }

    public int Number;

    public string GetMessage() => "";
}
