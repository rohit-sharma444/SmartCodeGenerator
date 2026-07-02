using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents Alpha Numeric Non Blank Rule Exception.
/// </summary>

public class AlphaNumericNonBlankRuleException : ApplicationException 
{

    public int Number => 11;

    public string GetMessage() => "Alpha numberic non blank rule Exception occured!";

    public AlphaNumericNonBlankRuleException()
    {
    }

    public AlphaNumericNonBlankRuleException(string message)
      : base(message)
    {
    }

    public AlphaNumericNonBlankRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public AlphaNumericNonBlankRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public AlphaNumericNonBlankRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public AlphaNumericNonBlankRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected AlphaNumericNonBlankRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
