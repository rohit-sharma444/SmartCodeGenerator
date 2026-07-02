using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents And Rule Exception.
/// </summary>

public class AndRuleException : ApplicationException 
{

    public int Number => 2;

    public string GetMessage() => "AND RULE Exception occured!";

    public AndRuleException()
    {
    }

    public AndRuleException(string message)
      : base(message)
    {
    }

    public AndRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public AndRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public AndRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public AndRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected AndRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
