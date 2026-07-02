namespace CodeGenerationEngine.Helper;

public static class FileHelper
{
    public static IEnumerable<string> GetActiveClassFiles(string rootPath)
    {
        return Directory.GetFiles(rootPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(
                    $"{Path.DirectorySeparatorChar}DeletedClasses{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase));
    }
}