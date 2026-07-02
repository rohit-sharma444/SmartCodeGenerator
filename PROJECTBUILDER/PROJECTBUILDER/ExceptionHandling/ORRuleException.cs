using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents ORRule Exception.
/// </summary>

public class ORRuleException : ApplicationException 
{

    public int Number => 8;

    public string GetMessage() => "OR rule Exception occured!";

    public ORRuleException()
    {
    }

    public ORRuleException(string message)
      : base(message)
    {
    }

    public ORRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public ORRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public ORRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public ORRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected ORRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
