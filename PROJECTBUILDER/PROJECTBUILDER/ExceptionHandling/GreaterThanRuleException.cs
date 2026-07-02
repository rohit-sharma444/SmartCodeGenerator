using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents Greater Than Rule Exception.
/// </summary>

public class GreaterThanRuleException : ApplicationException 
{

    public int Number => 4;

    public string GetMessage() => "Greater than rule Exception occured!";

    public GreaterThanRuleException()
    {
    }

    public GreaterThanRuleException(string message)
      : base(message)
    {
    }

    public GreaterThanRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public GreaterThanRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public GreaterThanRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public GreaterThanRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected GreaterThanRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
