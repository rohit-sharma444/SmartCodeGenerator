namespace CodeCommenterPro.Helpers;

public static class VerbDictionary
{
    private static readonly Dictionary<string, string>
        Verbs = new()
        {
            {"Get","Gets"},
            {"Set","Sets"},
            {"Create","Creates"},
            {"Update","Updates"},
            {"Delete","Deletes"},
            {"Save","Saves"},
            {"Load","Loads"},
            {"Find","Finds"},
            {"Search","Searches"},
            {"Calculate","Calculates"},
            {"Validate","Validates"},
            {"Export","Exports"},
            {"Import","Imports"},
            {"Process","Processes"}
        };

    public static string ToSentence(string methodName)
    {
        foreach (var item in Verbs)
        {
            if (methodName.StartsWith(item.Key))
            {
                return item.Value +
                       methodName[item.Key.Length..];
            }
        }

        return methodName;
    }
}