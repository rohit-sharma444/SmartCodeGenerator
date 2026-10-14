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
}
