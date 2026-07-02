using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents Less Than Equal To Rule Exception.
/// </summary>

public class LessThanEqualToRuleException : ApplicationException 
{

    public int Number => 5;

    public string GetMessage() => "Less than equal to rule Exception occured!";

    public LessThanEqualToRuleException()
    {
    }

    public LessThanEqualToRuleException(string message)
      : base(message)
    {
    }

    public LessThanEqualToRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public LessThanEqualToRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public LessThanEqualToRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public LessThanEqualToRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected LessThanEqualToRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
