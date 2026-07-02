using System;
using System.Linq.Expressions;
using System.Text.RegularExpressions;

namespace PROJECTBUILDER.MyFluent;
/// <summary>
/// Represents Helpers.
/// </summary>

internal static class Helpers
{
    //Regex complaint with RFC 2822. http://www.regular-expressions.info/email.html
    public const string regExEmail = @"(?:[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*|""(?:[\x01-\x08\x0b\x0c\x0e-\x1f\x21\x23-\x5b\x5d-\x7f]|\\[\x01-\x09\x0b\x0c\x0e-\x7f])*"")@(?:(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?|\[(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?|[a-z0-9-]*[a-z0-9]:(?:[\x01-\x08\x0b\x0c\x0e-\x1f\x21-\x5a\x53-\x7f]|\\[\x01-\x09\x0b\x0c\x0e-\x7f])+)\])";

    public const string regExCreditCard = @"^(?:4[0-9]{12}(?:[0-9]{3})?|5[1-5][0-9]{14}|6(?:011|5[0-9][0-9])[0-9]{12}|3[47][0-9]{13}|3(?:0[0-5]|[68][0-9])[0-9]{11}|(?:2131|1800|35\d{3})\d{11})$";

    //Regex complaint with ISO 4217 Currency Codes. http://www.xe.com/iso4217.php
    public const string regExISOCurrencyCodes = @"^(AED|AFN|ALL|AMD|ANG|AOA|ARS|AUD|AWG|AZN|BAM|BBD|BDT|BGN|BHD|BIF|BMD|BND|BOB|BRL|BSD|BTN|BWP|BYR|BZD|CAD|CDF|CHF|CLP|CNY|COP|CRC|CUC|CUP|CVE|CZK|DJF|DKK|DOP|DZD|EGP|ERN|ETB|EUR|FJD|FKP|GBP|GEL|GGP|GHS|GIP|GMD|GNF|GTQ|GYD|HKD|HNL|HRK|HTG|HUF|IDR|ILS|IMP|INR|IQD|IRR|ISK|JEP|JMD|JOD|JPY|KES|KGS|KHR|KMF|KPW|KRW|KWD|KYD|KZT|LAK|LBP|LKR|LRD|LSL|LTL|LVL|LYD|MAD|MDL|MGA|MKD|MMK|MNT|MOP|MRO|MUR|MVR|MWK|MXN|MYR|MZN|NAD|NGN|NIO|NOK|NPR|NZD|OMR|PAB|PEN|PGK|PHP|PKR|PLN|PYG|QAR|RON|RSD|RUB|RWF|SAR|SBD|SCR|SDG|SEK|SGD|SHP|SLL|SOS|SPL|SRD|STD|SVC|SYP|SZL|THB|TJS|TMT|TND|TOP|TRY|TTD|TVD|TWD|TZS|UAH|UGX|USD|UYU|UZS|VEF|VND|VUV|WST|XAF|BEA|CFA|BEA|XCD|XDR|IMF|XOF|BCE|XPF|CFP|YER|ZAR|ZMK|ZWD)$";

    public static Expression<Func<TInput, object>> Convert<TInput, TOutput>(Expression<Func<TInput, TOutput>> expression)
    {
        Expression converted = Expression.Convert(expression.Body, typeof(object));

        return Expression.Lambda<Func<TInput, object>>(converted, expression.Parameters);
    }

    public static Func<object, bool> Convert<TProperty>(Func<TProperty, bool> func)
    {
        if (func == null) return null;

        else return new Func<object, bool>(o => func((TProperty)o));
    }

    public static bool VerifyWithLuhn(string cardNumber)
    {
        byte[] number = new byte[16]; // number to validate
        // Remove non-digits
        int len = 0;

        for (int i = 0; i < cardNumber.Length; i++)
        {
            if (char.IsDigit(cardNumber, i))
            {
                if (len == 16) return false; // number has too many digits

                number[len++] = byte.Parse(cardNumber[i].ToString());
            }
        }

        // Use Luhn Algorithm to validate
        int sum = 0;

        for (int i = len - 1; i >= 0; i--)
        {
            if (i % 2 == len % 2)
            {
                int n = number[i] * 2;

                sum += n / 10 + n % 10;
            }
            else
                sum += number[i];
        }

        return sum % 10 == 0;
    }

    public static bool IsAlphaNumeric(string stringToTest, string alphaUnicodeRange = "a-zA-Z", string numericUnicodeRange = "0-9")
    {
        return Regex.Match(stringToTest, @"^(?=[" + numericUnicodeRange + @"]*?[" + alphaUnicodeRange + @"]+)(?=[" + alphaUnicodeRange + @"]*?[" + numericUnicodeRange + @"]+)[" + alphaUnicodeRange + numericUnicodeRange + @"]+$").Success;
    }

    public static bool IsNumeric(string stringToTest, string numericUnicodeRange = "0-9")
    {
        return Regex.Match(stringToTest, @"^[" + numericUnicodeRange + @"]+$").Success;
    }

    public static bool IsAlpha(string stringToTest, string alphaUnicodeRange = "a-zA-Z")
    {
        return Regex.Match(stringToTest, @"^[" + alphaUnicodeRange + @"]+$").Success;
    }

    public static bool ValidateEmail(string email)
    {
        return Regex.Match(email, regExEmail).Success;
    }

    public static bool ValidateCurrencyCode(string currencyCode)
    {
        return Regex.Match(currencyCode, regExISOCurrencyCodes).Success;
    }

    public static bool ValidateCreditCard(string creditCardNo)
    {
        return Regex.Match(creditCardNo, regExCreditCard).Success;
    }

    public static bool Matches(string input, string regEx)
    {
        return Regex.Match(input, regEx).Success;
    }
}
