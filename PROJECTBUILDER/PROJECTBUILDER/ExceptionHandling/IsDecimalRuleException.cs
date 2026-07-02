using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents Is Decimal Rule Exception.
/// </summary>

public class IsDecimalRuleException : ApplicationException 
{

    public int Number => 10;

    public string GetMessage() => "Is decimal rule Exception occured!";

    public IsDecimalRuleException()
    {
    }

    public IsDecimalRuleException(string message)
      : base(message)
    {
    }

    public IsDecimalRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public IsDecimalRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public IsDecimalRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public IsDecimalRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected IsDecimalRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
