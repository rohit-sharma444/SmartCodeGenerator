using PROJECTBUILDER.DTO;
using PROJECTBUILDER.ExceptionHandling;
using PROJECTBUILDER.RULES;

namespace PROJECTBUILDER;
/// <summary>
/// Represents Validation Engine.
/// </summary>

public static class ValidationEngine
{

    public static bool Validate(DataHolderDTO dataObject)
    {
        var result = true;
        if (dataObject == null) { throw new RuleFailureException("DataHolderDTO is null"); }

        LogWriter.Write("Validating...");

        //Reading business data.
        foreach (Object bd in dataObject.BusinessData.Item2)
        {
            LogWriter.Write("Reading row for validation.");
            IRule secondRuleToCheck = null;

            for (var ctr = 0; ctr < bd.GetType().GetProperties().Count(); ctr++)
            {
                LogWriter.Write("Reading column for validation.");
                if (bd.GetType().GetProperty(bd.GetType().
                    GetProperties()[ctr].Name).Name.ToString() !=
                    dataObject.Metadata[ctr].Field.ToString()) result = false;

                if (!result)
                {
                    var failureMessage = ("Metadata {0} does not mapped with business data {1}.",
                        dataObject.Metadata[ctr].Field.ToString(), bd.GetType().GetProperty(bd.GetType().
                    GetProperties()[ctr].Name).Name.ToString());
                    LogWriter.Write(failureMessage.ToString());
                    break;
                }

                var value = bd.GetType().GetProperty(bd.GetType().GetProperties()
                    [ctr].Name).GetValue(bd);

                foreach (var rule in dataObject.Validators.Where(x => x.Field == dataObject.Metadata[ctr].Field.ToString()))
                {
                    IRule ruleToCheck = null;
                    switch (rule.Rule)
                    {
                        case "BOOLEANRULE":
                            if (ruleToCheck == null)
                                ruleToCheck = BooleanRule.Rule(value.ToString() == string.Empty);
                            if (secondRuleToCheck == null)
                                secondRuleToCheck = BooleanRule.Rule(value.ToString() == string.Empty);
                            LogWriter.Write("BOOLEAN RULE CALLED.");
                            break;

                        case "ANDRULE":
                            IRule rule3 = (IRule)ANDRule.Rule(new IRule[2]
                              {
                                      ruleToCheck,
                                      secondRuleToCheck
                              });
                            if (rule3.IsFalse())
                                rule3.FailureIndicator = (IFailureIndicator)new ExceptionFailureIndicator(typeof(AndRuleException));
                            LogWriter.Write("AND RULE CALLED.");
                            break;

                        case "ORRRULE":
                            secondRuleToCheck = (IRule)ORRule.Rule(new IRule[]
                             {
                                                    ruleToCheck
                             });
                            if (secondRuleToCheck.IsFalse())
                                secondRuleToCheck.FailureIndicator = (IFailureIndicator)new ExceptionFailureIndicator(typeof(ORRuleException));
                            LogWriter.Write("OR RULE CALLED.");
                            break;

                        case "EXISTRULE":
                            LogWriter.Write("EXIST RULE CALLED.");
                            break;

                        case "NOTEXISTSRULE":
                            LogWriter.Write("NOT EXIST RULE CALLED.");
                            break;

                        case "GREATERTHANRULE":
                            LogWriter.Write("GREATER THAN RULE CALLED.");
                            break;

                        case "LESSTHANRULE":
                            LogWriter.Write("LESS THAN RULE CALLED.");
                            break;

                        case "GREATERTHANDEQUALTORULE":
                            LogWriter.Write("GREATER THAN AND EQUAL TO RULE CALLED.");
                            break;

                        case "LESSTHANDEQUALTORULE":
                            LogWriter.Write("LESS THAN AND EQUAL TO RULE CALLED.");
                            break;

                        case "EQUALTORULE":
                            LogWriter.Write("EQUAL TO RULE CALLED.");
                            break;

                        case "NOTRULE":
                            LogWriter.Write("NOT RULE CALLED.");
                            //ruleToCheck = NOTRule.Rule(value.ToString());
                            break;

                        case "FORMATRULE":
                            LogWriter.Write("FORMAT RULE CALLED.");
                            ruleToCheck = FormatRule.Rule(value.ToString(), "0-9");
                            if (!ruleToCheck.IsFalse()) throw new RuleFailureException("Value " + value.ToString() + " is not of format 0-9");
                            break;

                        case "IsDecimalRule":
                            break;

                        case "AlphaNumericNonBlankRule":
                            break;

                        case "NoWhitespaceRule":
                            break;

                        case "NotBlankRule":
                            break;

                        case "DateNotNullRule":
                            break;

                        case "Memberrule":
                            break;
                    }
                }
            }
        }
        return result;
    }
}