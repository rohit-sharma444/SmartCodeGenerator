using System.Text.RegularExpressions;

namespace PROJECTBUILDER.RULES;
/// <summary>
/// Represents Format Rule.
/// </summary>

public class FormatRule : Rule
{
    private const string IS_FORMATTED = "is of format";
    private const string IS_NOT_FORMATTED = "is not of format";
    private string _strFormat;
    private string _strValue;

    /// <summary>
    /// Instanciate with value to check with specified format
    /// </summary>
    /// <param name="value"></param>
    /// <param name="format"></param>
    /// <returns></returns>
    public static FormatRule Rule(string value, string format) => new FormatRule(value, format);

    public static FormatRule Rule(string value, string format, object ruleIdentifier)
    {
        FormatRule formatRule = new FormatRule(value, format);
        formatRule.Identifier = ruleIdentifier;
        return formatRule;
    }

    protected FormatRule(string value, string format)
    {
        this._strValue = value;
        this._strFormat = format;
    }

    public override bool IsTrue()
    {
        bool flag = true;
        if (!Regex.Match(this._strValue, this._strFormat).Success)
        {
            if (this.FailureIndicator == null)
                this.SetFailureIndicator("is not of format");
            flag = false;
        }
        return flag;
    }

    public override bool IsFalse()
    {
        bool flag = true;
        if (Regex.Match(this._strValue, this._strFormat).Success)
        {
            if (this.FailureIndicator == null)
                this.SetFailureIndicator("is of format");
            flag = false;
        }
        return flag;
    }

    public string Name => "FormatRule";
}