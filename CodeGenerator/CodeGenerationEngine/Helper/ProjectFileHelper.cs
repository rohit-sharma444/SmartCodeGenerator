namespace CodeGenerationEngine.Core;

public static class ProjectFileHelper
{
    public static string? FindProjectDirectory(string filePath)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(filePath)!);

        while (dir != null)
        {
            if (dir.GetFiles("*.csproj").Length > 0)
                return dir.FullName;

            dir = dir.Parent;
        }

        return null;
    }
}