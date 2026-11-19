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
    /// <summary>
    /// The text of the source file at <paramref name="path"/>, decoded the way it is written: by
    /// its byte-order mark (UTF-8, UTF-16 or UTF-32, either byte order), else as strict UTF-8.
    /// </summary>
    /// <exception cref="NotUtf8Exception">
    /// The file has no byte-order mark and is not valid UTF-8 - a Windows-1252 file, say. Decoding
    /// it as UTF-8 anyway turns every byte above 0x7F into U+FFFD, and writing that back changes
    /// a string literal like <c>"Café"</c> without a word. An edit refuses the file instead.
    /// </exception>
    public static string Load(string path, out Encoding encoding, out string newline)
    {
        var bytes = File.ReadAllBytes(path);

        encoding = EncodingOf(bytes, out var preamble);

        string text;
        try
        {
            text = encoding.GetString(bytes, preamble, bytes.Length - preamble);
        }
        catch (DecoderFallbackException)
        {
            throw new NotUtf8Exception(path);
        }

        newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        return text;
    }

    /// <summary>The text of the source file at <paramref name="path"/>; see
    /// <see cref="Load(string, out Encoding, out string)"/>.</summary>
    public static string Load(string path) => Load(path, out _, out _);

    /// <summary>
    /// Writes <paramref name="text"/> in <paramref name="encoding"/>, with that encoding's
    /// byte-order mark when it has one: the encoding <see cref="Load(string, out Encoding, out string)"/>
    /// gave back.
    /// </summary>
    public static void Save(string path, string text, Encoding encoding) =>
        File.WriteAllBytes(path, [.. encoding.GetPreamble(), .. encoding.GetBytes(text)]);

    private static Encoding EncodingOf(byte[] bytes, out int preamble)
    {
        // UTF-32 LE before UTF-16 LE: FF FE 00 00 starts with the UTF-16 LE mark.
        if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00))
        {
            preamble = 4;
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true);
        }

        if (StartsWith(bytes, 0x00, 0x00, 0xFE, 0xFF))
        {
            preamble = 4;
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: true);
        }

        if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
        {
            preamble = 3;
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
        }

        if (StartsWith(bytes, 0xFF, 0xFE))
        {
            preamble = 2;
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true);
        }

        if (StartsWith(bytes, 0xFE, 0xFF))
        {
            preamble = 2;
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true);
        }

        preamble = 0;
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    }

    private static bool StartsWith(byte[] bytes, params byte[] mark) =>
        bytes.Length >= mark.Length && bytes.AsSpan(0, mark.Length).SequenceEqual(mark);

    /// <summary>
    /// Reads a file as UTF-8 with or without its mark, and never refuses it.
    /// </summary>
    /// <remarks>
    /// For files this tool generated itself. A file the user owns is read with
    /// <see cref="Load(string, out Encoding, out string)"/>, which refuses one that is not UTF-8
    /// rather than decoding it wrong.
    /// </remarks>
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
    /// they are given the file's. A file that mixes the two is left mixed. The file keeps its
    /// encoding too: a UTF-16 file is written back as UTF-16, not as UTF-8. A file that is not
    /// UTF-8 is not written at all (<see cref="NotUtf8Exception"/>).
    /// </remarks>
    public static void Rewrite(string path, string text)
    {
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        if (File.Exists(path))
        {
            var original = Load(path, out encoding, out _);
            text = InLineEndsOf(original, text);
        }

        Save(path, text, encoding);
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

/// <summary>
/// A file an edit refused because it is not UTF-8 and has no byte-order mark that says what it is.
/// </summary>
/// <remarks>
/// An <see cref="IOException"/>, so every edit path that already turns an I/O failure into a
/// refusal message shows this one without a change.
/// </remarks>
public sealed class NotUtf8Exception(string path)
    : IOException($"{Path.GetFileName(path)} is not UTF-8; save it as UTF-8 and try again. Nothing was changed.")
{
    public string FilePath { get; } = path;
}
