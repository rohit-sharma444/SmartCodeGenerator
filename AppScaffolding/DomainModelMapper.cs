using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace AppScaffolding;

/// <summary>Maps an approved domain model to CodeGenerationEngine class definitions.</summary>
public static class DomainModelMapper
{
    public static IReadOnlyList<ClassDefinition> ToClassDefinitions(CandidateDomainModel model)
    {
        var classes = new List<ClassDefinition>();

        foreach (var entity in model.Entities)
        {
            var definition = new ClassDefinition { Name = entity.Name };

            if (entity.Fields.Count == 0)
            {
                // Field-less entities get a primary key so the class isn't empty.
                definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });
            }
            else
            {
                foreach (var field in entity.Fields)
                    definition.Properties.Add(
                        new PropertyDefinition { Name = field.Name, Type = field.Type });
            }

            classes.Add(definition);
        }

        return classes;
    }
}
