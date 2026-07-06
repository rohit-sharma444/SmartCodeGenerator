using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for detecting circular dependencies in generated code.
/// </summary>
public static class CircularDependencyDetector
{
    public static List<List<string>> DetectCycles(DependencyGraph graph)
    {
        var cycles = new List<List<string>>();

        var visited = new HashSet<string>();

        var stack = new HashSet<string>();

        foreach (var node in graph.Dependencies.Keys)
        {
            Detect(node, graph, visited, stack, [], cycles);
        }

        return cycles;
    }

    private static void Detect(string node, DependencyGraph graph,
        HashSet<string> visited, HashSet<string> stack,
        List<string> path, List<List<string>> cycles)
    {
        if (stack.Contains(node))
        {
            int index = path.IndexOf(node);
            cycles.Add([.. path.Skip(index), node]);
            return;
        }

        if (!visited.Add(node))
            return;

        stack.Add(node);

        path.Add(node);

        if (graph.Dependencies.TryGetValue(node, out var dependencies))
        {
            foreach (var dependency in dependencies)
            {
                Detect(dependency, graph, visited, stack, path, cycles);
            }
        }

        stack.Remove(node);

        path.RemoveAt(path.Count - 1);
    }
}