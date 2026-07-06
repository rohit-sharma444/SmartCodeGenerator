namespace CodeGenerationEngine.Services;

public static class Tokenizer
{
    public static string[] Tokenize(string text)
    {
        return text
            .ToLower()
            .Replace(",", "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}