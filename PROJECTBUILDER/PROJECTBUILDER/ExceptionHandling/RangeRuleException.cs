using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents Range Rule Exception.
/// </summary>

public class RangeRuleException : ApplicationException 
{

    public int Number => 9;

    public string GetMessage() => "Range rule Exception occured!";

    public RangeRuleException()
    {
    }

    public RangeRuleException(string message)
      : base(message)
    {
    }

    public RangeRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public RangeRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public RangeRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public RangeRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected RangeRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
