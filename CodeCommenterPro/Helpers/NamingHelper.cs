namespace CodeCommenterPro.Helpers;

using System.Text.RegularExpressions;

public static class NamingHelper
{
    public static string SplitCamelCase(
        string value)
    {
        return Regex.Replace(
            value,
            "([a-z])([A-Z])",
            "$1 $2");
    }
}