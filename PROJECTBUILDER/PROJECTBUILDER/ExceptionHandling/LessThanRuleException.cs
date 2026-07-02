using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents Less Than Rule Exception.
/// </summary>

public class LessThanRuleException : ApplicationException 
{

    public int Number => 6;

    public string GetMessage() => "Less than rule Exception occured!";

    public LessThanRuleException()
    {
    }

    public LessThanRuleException(string message)
      : base(message)
    {
    }

    public LessThanRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public LessThanRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public LessThanRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public LessThanRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected LessThanRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
