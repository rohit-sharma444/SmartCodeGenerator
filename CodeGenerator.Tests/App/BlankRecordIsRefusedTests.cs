using System.Reflection;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// New then Save in every generated app stored an all-empty record, its dates 0001-01-01. Each
/// test compiles the model and its generated validator, then runs a record through it.
/// </summary>
[TestClass]
[DoNotParallelize]
public class BlankRecordIsRefusedTests
{
    [TestInitialize]
    public void Setup() => ValidationRegistry.Clear();

    [TestCleanup]
    public void Cleanup() => ValidationRegistry.Clear();

    private const string LookupThatFindsEverything = """
namespace Acme.Validation;

public sealed class StoreEntityLookup : IEntityLookup
{
    public bool Exists<TEntity>(int id) where TEntity : class, new() => true;
    public int CountReferencing<TEntity>(string foreignKeyProperty, int id) where TEntity : class, new() => 0;
    public bool AnyOtherWith<TEntity>(string propertyName, object? value, int exceptId) where TEntity : class, new() => false;
}
""";

    /// <summary>A discovered Book: no field is required, as the user described none.</summary>
    private static CandidateDomainModel Discovered()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Book",
            Fields =
            {
                new CandidateField { Name = "Title", Type = "string" },
                new CandidateField { Name = "Pages", Type = "int" },
                new CandidateField { Name = "PublishedOn", Type = "DateTime" },
            },
        });
        return model;
    }

    [TestMethod]
    public void A_record_with_nothing_filled_in_is_refused()
    {
        var messages = Validate(Discovered(), "Book", _ => { }, "Insert");

        Assert.IsNotEmpty(messages, "a blank Book was accepted");
    }

    [TestMethod]
    public void A_record_with_one_field_filled_in_is_accepted()
    {
        // Nothing here is required, so one value is enough: the check is about a record with
        // none, not a new rule on any one field.
        var messages = Validate(Discovered(), "Book", book => Set(book, "Title", "Emma"), "Insert");

        Assert.IsEmpty(messages, string.Join("\n", messages));
    }

    [TestMethod]
    public void A_blank_record_can_still_be_deleted()
    {
        var messages = Validate(Discovered(), "Book", _ => { }, "Delete");

        Assert.IsEmpty(messages, string.Join("\n", messages));
    }

    /// <summary>A date the source declared NOT NULL is required, as a NOT NULL string already
    /// is: left blank it was saved as 0001-01-01, a value no NOT NULL column means.</summary>
    [TestMethod]
    public void A_not_null_date_left_blank_is_refused()
    {
        var model = Discovered();
        model.Entities[0].Fields.Single(f => f.Name == "PublishedOn").Facts = new TypeFacts { IsNullable = false };

        var messages = Validate(model, "Book", book => Set(book, "Title", "Emma"), "Insert");

        Assert.IsTrue(messages.Any(m => m.Contains("PublishedOn is required")), string.Join("\n", messages));
    }

    [TestMethod]
    public void A_not_null_date_filled_in_is_accepted()
    {
        var model = Discovered();
        model.Entities[0].Fields.Single(f => f.Name == "PublishedOn").Facts = new TypeFacts { IsNullable = false };

        var messages = Validate(model, "Book", book =>
        {
            Set(book, "Title", "Emma");
            Set(book, "PublishedOn", new DateTime(1815, 12, 23));
        }, "Insert");

        Assert.IsEmpty(messages, string.Join("\n", messages));
    }

    private static void Set(object item, string property, object value)
        => item.GetType().GetProperty(property)!.SetValue(item, value);

    private static IReadOnlyList<string> Validate(
        CandidateDomainModel model, string className, Action<object> fill, string operation)
    {
        var classes = DomainModelMapper.ToClassDefinitions(model);
        var definition = classes.Single(c => c.Name == className);

        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("GlobalUsings.cs", "global using System;\nglobal using System.Collections.Generic;"),
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", [], login: false)),
            ("Validation/StoreEntityLookup.cs", LookupThatFindsEverything),
            ("Models/" + className + ".cs", RoslynClassGenerator.Generate(definition, "Acme.Models")),
            ("Validation/" + className + "Validator.Generated.cs",
                ValidatorTemplate.Generated("Acme", definition, [], referencesRequired: false)),
            ("Validation/" + className + "Validator.Custom.cs",
                ValidatorTemplate.CustomStub("Acme", className)),
        });

        var compileErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        Assert.IsEmpty(compileErrors, "Compilation failed:\n" + string.Join("\n", compileErrors));

        var assembly = Assembly.Load(result.AssemblyBytes!);
        var entityType = assembly.GetType("Acme.Models." + className)!;
        var item = Activator.CreateInstance(entityType)!;
        fill(item);

        var lookupType = assembly.GetType("Acme.Validation.StoreEntityLookup")!;
        var validatorType = assembly.GetType("Acme.Validation." + className + "Validator")!;
        var validator = Activator.CreateInstance(validatorType)!;
        var contextFor = assembly.GetType("Acme.Validation.ValidationContext`1")!
            .MakeGenericType(entityType).GetMethod("For")!;
        var op = assembly.GetType("Acme.Validation.ValidationOperation")!
            .GetField(operation)!.GetValue(null)!;

        var context = contextFor.Invoke(null, [item, op, Activator.CreateInstance(lookupType)]);
        var outcome = validatorType.GetMethod("Validate")!.Invoke(validator, [context])!;

        return (IReadOnlyList<string>)outcome.GetType().GetProperty("Messages")!.GetValue(outcome)!;
    }
}
