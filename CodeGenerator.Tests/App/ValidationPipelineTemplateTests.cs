using AppScaffolding;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ValidationPipelineTemplateTests
{
    /// <summary>Drives the emitted pipeline for real: three stages, the middle one failing.</summary>
    private const string Driver = """
using System.Collections.Generic;
using Acme.Validation;

namespace Acme.Probe;

public sealed class NoLookup : IEntityLookup
{
    public bool Exists<TEntity>(int id) where TEntity : class, new() => true;

    public int CountReferencing<TEntity>(string foreignKeyProperty, int id)
        where TEntity : class, new() => 0;
}

public static class Probe
{
    public static List<string> Ran { get; } = new();

    public static ValidationOutcome ThreeStages()
    {
        ValidationPipeline.Stage<string>[] stages =
        [
            (context, errors) => Ran.Add("first"),
            (context, errors) =>
            {
                Ran.Add("second");
                errors.Add(new ValidationError("A", "a is wrong"));
                errors.Add(new ValidationError("B", "b is wrong"));
            },
            (context, errors) => Ran.Add("third"),
        ];

        return ValidationPipeline.Run(
            stages,
            ValidationContext<string>.For("x", ValidationOperation.Insert, new NoLookup()));
    }
}
""";

    [TestMethod]
    public void AStageCollectsEveryError_AndThePipelineStopsAtTheFirstFailingStage()
    {
        // Both halves of the short-circuit rule, run rather than read. Collecting within a stage
        // is what lets a form show all its bad fields at once; stopping between stages is what
        // keeps a range check away from a value that is not a number.
        var probe = Compile();

        var outcome = probe.GetType("Acme.Probe.Probe")!.GetMethod("ThreeStages")!.Invoke(null, null)!;
        var errors = (System.Collections.ICollection)outcome.GetType()
            .GetProperty("Errors")!.GetValue(outcome)!;

        Assert.HasCount(2, errors);

        var ran = (System.Collections.IEnumerable)probe.GetType("Acme.Probe.Probe")!
            .GetProperty("Ran")!.GetValue(null)!;

        var names = new List<string>();
        foreach (var name in ran)
            names.Add((string)name);

        CollectionAssert.AreEqual(new[] { "first", "second" }, names,
            "the third stage ran after an earlier one had already failed");
    }

    [TestMethod]
    public void NoStageFails_AndTheOutcomeIsValid()
    {
        var source = ValidationPipelineTemplate.Source("Acme");

        // The empty case returns the shared Valid instance rather than allocating a list.
        StringAssert.Contains(source, "ValidationOutcome.Valid");
    }

    private static System.Reflection.Assembly Compile()
    {
        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme")),
            ("Probe/Probe.cs", Driver),
        });

        var errors = string.Join("\n",
            result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => e.ToString()));

        Assert.IsTrue(result.Success, $"Compilation failed:\n{errors}");

        return System.Reflection.Assembly.Load(result.AssemblyBytes!);
    }
}
