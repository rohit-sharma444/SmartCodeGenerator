using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Helper;

/// <summary>
/// Helper class for extracting file paths from natural language input.
/// </summary>
public static class PathHelper
{
    public static string GetAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));

        return Path.GetFullPath(path);
    }

    public static string? ExtractPath(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var match = Regex.Match(
            input,
            @"([A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+\\?)*)",
            RegexOptions.IgnoreCase);

        return match.Success
            ? match.Groups[1].Value.TrimEnd('\\')
            : null;
    }
}