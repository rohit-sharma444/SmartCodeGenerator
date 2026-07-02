namespace CodeCommenterPro.Services;

public static class IgnoreService
{
    public static bool ShouldSkip(
        string file)
    {
        return
            file.EndsWith(".Designer.cs") ||
            file.EndsWith(".g.cs") ||
            file.EndsWith(".generated.cs") ||
            file.Contains("\\bin\\") ||
            file.Contains("\\obj\\");
    }
}