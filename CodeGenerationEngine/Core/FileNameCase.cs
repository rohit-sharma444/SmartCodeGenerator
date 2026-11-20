namespace CodeGenerationEngine.Core;

/// <summary>
/// The case of a file's name as it is on disk, which Windows keeps but does not compare.
/// </summary>
/// <remarks>
/// Copying a version back over a file whose name differs only in case keeps the name on disk: the
/// undo of <c>rename class customer to Customer</c> put back the old text but left the file called
/// Customer.cs, and a failed script's rollback did the same.
/// </remarks>
internal static class FileNameCase
{
    /// <summary>The name of the file at <paramref name="path"/> as the disk spells it, or the name
    /// in <paramref name="path"/> when there is no such file.</summary>
    public static string ActualName(string path)
    {
        var name = Path.GetFileName(path);
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return name;

        return Directory.EnumerateFiles(directory, name)
            .Select(Path.GetFileName)
            .FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ?? name;
    }

    /// <summary>
    /// When a file exists at <paramref name="path"/> under a name that differs only in case,
    /// renames it to the case <paramref name="path"/> gives - through a temporary name, as Windows
    /// requires.
    /// </summary>
    public static void Match(string path)
    {
        var wanted = Path.GetFileName(path);
        var actual = ActualName(path);
        if (string.Equals(actual, wanted, StringComparison.Ordinal) || !File.Exists(path))
            return;

        var directory = Path.GetDirectoryName(path)!;
        var temporary = Path.Combine(directory, Path.GetRandomFileName());
        File.Move(Path.Combine(directory, actual), temporary);
        File.Move(temporary, path);
    }

    /// <summary>Copies <paramref name="source"/> to <paramref name="destination"/>, giving the copy
    /// the case of the source's name on disk.</summary>
    public static void Copy(string source, string destination)
    {
        destination = Path.Combine(Path.GetDirectoryName(destination)!, ActualName(source));
        Match(destination);
        File.Copy(source, destination, overwrite: true);
    }
}
