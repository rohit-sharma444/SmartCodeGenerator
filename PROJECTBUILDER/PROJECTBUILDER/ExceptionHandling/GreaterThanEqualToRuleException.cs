using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents Greater Than Equal To Rule Exception.
/// </summary>

public class GreaterThanEqualToRuleException : ApplicationException 
{

    public int Number => 4;

    public string GetMessage() => "Greater than and equal to rule Exception occured!";

    public GreaterThanEqualToRuleException()
    {
    }

    public GreaterThanEqualToRuleException(string message)
      : base(message)
    {
    }

    public GreaterThanEqualToRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public GreaterThanEqualToRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public GreaterThanEqualToRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public GreaterThanEqualToRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected GreaterThanEqualToRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
