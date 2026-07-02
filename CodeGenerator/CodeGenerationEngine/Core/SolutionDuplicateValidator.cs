using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for validating that there are no duplicate class definitions in the solution.
/// </summary>
public static class SolutionDuplicateValidator
{
    public static List<DuplicateClassIssue> Validate(IEnumerable<string> sourceFiles)
    {
        var classes = new List<ClassInfo>();

        foreach (var file in sourceFiles)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
            classes.AddRange(ClassExtractor.Extract(tree, file));
        }

        return DuplicateClassDetector.FindDuplicates(classes);
    }
}