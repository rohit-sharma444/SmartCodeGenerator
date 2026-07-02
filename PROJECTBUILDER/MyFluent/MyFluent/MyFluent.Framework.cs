using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.RegularExpressions;

namespace MyFluent.Framework
{
    public interface IValidator<T>
    {
        ValidationResult Validate(T item);
        string GetJQueryRules(bool submitHandler);
    }

    public class Validator<T> : IValidator<T>
    {
        public Validator()
        {
            RuleBuilder<T>.InitialiseSets();
        }

        private void ProcessRuleSet(T item, RuleSet<T, object> ruleSet, ValidationResult results)
        {
            ruleSet.Rules.ForEach(rule =>
            {
                try
                {
                    object o = ruleSet.RuleSetFor.Compile()(item);

                    if (!rule.TheRule(o))
                    {
                        results.Errors.Add(new ValidationError { ErrorMessage = rule.ErrorMessage });
                        results.IsValid = false;
                    }
                }
                catch (Exception ex)
                {
                    results.Errors.Add(new ValidationError { ErrorMessage = ex.Message });
                    results.IsValid = false;
                }
            });
        }

        #region Public Methods
        public string GetJQueryRules(bool submitHandler)
        {
            string js = @"
            <script type='text/javascript'>
                var ruleNo = 0; var errorList = {} ; 
                
                function OnInit() { 
                    $(document).ready(function() {";
            
            string jQueryRule = string.Empty;
            string jQueryFunctions = string.Empty;
            List<string> jQueryFunctionNames = new List<string>();
            int iSet = 0;
            string rules = string.Empty;

            RuleBuilder<T>.GetSets().ForEach(set =>
            {
                var ruleSet = set as RuleSet<T, object>;

                if (ruleSet != null)
                {
                    UnaryExpression me = (UnaryExpression)ruleSet.RuleSetFor.Body;

                    string propertyName = Regex.Match(me.Operand.ToString(), @"^.*?\.(?<PropertyName>.*?)$").Groups["PropertyName"].Captures[0].Value;

                    string errorMessages = string.Empty;

                    jQueryRule = jQueryRule + @"
                            jQuery.validator.addMethod(""{0}"", function (value, element, params) {                                
                                var errors = {};
                                var isError = false;
                                var validator = this;                                                                 
                            ";

                    int iRuleNo = 0;

                    ruleSet.Rules.ForEach(rule =>
                    {
                        errorMessages = errorMessages + @"<br />" + rule.ErrorMessage;

                        string jQueryFunctionName = rule.JQueryFunctionName.Replace(".", "_");

                        string jQueryFunctionNameProperty = propertyName.Replace(".", "_") + jQueryFunctionName + iRuleNo.ToString();

                        if (!string.IsNullOrEmpty(rule.JQueryFunction))
                        {
                            jQueryRule = jQueryRule + "\nif (" + jQueryFunctionNameProperty + @"(value) == false){ isError = true; errors[element.name] =  errors[element.name] != null ?  errors[element.name] + ""|"" + """ + rule.ErrorMessage + @""" : """ + rule.ErrorMessage + @"""; }";

                            if (!jQueryFunctionNames.Contains(jQueryFunctionNameProperty))
                            {
                                jQueryFunctionNames.Add(jQueryFunctionNameProperty);
                                jQueryFunctions = jQueryFunctions + rule.JQueryFunction.Replace("function " + jQueryFunctionName, "function " + jQueryFunctionNameProperty);
                            }
                        }
                        else
                        {
                            jQueryRule = jQueryRule + "\nif (" + jQueryFunctionName+ @"(value) == false){ isError = true; errors[element.name] =  errors[element.name] != null ?  errors[element.name] + ""|"" + """ + rule.ErrorMessage + @""" : """ + rule.ErrorMessage + @"""; }";
                        }
                        
                        iRuleNo++;
                    });

                    jQueryRule = jQueryRule + @" FireValidation(isError, validator, errors, element); return !isError; }, """");";

                    rules = rules + @"""" + propertyName + @"""" + @": { " + @"""" + propertyName + @"""" + @": true }" + (RuleBuilder<T>.GetSets().Count - 1 == iSet ? string.Empty : @",");

                    jQueryRule = Regex.Replace(jQueryRule, @"\{0\}", propertyName);

                    iSet++;
                }  
            });

            jQueryFunctions = jQueryFunctions + @"
                                                    function FireValidation(isError, validator, errors, element) {
                                                        if (ruleNo < " + (RuleBuilder<T>.GetSets().Count - 1).ToString() + @") { 
                                                            if (isError) {
                                                              errorList[element.name] = errors[element.name];   
                                                            }                                                
                                                            validator.showErrors(errorList);  
                                                            validator.defaultShowErrors();
                                                            ruleNo = 0;
                                                            errorList = {};
                                                        } else { 
                                                            if (isError) {
                                                                errorList[element.name] = errors[element.name]; 
                                                            }                                                            
                                                            ruleNo++; 
                                                        } 
                                                    }";

            string strSubmitHandler = string.Empty;

            if (submitHandler)
            {
                strSubmitHandler = @"
                                            $(document).ready(function () {
                                                $(document).submit(function (e) {
                                                    $(""form"").validate({
                                                        showErrors: function (errorMap, errorList) {
                                                        },
                                                        rules:
                                                            {
                                                                " + rules + @"
                                                            }
                                                        }).resetForm();                                                    

                                                    return $(""form"").valid();
                                                });
                                            });
                                        ";
            }            

            return js + jQueryRule + "}); } OnInit();\n"  + jQueryFunctions + strSubmitHandler + "</script>";
        }

        public ValidationResult Validate(T item)
        {
            ValidationResult results = new ValidationResult();
            results.Errors = new List<ValidationError>();
            results.IsValid = true;

            RuleBuilder<T>.GetSets().ForEach(set =>
            {
                try
                {
                    var ruleSet = set as RuleSet<T, object>;

                    if (ruleSet == null)
                    {
                        var conditionSet = (ConditionSet<T, object>)set;

                        if (conditionSet.Condition(conditionSet.ConditionFor.Compile()(item)))
                        {
                            conditionSet.ConditionObjRuleSets.ForEach(objRuleSet =>
                            {
                                ProcessRuleSet(item, objRuleSet, results);
                            });
                        }
                    }
                    else
                    {
                        ProcessRuleSet(item, ruleSet, results);
                    }
                }
                catch (Exception ex)
                {
                    results.Errors.Add(new ValidationError { ErrorMessage = ex.Message });
                    results.IsValid = false;
                }
            });

            return results;
        }

        public static RuleSet<T, TProperty> RuleFor<TProperty>(Expression<Func<T, TProperty>> ruleFor)
        {
            RuleSet<T, object> ruleSet = new RuleSet<T, object>();
            RuleSet<T, TProperty> ruleSetT = new RuleSet<T, TProperty>();

            ruleSet.RuleSetFor = Helpers.Convert(ruleFor);
            ruleSet.Rules = new List<Rule<object>>();

            ruleSetT.RuleSetFor = ruleFor;
            ruleSetT.Rules = new List<Rule<TProperty>>();
            ruleSetT.ObjRuleSet = ruleSet;

            RuleBuilder<T>.AddSet(ruleSet);

            return ruleSetT;
        }

        public static ConditionSet<T, TProperty> If<TProperty>(Expression<Func<T, TProperty>> conditionFor, Func<TProperty, bool> condition)
        {
            ConditionSet<T, object> conditionSet = new ConditionSet<T, object>();
            ConditionSet<T, TProperty> conditionSetTProperty = new ConditionSet<T, TProperty>();

            conditionSet.ConditionFor = Helpers.Convert(conditionFor);
            conditionSet.Condition = Helpers.Convert(condition);
            conditionSet.ConditionObjRuleSets = new List<RuleSet<T, object>>();

            conditionSetTProperty.ConditionFor = conditionFor;
            conditionSetTProperty.Condition = condition;
            conditionSetTProperty.ConditionObjRuleSets = new List<RuleSet<T, object>>();
            conditionSetTProperty.ObjConditionSet = conditionSet;

            RuleBuilder<T>.AddSet(conditionSet);

            return conditionSetTProperty;
        }
        #endregion
    }

    public class Rule<TProperty>
    {
        internal Func<TProperty, bool> TheRule { get; set; }
        internal string ErrorMessage { get; set; }
        internal string JQueryFunction { get; set; }
        internal string JQueryFunctionName { get; set; }
    }

    public abstract class Set<T, TProperty>
    {
        //Data structures required by ConditionSet
        internal protected Expression<Func<T, TProperty>> ConditionFor { get; set; }
        internal protected Func<TProperty, bool> Condition { get; set; }
        internal protected List<RuleSet<T, object>> ConditionObjRuleSets { get; set; }
        internal protected ConditionSet<T, object> ObjConditionSet { get; set; }

        //Data structures required by RuleSet
        internal protected Expression<Func<T, TProperty>> RuleSetFor { get; set; }
        internal protected List<Rule<TProperty>> Rules { get; set; }
        internal protected RuleSet<T, object> ObjRuleSet { get; set; }
    }

    public class ConditionSet<T, TProperty> : Set<T, TProperty>
    {
        public RuleSet<T, TProperty> ConditionRuleFor<TConditionProperty>(Expression<Func<T, TConditionProperty>> ruleSetFor)
        {
            RuleSet<T, object> ruleSet = new RuleSet<T, object>();

            RuleSet<T, TProperty> ruleSetT = new RuleSet<T, TProperty>();
            ruleSetT.ObjRuleSet = ruleSet;
            ruleSetT.ObjConditionSet = this.ObjConditionSet;
            ruleSetT.Rules = new List<Rule<TProperty>>();

            ruleSet.RuleSetFor = Helpers.Convert(ruleSetFor);
            ruleSet.Rules = new List<Rule<object>>();
            ruleSet.ObjRuleSet = ruleSet;
            ruleSet.ConditionObjRuleSets = new List<RuleSet<T, object>>() { ruleSet };

            this.ObjConditionSet.ConditionObjRuleSets.Add(ruleSet);

            return ruleSetT;
        }
    }

    public partial class RuleSet<T, TProperty> : Set<T, TProperty>
    {
        public RuleSet<T, TProperty> ConditionRuleFor<TConditionProperty>(Expression<Func<T, TConditionProperty>> ruleSetFor)
        {
            RuleSet<T, object> ruleSet = new RuleSet<T, object>();

            RuleSet<T, TProperty> ruleSetT = new RuleSet<T, TProperty>();
            ruleSetT.ObjRuleSet = ruleSet;
            ruleSetT.Rules = new List<Rule<TProperty>>();

            ruleSet.RuleSetFor = Helpers.Convert(ruleSetFor);
            ruleSet.Rules = new List<Rule<object>>();
            ruleSet.ConditionObjRuleSets = new List<RuleSet<T, object>>() { ruleSet };

            if (this.ObjConditionSet == null)
            {
                throw new Exception("A ConditionRuleFor has no attached rules or has been used inappropriately. Remove it if not required.");
            }

            this.ObjConditionSet.ConditionObjRuleSets.Add(ruleSet);

            return ruleSetT;
        }               
    }

    public static class RuleBuilder<T>
    {
        private static List<Set<T, object>> sets;

        internal static void AddSet(Set<T, object> set)
        {
            sets.Add(set);
        }

        internal static List<Set<T, object>> GetSets()
        {
            return sets;
        }

        internal static void InitialiseSets()
        {
            sets = new List<Set<T, object>>();
        }
    }
}
