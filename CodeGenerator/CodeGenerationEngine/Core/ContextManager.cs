using CodeGenerationEngine.Models;
using CodeGenerationEngine.Semantics.Models;

namespace CodeGenerationEngine.Core;

public static class ContextManager
{
    private static readonly SemanticContext Context = new();

    public static SemanticContext Current => Context;

    public static void SetCurrentClass(string className)
    {
        Context.CurrentClass = className;
    }

    public static void SetCurrentProject(string project)
    {
        Context.CurrentProject = project;
    }

    public static void Clear()
    {
        Context.CurrentClass = null;
        Context.CurrentProject = null;
        Context.CurrentNamespace = null;
        Context.CurrentFile = null;
    }
}