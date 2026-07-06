using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Provides functionality to detect duplicate class definitions in the parsed code.
/// </summary>
public static class DuplicateClassDetector
{
    public static List<DuplicateClassIssue> FindDuplicates(IEnumerable<ClassInfo> classes)
    {
        var result = new List<DuplicateClassIssue>();

        var duplicates = classes
                .GroupBy(x =>
                    $"{x.Namespace}.{x.Name}")
                .Where(g =>
                    g.Count() > 1);

        foreach (var duplicate in duplicates)
        {
            bool allPartial = duplicate.All(x => x.IsPartial);

            if (allPartial)
                continue;

            foreach (var cls in duplicate)
            {
                result.Add(new DuplicateClassIssue
                {
                    Namespace = cls.Namespace,

                    ClassName = cls.Name,

                    FileName = cls.FilePath,

                    Message = $"Duplicate class {cls.Name}"
                });
            }
        }

        return result;
    }
}