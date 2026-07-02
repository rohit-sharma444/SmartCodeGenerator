using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents Date Not Null Rule Exception.
/// </summary>

public class DateNotNullRuleException : ApplicationException 
{

    public int Number => 14;

    public string GetMessage() => "Date not null rule Exception occured!";

    public DateNotNullRuleException()
    {
    }

    public DateNotNullRuleException(string message)
      : base(message)
    {
    }

    public DateNotNullRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public DateNotNullRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public DateNotNullRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public DateNotNullRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected DateNotNullRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
