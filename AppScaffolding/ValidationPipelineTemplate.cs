namespace AppScaffolding;

/// <summary>Emits <c>Validation/ValidationPipeline.cs</c>: the stage ordering and the
/// short-circuit, in one place for the whole app.</summary>
/// <remarks>
/// A stage is a DELEGATE over a method on the generated validator, not a class. Seven classes per
/// entity would be seven files per entity, and an app whose entities have only field rules would
/// carry six empty ones each; a method list means an empty stage emits nothing at all, which is
/// what keeps a three-entity CRUD app's output no heavier than before this phase.
/// </remarks>
public static class ValidationPipelineTemplate
{
    /// <summary>Source of <c>Validation/ValidationPipeline.cs</c> in
    /// <paramref name="rootNamespace"/>.Validation.</summary>
    public static string Source(string rootNamespace) => $$"""
using System.Collections.Generic;

namespace {{rootNamespace}}.Validation;

/// <summary>Runs an entity's stages in order and stops at the first one that found anything.</summary>
public static class ValidationPipeline
{
    /// <summary>One stage: adds an error per rule it finds, and adds nothing when it finds none.</summary>
    public delegate void Stage<T>(ValidationContext<T> context, List<ValidationError> errors);

    /// <summary>
    /// Every error the first failing stage found, or a valid outcome when none failed.
    /// </summary>
    /// <remarks>
    /// A stage collects EVERY error it finds, so a form can show all its bad fields at once. The
    /// pipeline stops BETWEEN stages, which is what keeps a range check away from a value that is
    /// not a number, and what makes a posting gate a property of the ordering rather than an
    /// extra check: a Post commits only when every stage returned nothing.
    /// </remarks>
    public static ValidationOutcome Run<T>(Stage<T>[] stages, ValidationContext<T> context)
    {
        var errors = new List<ValidationError>();

        foreach (var stage in stages)
        {
            stage(context, errors);

            if (errors.Count > 0)
                return new ValidationOutcome(errors);
        }

        return ValidationOutcome.Valid;
    }
}
""";
}
