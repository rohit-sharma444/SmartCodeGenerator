using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

public static class ContextResolver
{
    public static void Resolve(SemanticModel model)
    {
        if (model.Context is null)
            return;

        if (model.Entities.Count > 0)
        {
            model.Context.LastEntity =
                model.Entities.Last().Name;
        }

        foreach (var entity in model.Entities)
        {
            if (string.IsNullOrWhiteSpace(entity.ParentName))
                entity.ParentName = model.Context.LastEntity;
        }
    }
}