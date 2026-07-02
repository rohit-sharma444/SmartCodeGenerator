using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents Not Rule Exception.
/// </summary>

public class NotRuleException : ApplicationException 
{

    public int Number => 7;

    public string GetMessage() => "Not rule Exception occured!";

    public NotRuleException()
    {
    }

    public NotRuleException(string message)
      : base(message)
    {
    }

    public NotRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public NotRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public NotRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public NotRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected NotRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
