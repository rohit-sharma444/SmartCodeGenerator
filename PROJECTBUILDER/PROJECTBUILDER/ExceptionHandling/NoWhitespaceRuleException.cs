using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents No Whitespace Rule Exception.
/// </summary>

public class NoWhitespaceRuleException : ApplicationException 
{

    public int Number => 12;

    public string GetMessage() => "No white space rule Exception occured!";

    public NoWhitespaceRuleException()
    {
    }

    public NoWhitespaceRuleException(string message)
      : base(message)
    {
    }

    public NoWhitespaceRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public NoWhitespaceRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public NoWhitespaceRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public NoWhitespaceRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected NoWhitespaceRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
