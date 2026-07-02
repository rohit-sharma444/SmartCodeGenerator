namespace CodeGenerationEngine.Core;

public static class SessionContext
{
    public static string? CurrentProject { get; set; }

    public static string? CurrentClass { get; set; }

    public static string? CurrentNamespace { get; set; }

    public static void Clear()
    {
        CurrentProject = null;
        CurrentClass = null;
        CurrentNamespace = null;
    }
}