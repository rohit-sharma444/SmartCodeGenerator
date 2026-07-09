namespace CodeGenerationEngine.Helper;

/// <summary>
/// Reads and appends the CLI's command history, one command per line, in plain text.
/// </summary>
/// <remarks>
/// This file used to be AES-encrypted under a key hardcoded in the source, which
/// protected it from nobody while implying it held something worth protecting. It
/// holds the same thing <c>.bash_history</c> does — the commands you typed — so it
/// is stored the same way, and <c>.codegen_history</c> is gitignored.
/// </remarks>
public static class CommandHistoryFile
{
    /// <summary>
    /// Appends <paramref name="command"/> as a single line. Embedded newlines are
    /// folded to spaces so one command can never become two history entries.
    /// </summary>
    /// <exception cref="IOException">The line could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The path is not writable.</exception>
    public static void AppendLine(string filePath, string command)
    {
        var single = command.ReplaceLineEndings(" ");
        File.AppendAllText(filePath, single + Environment.NewLine);
    }

    /// <summary>
    /// Returns the commands in the file, oldest first. A missing or unreadable file
    /// yields an empty history rather than throwing, and an individual line that
    /// cannot be used is skipped rather than discarding the ones around it.
    /// </summary>
    public static IReadOnlyList<string> ReadLines(string filePath)
    {
        if (!File.Exists(filePath))
            return Array.Empty<string>();

        try
        {
            return File.ReadLines(filePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Where(line => !IsLegacyCipherText(line))
                .Select(line => line.Trim())
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// True for a line written by the retired AES helper: base64 of a whole number
    /// of 16-byte cipher blocks. Real commands either contain a space or are far
    /// shorter than one block, so this never mistakes `help` or `exit` for cipher text.
    /// </summary>
    private static bool IsLegacyCipherText(string line)
    {
        const int Base64CharsPerAesBlock = 24;

        if (line.Length < Base64CharsPerAesBlock || line.Length % 4 != 0)
            return false;

        foreach (var c in line)
        {
            var isBase64Char = char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=';
            if (!isBase64Char)
                return false;
        }

        var decoded = new byte[line.Length / 4 * 3];
        return Convert.TryFromBase64String(line, decoded, out var byteCount)
            && byteCount >= 16
            && byteCount % 16 == 0;
    }
}
