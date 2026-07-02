namespace CodeGenerationEngine.Helper;

public static class CommandCatalog
{
    public static readonly List<string> Commands =
    [
        "help",
        "demo",

        "create class",
        "delete class",
        "restore class",
        "restore last class",
        "list deleted classes",

        "list classes",
        "show class",
        "rename class",

        "add property",
        "list properties",

        "add method",
        "list methods",

        "create service",
        "create repository",
        "create interface",

        "create controller",
        "create api",

        "create console project",
        "create class library",
        "create webapi project",
        "create blazor project",
        "create worker service",
        "create wpf project",

        "list projects",
        "use project",
        "current project",

        "add validation",
        "generate validation"
    ];
}