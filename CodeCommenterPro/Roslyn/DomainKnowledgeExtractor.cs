using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public sealed class DomainKnowledgeExtractor
{
    public DomainModel Build(
        Compilation compilation)
    {
        var model = new DomainModel();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel =
                compilation.GetSemanticModel(tree);

            var root =
                tree.GetRoot();

            foreach (var cls in root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>())
            {
                AddConcept(
                    model,
                    cls.Identifier.Text);

                foreach (var property in cls.Members
                    .OfType<PropertyDeclarationSyntax>())
                {
                    AddRelationship(
                        model,
                        cls.Identifier.Text,
                        property.Identifier.Text);
                }

                foreach (var method in cls.Members
                    .OfType<MethodDeclarationSyntax>())
                {
                    AddRelationship(
                        model,
                        cls.Identifier.Text,
                        method.Identifier.Text);
                }
            }
        }

        return model;
    }

    private static void AddRelationship(
    DomainModel model,
    string source,
    string target)
    {
        if (!model.Concepts.TryGetValue(
                source,
                out var sourceConcept))
        {
            sourceConcept = new DomainConcept
            {
                Name = source
            };

            model.Concepts[source] = sourceConcept;
        }

        if (!model.Concepts.ContainsKey(target))
        {
            model.Concepts[target] =
                new DomainConcept
                {
                    Name = target
                };
        }

        sourceConcept.RelatedConcepts.Add(target);
    }


    private static void AddConcept(
    DomainModel model,
    string name)
    {
        if (!model.Concepts.TryGetValue(
                name,
                out var concept))
        {
            concept = new DomainConcept
            {
                Name = name
            };

            model.Concepts[name] = concept;
        }

        concept.UsageCount++;
    }

}