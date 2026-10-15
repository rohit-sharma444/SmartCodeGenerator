using System.IO;
using System.Text;

namespace CodeGenerationEngine.Core.Validation;

/// <summary>
/// Reading and writing a source file without changing anything about it that is not the edit.
/// </summary>
/// <remarks>
/// <c>File.ReadAllText</c> followed by <c>File.WriteAllText</c> silently drops a byte-order mark,
/// and a file written with the default newline lands LF-only in a CRLF checkout. Both have been
/// real defects here, and neither shows up in a diff you skim.
/// </remarks>
public static class SourceFileText
{
    public static string Read(string path, out bool hasBom, out string newline)
    {
        var bytes = File.ReadAllBytes(path);

        hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        var text = new UTF8Encoding(false).GetString(hasBom ? bytes[3..] : bytes);

        newline = text.Contains("\r\n") ? "\r\n" : "\n";

        return text;
    }

    public static void Write(string path, string text, bool hasBom) =>
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: hasBom));

    /// <summary>
    /// Writes <paramref name="text"/> over the file at <paramref name="path"/> the way the file was
    /// written: with its byte-order mark when it had one, and with its line ends when it used one
    /// kind throughout. A new file gets no byte-order mark.
    /// </summary>
    /// <remarks>
    /// For an edit made on text read with <c>File.ReadAllText</c>, which drops the mark. Lines an
    /// edit adds come with the generator's own line end; in a file that is all CRLF (or all LF)
    /// they are given the file's. A file that mixes the two is left mixed.
    /// </remarks>
    public static void Rewrite(string path, string text)
    {
        var hasBom = false;

        if (File.Exists(path))
        {
            var original = Read(path, out hasBom, out _);
            text = InLineEndsOf(original, text);
        }

        Write(path, text, hasBom);
    }

    /// <summary><paramref name="text"/> with the line ends of <paramref name="original"/>, when
    /// that uses only one kind.</summary>
    public static string InLineEndsOf(string original, string text)
    {
        var crlf = CountCrlf(original);
        var lf = original.Count(c => c == '\n');

        if (lf == 0)
            return text;

        // Every line end CRLF: lone LFs in the new text become CRLF.
        if (crlf == lf)
            return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);

        // Every line end LF.
        if (crlf == 0)
            return text.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text;
    }

    private static int CountCrlf(string text)
    {
        var count = 0;
        for (var i = text.IndexOf("\r\n", StringComparison.Ordinal); i >= 0; i = text.IndexOf("\r\n", i + 2, StringComparison.Ordinal))
            count++;
        return count;
    }
}
