using System.Text;

namespace DataIngestion.Schema.Readers;

/// <summary>
/// A text file's bytes as text, in the encoding it was actually written in. Used by every reader
/// that takes a text file (CSV, TXT/TSV, JSON).
/// </summary>
/// <remarks>
/// <see cref="File.ReadAllText(string)"/> assumes UTF-8 and turns every byte that is not valid
/// UTF-8 into U+FFFD, so a CSV saved by Excel in Windows-1252 lost every accented letter without a
/// word: "Zürich" became "Z�rich" in the generated app. The order here is the one that can
/// never misread a valid file: a byte order mark says what it is; otherwise STRICT UTF-8 (any
/// UTF-8 file, including plain ASCII, passes it); and only a file strict UTF-8 refuses is read as
/// Windows-1252, the encoding Excel and Notepad save "ANSI" text in on a Western machine. It is
/// a superset of Latin-1 for every printable character, and every byte decodes to something, so
/// nothing is replaced — and the user is told which encoding was assumed.
/// </remarks>
public static class SourceText
{
    /// <summary>The fallback for a file that is not valid UTF-8.</summary>
    private const int Windows1252 = 1252;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The file at <paramref name="path"/> as text; <paramref name="note"/> says what
    /// was assumed when the file was not UTF-8, and is null otherwise.</summary>
    public static string Read(string path, out string? note)
        => Decode(File.ReadAllBytes(path), Path.GetFileName(path), out note);

    /// <inheritdoc cref="Read"/>
    public static string Decode(byte[] bytes, string label, out string? note)
    {
        note = null;

        if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
            return AfterMark(Encoding.UTF8, bytes, 3);

        // UTF-32 LE before UTF-16 LE: its mark begins with UTF-16's.
        if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00))
            return AfterMark(Encoding.UTF32, bytes, 4);

        if (StartsWith(bytes, 0xFF, 0xFE))
            return AfterMark(Encoding.Unicode, bytes, 2);

        if (StartsWith(bytes, 0xFE, 0xFF))
            return AfterMark(Encoding.BigEndianUnicode, bytes, 2);

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            note = $"{label} is not UTF-8, so it was read as Windows-1252 (the \"ANSI\" encoding "
                   + "Excel and Notepad save in). If any accented letters look wrong in the app, "
                   + "save the file as UTF-8 and build again.";

            return CodePagesEncodingProvider.Instance.GetEncoding(Windows1252)!.GetString(bytes);
        }
    }

    /// <summary>A file whose mark names its encoding is decoded in it; a byte the mark's encoding
    /// cannot read is the file's own damage, not a reason to guess another encoding.</summary>
    private static string AfterMark(Encoding encoding, byte[] bytes, int markLength)
        => encoding.GetString(bytes, markLength, bytes.Length - markLength);

    private static bool StartsWith(byte[] bytes, params byte[] mark)
        => bytes.Length >= mark.Length && bytes.AsSpan(0, mark.Length).SequenceEqual(mark);
}
