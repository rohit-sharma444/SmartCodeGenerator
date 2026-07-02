using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents Boolean Rule Exception.
/// </summary>

public class BooleanRuleException : ApplicationException 
{

    public int Number => 1;

    public string GetMessage() => "BOOLEAN RULE Exception occured!";

    public BooleanRuleException()
    {
    }

    public BooleanRuleException(string message)
      : base(message)
    {
    }

    public BooleanRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public BooleanRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public BooleanRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public BooleanRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected BooleanRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
