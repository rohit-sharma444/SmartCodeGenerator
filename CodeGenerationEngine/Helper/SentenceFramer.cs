namespace CodeGenerationEngine.Helper;

/// <summary>
/// Utility to ensure sentences are properly framed for NLP processing (e.g. ending with a period, capitalized).
/// </summary>
public static class SentenceFramer
{
    public static string Frame(string sentence, string lookupValue, List<string> values)
    {
        if (string.IsNullOrWhiteSpace(sentence) || string.IsNullOrWhiteSpace(lookupValue))
            return string.Empty;

        int index = sentence.IndexOf(lookupValue, StringComparison.OrdinalIgnoreCase);

        if (index < 0)
            return sentence;

        return sentence.Substring(0, index + lookupValue.Length).Trim() +
               " " + string.Join(", ", values);
    }
}
