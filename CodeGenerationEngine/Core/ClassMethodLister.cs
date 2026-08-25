using System.Text.RegularExpressions;
using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Utility class to list public methods of a specified class within a given workspace.
/// </summary>
public static class ClassMethodLister
{
    /// <summary>
    /// The public methods declared in a class file, as data.
    /// </summary>
    /// <remarks>
    /// Nothing is printed. <see cref="ListMethods"/> prints from this, and the chat page's project
    /// tree draws from it, so the tree and the command cannot disagree about what a class declares.
    /// <para>
    /// Regex, not Roslyn, because that is what this class has always used and changing it would
    /// change what <c>show methods</c> prints. It sees <c>public T Name(</c> and nothing else: no
    /// private members, no expression-bodied members, and a constructor reads as a method whose
    /// type is the class name.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<MethodSignature> MethodsIn(string classFile)
    {
        if (!File.Exists(classFile))
            return [];

        var code = File.ReadAllText(classFile);

        return
        [
            .. Regex.Matches(code, @"public\s+([\w<>]+)\s+(\w+)\s*\(")
                .Select(m => new MethodSignature(m.Groups[2].Value, m.Groups[1].Value))
        ];
    }

    /// <summary>
    /// The file a class lives in, or null.
    /// </summary>
    /// <remarks>
    /// First match by filename, which is the convention the whole tool works on: a class is a
    /// <c>.cs</c> file with its name. Build output is skipped, or a stale copy under <c>obj</c>
    /// wins the race against the source and the reader is shown yesterday's members.
    /// </remarks>
    public static string? FileOf(string searchRoot, string className)
    {
        if (string.IsNullOrWhiteSpace(searchRoot) || !Directory.Exists(searchRoot))
            return null;

        return Directory
            .GetFiles(searchRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                .Equals(className, StringComparison.OrdinalIgnoreCase));
    }

    public static bool ListMethods(string workspaceRoot, string className)
    {
        // Through the same lookup the tree uses. Two answers to "which file is Customer" that
        // could differ would defeat the point of sharing MethodsIn below.
        var file = FileOf(workspaceRoot, className);

        if (file == null)
        {
            Output.Warning($"Class '{className}' not found.");
            return false;
        }

        var methods = MethodsIn(file);

        if (methods.Count == 0)
        {
            Console.WriteLine("No methods found.");
            return false;
        }

        Console.WriteLine();
        Console.WriteLine($"Methods in {className}:");
        Console.WriteLine();

        foreach (var method in methods)
        {
            Console.WriteLine($"{method.ReturnType} {method.Name}()");
        }
        return true;
    }
}
