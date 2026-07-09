using System;
using System.IO;
using CodeGenerationEngine.Helper;

namespace CodeGenerator.Tests.Helper;

[TestClass]
public class CommandHistoryFileTests
{
    private static string NewHistoryPath()
        => Path.Combine(Path.GetTempPath(), "history_" + Path.GetRandomFileName());

    [TestMethod]
    public void AppendLine_ThenReadLines_RoundTripsInOrder()
    {
        var path = NewHistoryPath();
        try
        {
            CommandHistoryFile.AppendLine(path, "create console project Shop");
            CommandHistoryFile.AppendLine(path, "use project Shop");

            var lines = CommandHistoryFile.ReadLines(path);

            Assert.HasCount(2, lines);
            Assert.AreEqual("create console project Shop", lines[0]);
            Assert.AreEqual("use project Shop", lines[1]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ReadLines_MissingFile_ReturnsEmpty()
        => Assert.IsEmpty(CommandHistoryFile.ReadLines(NewHistoryPath()));

    [TestMethod]
    public void ReadLines_SkipsBlankLines()
    {
        var path = NewHistoryPath();
        try
        {
            File.WriteAllText(path, "help\n\n   \nexit\n");
            var lines = CommandHistoryFile.ReadLines(path);

            Assert.HasCount(2, lines);
            Assert.AreEqual("help", lines[0]);
            Assert.AreEqual("exit", lines[1]);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Exactly what the retired AES helper wrote: base64 of a 16-byte-block
    /// ciphertext, one per line.
    /// </summary>
    private const string LegacyCipherLine = "d2LrQoQe6mrH/kqZUZ8sPPjPMOtqzY5ZQO0wCVfF2xU=";

    [TestMethod]
    public void ReadLines_LegacyEncryptedLines_AreSkipped()
    {
        var path = NewHistoryPath();
        try
        {
            File.WriteAllText(path, LegacyCipherLine + "\n");
            Assert.IsEmpty(CommandHistoryFile.ReadLines(path));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ReadLines_OneLegacyLine_DoesNotDiscardTheRestOfTheFile()
    {
        // The old loader caught broadly and cleared the whole list, so a single
        // undecryptable line threw away every command the user had ever typed.
        var path = NewHistoryPath();
        try
        {
            File.WriteAllText(path, $"create class Product\n{LegacyCipherLine}\nuse project Shop\n");

            var lines = CommandHistoryFile.ReadLines(path);

            Assert.HasCount(2, lines);
            Assert.AreEqual("create class Product", lines[0]);
            Assert.AreEqual("use project Shop", lines[1]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ReadLines_ShortCommandsThatLookLikeBase64_AreKept()
    {
        // "help" and "exit" are valid base64 alphabet and length % 4 == 0. Only a
        // ciphertext-length token should be treated as legacy.
        var path = NewHistoryPath();
        try
        {
            File.WriteAllText(path, "help\nexit\nlist\n");
            var lines = CommandHistoryFile.ReadLines(path);

            Assert.HasCount(3, lines);
            CollectionAssert.AreEqual(new[] { "help", "exit", "list" }, lines.ToArray());
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void AppendLine_CommandContainingNewline_DoesNotCorruptTheFile()
    {
        var path = NewHistoryPath();
        try
        {
            CommandHistoryFile.AppendLine(path, "create class A\nrm -rf /");
            CommandHistoryFile.AppendLine(path, "exit");

            var lines = CommandHistoryFile.ReadLines(path);

            // The embedded newline must not become a second history entry.
            Assert.HasCount(2, lines);
            Assert.AreEqual("exit", lines[1]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void AppendLine_UnwritablePath_Throws()
    {
        // Callers wrap this in try/catch: history is best-effort, never fatal.
        var directoryNotFile = Path.Combine(Path.GetTempPath(), "hist_dir_" + Path.GetRandomFileName());
        Directory.CreateDirectory(directoryNotFile);
        try
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(
                () => CommandHistoryFile.AppendLine(directoryNotFile, "help"));
        }
        finally { Directory.Delete(directoryNotFile); }
    }
}
