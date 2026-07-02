using System.Runtime.Serialization;

namespace PROJECTBUILDER.ExceptionHandling;
/// <summary>
/// Represents Member Rule Exception.
/// </summary>

public class MemberRuleException : ApplicationException 
{

    public int Number => 15;

    public string GetMessage() => "Member rule Exception occured!";

    public MemberRuleException()
    {
    }

    public MemberRuleException(string message)
      : base(message)
    {
    }

    public MemberRuleException(string message, Exception innerException)
      : base(message, innerException)
    {
    }

    public MemberRuleException(int number, Exception innerException)
      : this(number, innerException.Message, innerException)
    {
    }

    public MemberRuleException(int number, string message)
      : this(number, message, (Exception)null)
    {
    }

    public MemberRuleException(int number, string message, Exception innerException)
      : this(message, innerException)
    {
    }

    protected MemberRuleException(SerializationInfo info, StreamingContext context)
      : base(info, context)
    {
    }
}
