namespace PROJECTBUILDER.RULES;
/// <summary>
/// Represents Generic Rules.
/// </summary>

public static class GenericRules
{
    public static FormatRule IsDecimalRule(string str, ExceptionFailureIndicator failureIndicator)
    {
        FormatRule formatRule = FormatRule.Rule(str, "^([0-9]+.[0-9]*)|([0-9]*.[0-9]+)|([0-9]+)$");
        formatRule.FailureIndicator = failureIndicator;
        return formatRule;
    }

    public static FormatRule AlphaNumericNonBlankRule(string str, ExceptionFailureIndicator failureIndicator)
    {
        FormatRule formatRule = FormatRule.Rule(str, "^[a-zA-Z0-9]+$");
        formatRule.FailureIndicator = failureIndicator;
        return formatRule;
    }

    public static FormatRule NoWhitespaceRule(string str, ExceptionFailureIndicator failureIndicator)
    {
        FormatRule formatRule = FormatRule.Rule(str, "^\\S+$");
        formatRule.FailureIndicator = failureIndicator;
        return formatRule;
    }

    public static BooleanRule NotBlankRule(string strTest, ExceptionFailureIndicator failureIndicator)
    {
        BooleanRule booleanRule = BooleanRule.Rule(strTest.Trim().Length > 0);
        booleanRule.FailureIndicator = failureIndicator;
        return booleanRule;
    }


    public static BooleanRule DateNotNullRule(DateTime date, ExceptionFailureIndicator failureIndicator)
    {
        BooleanRule booleanRule = BooleanRule.Rule(date != DateTime.MinValue);
        booleanRule.FailureIndicator = failureIndicator;
        return booleanRule;
    }
}