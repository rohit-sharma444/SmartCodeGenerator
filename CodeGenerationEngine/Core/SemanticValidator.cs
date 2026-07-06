using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Provides functionality to perform semantic validation on C# source code using Roslyn.
/// </summary>
/// 
public static class SemanticValidator
{
    public static (bool,List<string>) Validate(SemanticModel model)
    {
        var result = true;
        var validationErrors = new List<string>();
        (result,validationErrors) = ValidateInheritance(model);
        (result,validationErrors) = ValidateDuplicateEntities(model);
        return (result, validationErrors);
    }

    private static (bool, List<string>) ValidateInheritance(SemanticModel model)
    {
        var validationErrors = new List<string>();
        foreach (var relation in model.Relations)
        {
            if (relation.RelationType != SemanticRelationType.Inherits)
                continue;

            bool sourceExists =
                model.Entities.Any(e =>
                    e.Name.Equals(
                        relation.Source,
                        StringComparison.OrdinalIgnoreCase));

            if (!sourceExists)
            {
                model.ValidationErrors.Add(
                    $"Class '{relation.Source}' not found.");
                return (false, validationErrors);
            }
        }
        return (true, validationErrors);
    }

    private static (bool, List<string>) ValidateDuplicateEntities(SemanticModel model)
    {
        var result = true;
        var validationErrors = new List<string>();

        var duplicates = model.Entities
                .GroupBy(x => x.Name)
                .Where(x => x.Count() > 1);

        foreach (var duplicate in duplicates)
        {
            model.ValidationErrors.Add(
                $"Duplicate entity '{duplicate.Key}'.");
            result = false;
        }
        return (result, validationErrors);
    }

}