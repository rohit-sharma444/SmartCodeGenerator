using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents Not Blank Rule Exception.
/// </summary>

public class NotBlankRuleException : ApplicationException 
{

    public int Number => 13;

    public string GetMessage() => "Not blank rule Exception occured!";

    public NotBlankRuleException()
    {
    }

    public NotBlankRuleException(string message)
      : base(message)
    {
    }

    public NotBlankRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public NotBlankRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public NotBlankRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public NotBlankRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected NotBlankRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
