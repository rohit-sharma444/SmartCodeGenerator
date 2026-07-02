using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents Format Rule Exception.
/// </summary>

public class FormatRuleException : ApplicationException 
{

    public int Number => 3;

    public string GetMessage() => "FORMAT RULE Exception occured!";

    public FormatRuleException()
    {
    }

    public FormatRuleException(string message)
      : base(message)
    {
    }

    public FormatRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public FormatRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public FormatRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public FormatRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected FormatRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
