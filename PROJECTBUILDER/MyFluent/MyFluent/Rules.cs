using System;

namespace MyFluent.Framework
{
    public partial class RuleSet<T, TProperty> : Set<T, TProperty>
    {
        #region Public Rules
        public RuleSet<T, TProperty> CreditCard(string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not a valid credit card number";
            }

            this.ObjRuleSet.Rules.Add(new Rule<object>
            {
                TheRule = prop =>
                {                    
                    string strProp = prop as string;

                    if (strProp != null)
                    {
                        if (Helpers.VerifyWithLuhn(strProp))
                        {
                            return Helpers.ValidateCreditCard(strProp);
                        }
                    }                    

                    return false;
                },
                ErrorMessage = errorMessage,
                JQueryFunction = @"
                    function checkLuhn(input)
                    {
                      var sum = 0;
                      var numdigits = input.length;
                      var parity = numdigits % 2;
                      for(var i=0; i < numdigits; i++) {
                        var digit = parseInt(input.charAt(i))
                        if(i % 2 == parity) digit *= 2;
                        if(digit > 9) digit -= 9;
                        sum += digit;
                      }
                      return (sum % 10) == 0;
                    }
                    function CreditCard(value) {
                        if(value == null || value == '')
                            return false;
                        if (checkLuhn(value)) {
                            var n = value.match(/" + Helpers.regExCreditCard + @"/g);
                            return n != null;
                        }
                        return false;
                    }
                                    ",
                JQueryFunctionName = "CreditCard"
            });

            return this;
        }

        public RuleSet<T, TProperty> Email(string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not a valid email address";
            }

            this.ObjRuleSet.Rules.Add(new Rule<object> 
            {
                TheRule = prop => 
                    {                        
                        string strProp = prop as string;

                        if (strProp != null)
                        {
                            return Helpers.ValidateEmail(strProp);
                        }

                        return false;
                    },
                ErrorMessage = errorMessage,
                JQueryFunction = @"
                            function Email(value) {
                                var n=value.match(/" + Helpers.regExEmail + @"/g);
                                return n != null;
                            }
                            ",
                JQueryFunctionName = "Email"
            });

            return this;
        }

        public RuleSet<T, TProperty> ISOCurrencyCode(string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not a valid ISO currency code";
            }

            this.ObjRuleSet.Rules.Add(new Rule<object>
            {
                TheRule = prop =>
                {
                    string strProp = prop as string;

                    if (strProp != null)
                    {
                        return Helpers.ValidateCurrencyCode(strProp);
                    }

                    return false;
                },
                ErrorMessage = errorMessage,
                JQueryFunction = @"
                            function ISOCurrencyCode(value) {
                                var str=value; 
                                var n=value.match(/" + Helpers.regExISOCurrencyCodes + @"/g);
                                return n != null;
                            }
                            ",
                JQueryFunctionName = "ISOCurrencyCode"
            });

            return this;
        }

        public RuleSet<T, TProperty> NotNullOrEmpty(string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item cannot be null or empty";
            }

            this.ObjRuleSet.Rules.Add(new Rule<object> 
            { 
                TheRule = prop => 
                    {
                        string strProp = prop as String;

                        return !string.IsNullOrEmpty(strProp);                        
                    }, 
                    ErrorMessage = errorMessage,
                    JQueryFunction = @"
                            function NotNullOrEmpty(value) { 
                                if (value == null || value == '')
                                {    
                                    return false; 
                                }

                                return true;
                            }
                            ",
                JQueryFunctionName = "NotNullOrEmpty"
            });

            return this;
        }

        public RuleSet<T, TProperty> IsTrue(string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not true";
            }

            this.ObjRuleSet.Rules.Add(new Rule<object>
            {
                TheRule = prop =>
                {                    
                    bool? boolProp = prop as bool?;

                    if (boolProp == null)
                        return false;                    

                    return boolProp.Value == true;
                },
                ErrorMessage = errorMessage,
                JQueryFunction = @"
                            function IsTrue(value) {   
                                if (value == true)
                                    return true;
                            
                                return false;
                            }
                            ",
                JQueryFunctionName = "IsTrue"
            });

            return this;
        }

        public RuleSet<T, TProperty> IsAlpha(string alphaUnicodeRange, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(alphaUnicodeRange))
            {
                alphaUnicodeRange = "a-zA-Z";
            }

            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not alpha";
            }

            this.ObjRuleSet.Rules.Add(new Rule<object> 
            { 
                TheRule = prop => 
                    {                        
                        string strProp = prop as String;

                        if (strProp != null)
                        {
                            return Helpers.IsAlpha(strProp, alphaUnicodeRange); ;
                        }

                        return false;
                    },
                 JQueryFunction = @"
                            function IsAlpha(value) { 
                                var str=value; 
                                var n=str.match(/^[" + alphaUnicodeRange + @"]+$/g);
                                if (n == null)
                                {    
                                    return false; 
                                }
                                return true;
                            }
                            ",
                JQueryFunctionName = "IsAlpha",
                ErrorMessage = errorMessage 
            });

            return this;
        }

        public RuleSet<T, TProperty> IsNumeric(string numericUnicodeRange, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(numericUnicodeRange))
            {
                numericUnicodeRange = "0-9";
            }

            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not numeric";
            }

            this.ObjRuleSet.Rules.Add(new Rule<object> 
            { 
                TheRule = prop => 
                    {                        
                        string strProp = prop as String;

                        if (strProp != null)
                        {
                            return Helpers.IsNumeric(strProp, numericUnicodeRange);
                        }                        

                        return false;
                    },
                JQueryFunction = @"
                            function IsNumeric(value) { 
                                var str=value; 
                                var n=str.match(/^[" + numericUnicodeRange + @"]+$/g);
                                if (n == null)
                                {    
                                    return false; 
                                }
                                return true;
                            }
                            ",
                JQueryFunctionName = "IsNumeric",
                ErrorMessage = errorMessage }
                );

            return this;
        }

        public RuleSet<T, TProperty> IsAlphaNumeric(string alphaUnicodeRange, string numericUnicodeRange, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(alphaUnicodeRange))
            {
                alphaUnicodeRange = "a-zA-Z";
            }

            if (string.IsNullOrEmpty(numericUnicodeRange))
            {
                numericUnicodeRange = "0-9";
            }

            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not alpha numeric";
            }

            this.ObjRuleSet.Rules.Add(new Rule<object> 
            { 
                TheRule = prop => 
                    {                        
                        string strProp = prop as String;

                        if (strProp != null)
                        {
                            return Helpers.IsAlphaNumeric(strProp, alphaUnicodeRange, numericUnicodeRange);
                        }                        

                        return false;
                    },
                JQueryFunction = @"
                            function IsAlphaNumeric(value) { 
                                var str=value; 
                                var n=str.match(/^(?=[" + numericUnicodeRange + @"]*?[" + alphaUnicodeRange + @"]+)(?=[" + alphaUnicodeRange + @"]*?[" + numericUnicodeRange + @"]+)[" + alphaUnicodeRange + numericUnicodeRange + @"]+$/g);
                                if (n == null)
                                {    
                                    return false; 
                                }
                                return true;
                            }
                            ",
                JQueryFunctionName = "IsAlphaNumeric",
                ErrorMessage = errorMessage 
            });

            return this;
        }

        public RuleSet<T, TProperty> NotNull(string errorMessage = "")            
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item cannot be null";
            }

            this.ObjRuleSet.Rules.Add(new Rule<object> 
            { 
                TheRule = prop => 
                    {
                        try
                        {                            
                            return (TProperty)prop != null;
                        }
                        catch
                        {
                            return false;
                        }                        
                    },
                JQueryFunction = @"
                            function NotNull(value) { 
                                if (value != null)
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                JQueryFunctionName = "NotNull",
                    ErrorMessage = errorMessage 
            });

            return this;
        }

        public RuleSet<T, TProperty> Required(Func<TProperty, bool> required, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Required validation failed";
            }

            this.ObjRuleSet.Rules.Add(new Rule<object> 
            { 
                TheRule = prop => 
                    {
                        try
                        {
                            return required((TProperty)prop);
                        }
                        catch
                        {
                            return false;
                        }                        
                    }, 
                    ErrorMessage = errorMessage 
            });

            return this;
        }

        public RuleSet<T, TProperty> Length(int length, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item does not greater have the required length of " + length.ToString();
            }

            this.ObjRuleSet.Rules.Add(new Rule<object> 
            { 
                TheRule = prop => 
                    {                        
                        string strProp = prop as String;

                        if (strProp != null)
                        {
                            return strProp.Length == length;
                        }

                        return false;                        
                    },
                JQueryFunction = @"
                            function Length(value) { 
                                if (value == " + length + @")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                JQueryFunctionName = "Length",
                    ErrorMessage = errorMessage 
            });

            return this;
        }

        public RuleSet<T, TProperty> Equal(int item, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not equal to " + item.ToString();
            }

            this.ObjRuleSet.Rules.Add(new Rule<object> 
            { 
                TheRule = prop => 
                    {      
                        try
                        {
                            return (int)prop == item;
                        }
                        catch
                        {
                            return false;
                        }                          
                    },
                JQueryFunction = @"
                            function Equal(value) { 
                                if (value == " + item + @")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                JQueryFunctionName = "Equal",
                    ErrorMessage = errorMessage 
            });

            return this;
        }

        public RuleSet<T, TProperty> Equal(string item, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not equal to " + item.ToString();
            }

            this.ObjRuleSet.Rules.Add(new Rule<object> 
            { 
                TheRule = prop => 
                    {
                        string strProp = prop as String;

                        if (strProp != null)
                        {
                            return strProp == item;
                        }
                        return false;

                    },
                JQueryFunction = @"
                            function Equal(value) { 
                                if (value == " + item + @")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                JQueryFunctionName = "Equal", 
                    ErrorMessage = errorMessage 
            });

            return this;
        }

        public RuleSet<T, TProperty> NotEqual(int item, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item is equal to " + item.ToString();
            }

            this.ObjRuleSet.Rules.Add(new Rule<object> 
            { 
                TheRule = prop => 
                    { 
                        try
                        {
                            return (int)prop != item;
                        }
                        catch
                        {
                            return false;
                        }                           
                    },
                JQueryFunction = @"
                            function NotEqual(value) { 
                                if (value != " + item + @")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                JQueryFunctionName = "NotEqual", 
                    ErrorMessage = errorMessage 
            });

            return this;
        }

        public RuleSet<T, TProperty> NotEqual(string item, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item is equal to " + item;
            }

            this.ObjRuleSet.Rules.Add(new Rule<object> 
            { 
                TheRule = prop => 
                    {
                        string strProp = prop as String;

                        if (strProp != null)
                        {
                            return strProp != item;
                        }
                        return false;                        
                    },
                JQueryFunction = @"
                            function NotEqual(value) { 
                                if (value != " + item + @")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                JQueryFunctionName = "NotEqual", 
                    ErrorMessage = errorMessage 
            });

            return this;
        }

        public RuleSet<T, TProperty> GreaterThan(int item, bool orEqual, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not " + (orEqual ? "greater than " : "greater than or equal to ") + item.ToString();
            }

            if (orEqual)
            {
                this.ObjRuleSet.Rules.Add(new Rule<object> 
                { 
                    TheRule = prop => 
                        {
                            try
                            {
                                return (int)prop >= item;
                            }
                            catch
                            {
                                return false;
                            }                             
                        },
                    JQueryFunction = @"
                            function GreaterThanOrEqual(value) {                                 
                                if (value >= " + item + @")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                    JQueryFunctionName = "GreaterThanOrEqual", 
                        ErrorMessage = errorMessage 
                });
            }
            else
            {
                this.ObjRuleSet.Rules.Add(new Rule<object> 
                { 
                    TheRule = prop => 
                        {
                            try
                            {
                                return (int)prop > item;
                            }
                            catch
                            {
                                return false;
                            }                           
                        },
                    JQueryFunction = @"
                            function GreaterThan(value) {                                 
                                if (value > " + item + @")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                    JQueryFunctionName = "GreaterThan",
                        ErrorMessage = errorMessage 
                });
            }

            return this;
        }

        public RuleSet<T, TProperty> GreaterThan(string item, bool orEqual, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not " + (orEqual ? "greater than " : "greater than or equal to ") + item.ToString();
            }

            if (orEqual)
            {
                this.ObjRuleSet.Rules.Add(new Rule<object> 
                { 
                    TheRule = prop => 
                        {
                            string strProp = prop as String;

                            if (strProp != null)
                            {
                                return ((string)prop).CompareTo(item) > 0;
                            }
                            return false;                             
                        },
                    JQueryFunction = @"
                            function GreaterThanOrEqual(value) {                                 
                                if (value >= """ + item + @""")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                    JQueryFunctionName = "GreaterThanOrEqual",  
                        ErrorMessage = errorMessage 
                });
            }
            else
            {
                this.ObjRuleSet.Rules.Add(new Rule<object> 
                { 
                    TheRule = prop => 
                        {
                            string strProp = prop as String;

                            if (strProp != null)
                            {
                                return ((string)prop).CompareTo(item) >= 0;
                            }
                            return false;
                        },
                    JQueryFunction = @"
                            function GreaterThan(value) {                                 
                                if (value > """ + item + @""")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                    JQueryFunctionName = "GreaterThan", 
                        ErrorMessage = errorMessage 
                });
            }

            return this;
        }

        public RuleSet<T, TProperty> LessThan(int item, bool orEqual, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not " + (orEqual ? "less than " : "less than or equal to ") + item.ToString();
            }

            if (orEqual)
            {
                this.ObjRuleSet.Rules.Add(new Rule<object> 
                { 
                    TheRule = prop => 
                        {
                            try
                            {
                                return (int)prop <= item;
                            }
                            catch
                            {
                                return false;
                            }                            
                        },
                    JQueryFunction = @"
                            function LessThanOrEqual(value) {                                 
                                if (value <= " + item + @")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                    JQueryFunctionName = "LessThanOrEqual",  
                        ErrorMessage = errorMessage 
                });
            }
            else
            {
                this.ObjRuleSet.Rules.Add(new Rule<object>
                {
                    TheRule = prop =>
                    {
                        try
                        {
                            return (int)prop < item;
                        }
                        catch
                        {
                            return false;
                        }
                    },
                    JQueryFunction = @"
                            function LessThan(value) {                                 
                                if (value < " + item + @")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                    JQueryFunctionName = "LessThan", 
                    ErrorMessage = errorMessage
                });
            }

            return this;
        }

        public RuleSet<T, TProperty> LessThan(string item, bool orEqual, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not " + (orEqual ? "less than " : "less than or equal to ") + item.ToString();
            }

            if (orEqual)
            {
                this.ObjRuleSet.Rules.Add(new Rule<object> 
                { 
                    TheRule = prop => 
                        {
                            string strProp = prop as String;

                            if (strProp != null)
                            {
                                return strProp.CompareTo(item) < 0;
                            }
                            return false;
                        },
                    JQueryFunction = @"
                            function LessThanOrEqual(value) {                                 
                                if (value == null || value == '') {
                                   return false;
                                }
            
                                return value <= """ + item + @""";                                
                            }
                            ",
                    JQueryFunctionName = "LessThanOrEqual",  
                        ErrorMessage = errorMessage 
                });
            }
            else
            {
                this.ObjRuleSet.Rules.Add(new Rule<object>
                {
                    TheRule = prop =>
                    {
                        string strProp = prop as String;

                        if (strProp != null)
                        {
                            return strProp.CompareTo(item) <= 0;
                        }
                        return false;
                    },
                    JQueryFunction = @"
                            function LessThan(value) { 
                                if (value == null || value == '') {
                                   return false;
                                }
            
                                return value < """ + item + @""";                                                             
                            }
                            ",
                    JQueryFunctionName = "LessThan",
                    ErrorMessage = errorMessage
                });
            }

            return this;
        }

        public RuleSet<T, TProperty> Between(int itemLower, int itemUpper, bool exclusive, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not between " + itemLower.ToString() + " and " + itemUpper.ToString();
            }

            if (exclusive)
            {
                this.ObjRuleSet.Rules.Add(new Rule<object> 
                { 
                    TheRule = prop => 
                    {
                        try
                        {
                            return itemLower < (int)prop && (int)prop < itemUpper;
                        }
                        catch
                        {
                            return false;
                        }
                    },
                    JQueryFunction = @"
                            function Between(value) {                                 
                                if (" + itemLower.ToString() + " < value < " + itemUpper.ToString() + @")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                    JQueryFunctionName = "Between", 
                    ErrorMessage = errorMessage 
                });
            }
            else
            {
                this.ObjRuleSet.Rules.Add(new Rule<object>
                {
                    TheRule = prop =>
                    {
                        try
                        {
                            return itemLower <= (int)prop && (int)prop <= itemUpper;
                        }
                        catch
                        {
                            return false;
                        }
                    },
                    JQueryFunction = @"
                            function Between(value) {                                 
                                if (" + itemLower.ToString() + " <= value <= " + itemUpper.ToString() + @")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                    JQueryFunctionName = "Between",
                    ErrorMessage = errorMessage
                });
            }

            return this;
        }

        public RuleSet<T, TProperty> Between(string itemLower, string itemUpper, bool exclusive, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not between " + itemLower + " and " + itemUpper;
            }

            if (exclusive)
            {
                this.ObjRuleSet.Rules.Add(new Rule<object> 
                { 
                    TheRule = prop => 
                        {
                            string strProp = prop as String;

                            if (strProp != null)
                            {
                                return strProp.CompareTo(itemLower) > 0 && strProp.CompareTo(itemUpper) < 0;
                            }
                            return false;
                        },
                    JQueryFunction = @"
                            function Between(value) {                                 
                                if (""" + itemLower.ToString() + @""" < value < """ + itemUpper.ToString() + @""")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                    JQueryFunctionName = "Between",  
                        ErrorMessage = errorMessage 
                });
            }
            else
            {
                this.ObjRuleSet.Rules.Add(new Rule<object>
                {
                    TheRule = prop =>
                    {
                        string strProp = prop as String;

                        if (strProp != null)
                        {
                            return strProp.CompareTo(itemLower) >= 0 && strProp.CompareTo(itemUpper) <= 0;
                        }
                        return false;
                    },
                    JQueryFunction = @"
                            function Between(value) {                                 
                                if (" + itemLower.ToString() + " <= value <= " + itemUpper.ToString() + @")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                    JQueryFunctionName = "Between",
                    ErrorMessage = errorMessage
                });
            }

            return this;
        }

        public RuleSet<T, TProperty> Between(DateTime itemLower, DateTime itemUpper, bool exclusive, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item not between " + itemLower.ToShortDateString() + " and " + itemUpper.ToShortDateString();
            }

            if (exclusive)
            {
                this.ObjRuleSet.Rules.Add(new Rule<object> 
                { 
                    TheRule = prop => 
                        {
                            try
                            {
                                return itemLower > (DateTime)prop && (DateTime)prop < itemUpper;
                            }
                            catch
                            {
                                return false;
                            }
                        },
                    JQueryFunction = @"
                            function Between(value) {                                 
                                if (" + itemLower.ToString() + @" < value < " + itemUpper.ToString() + @")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                    JQueryFunctionName = "Between",                         
                        ErrorMessage = errorMessage 
                });
            }
            else
            {
                this.ObjRuleSet.Rules.Add(new Rule<object>
                {
                    TheRule = prop =>
                    {
                        try
                        {
                            return itemLower >= (DateTime)prop && (DateTime)prop <= itemUpper;
                        }
                        catch
                        {
                            return false;
                        }
                    },
                    JQueryFunction = @"
                            function Between(value) {                                 
                                if (" + itemLower.ToString() + " <= value <= " + itemUpper.ToString() + @")
                                {    
                                    return true; 
                                }
                                return false;
                            }
                            ",
                    JQueryFunctionName = "Between",
                    ErrorMessage = errorMessage
                });
            }

            return this;
        }

        public RuleSet<T, TProperty> Matches(string regEx, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Item does not match the regEx";
            }

            this.ObjRuleSet.Rules.Add(new Rule<object> 
            { 
                TheRule = prop => 
                    {
                        try
                        {
                            return Helpers.Matches((string)prop, regEx);
                        }
                        catch
                        {
                            return false;
                        }                        
                    }, 
                ErrorMessage = errorMessage,
                JQueryFunction = @"
                            function Matches(value) { 
                                var str=value; 
                                var n=str.match(""" + regEx + @""");
                                if (n == null)
                                {    
                                    return false; 
                                }
                                return true;
                            }
                            ",
                JQueryFunctionName = "Matches"
            });

            return this;
        }

        public RuleSet<T, TProperty> JQueryRequired(string functionName, string errorMessage = "")
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "Required validation " + functionName + " failed";
            }

            this.ObjRuleSet.Rules.Add(new Rule<object>
            {
                TheRule = prop =>
                {
                    return true;
                },
                ErrorMessage = errorMessage,
                JQueryFunction = string.Empty,
                JQueryFunctionName = functionName
            });

            return this;
        }
        #endregion
    }
}
