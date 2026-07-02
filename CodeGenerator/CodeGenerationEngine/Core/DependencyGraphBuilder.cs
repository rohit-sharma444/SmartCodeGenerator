using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Builds a dependency graph of classes based on their field and constructor dependencies.
/// </summary>
public static class DependencyGraphBuilder
{
    public static DependencyGraph Build(Compilation compilation)
    {
        var graph = new DependencyGraph();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);

            var root = tree.GetRoot();

            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

            foreach (var cls in classes)
            {
                var symbol = model.GetDeclaredSymbol(cls);

                if (symbol == null)
                    continue;

                AddClassDependencies((INamedTypeSymbol)symbol, graph);
            }
        }

        return graph;
    }

    private static void AddClassDependencies(INamedTypeSymbol classSymbol, DependencyGraph graph)
    {
        string className = classSymbol.ToDisplayString();

        if (!graph.Dependencies.ContainsKey(className))
        {
            graph.Dependencies[className] = [];
        }

        foreach (var member in classSymbol.GetMembers())
        {
            if (member is IFieldSymbol field)
            {
                graph.Dependencies[className]
                    .Add(field.Type.ToDisplayString());
            }
        }
    }

    private static void AddConstructorDependencies(INamedTypeSymbol classSymbol, DependencyGraph graph)
    {
        string source = classSymbol.ToDisplayString();

        foreach (var ctor in classSymbol.Constructors)
        {
            foreach (var parameter in ctor.Parameters)
            {
                graph.Dependencies[source]
                    .Add(parameter.Type.ToDisplayString());
            }
        }
    }
}