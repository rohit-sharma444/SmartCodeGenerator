using System.Reflection;
using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The generated tests' TestSupport.Fill writes values the app's OWN generated validator accepts,
/// even after the user has accepted rules from the data. It wrote "Test" into every string, so
/// once a value list (Status: Open, InProgress, Closed) was accepted, every generated app failed
/// its own A_populated_Ticket_is_valid and Ticket_round_trips tests. Each test here compiles the
/// model, its generated validator and the generated TestSupport, then runs Fill.
/// </summary>
[TestClass]
[DoNotParallelize]
public class FilledRecordsPassAcceptedRulesTests
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

    private static RuleProposal Accepted(RuleProposalKind kind, PropertyRule rule)
        => new() { Kind = kind, Rule = rule, Evidence = "in all rows", Accepted = true };

    private static RuleProposal ValueList(string field, params string[] values)
        => Accepted(RuleProposalKind.Status,
            new PropertyRule { PropertyName = field, Name = "allowedvalues", AllowedValues = [.. values] });

    private static RuleProposal Pattern(string field, string name, string regex)
        => Accepted(RuleProposalKind.Pattern,
            new PropertyRule { PropertyName = field, Name = "pattern", RegexPattern = regex, CustomRule = name });

    private static RuleProposal Unique(string field)
        => Accepted(RuleProposalKind.Unique,
            new PropertyRule { PropertyName = field, Name = "unique", IsUnique = true });

    /// <summary>A Ticket imported from a file, with every kind of proposal the scan makes taken.</summary>
    private static CandidateDomainModel Tickets()
    {
        var title = new CandidateField { Name = "Title", Type = "string" };
        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(ValueList("Status", "Open", "InProgress", "Closed"));
        var priority = new CandidateField { Name = "Priority", Type = "string" };
        priority.Proposals.Add(ValueList("Priority", "Low", "Medium", "High"));
        var postcode = new CandidateField { Name = "Postcode", Type = "string" };
        postcode.Proposals.Add(Pattern("Postcode", "postcode",
            @"^[A-Za-z]{1,2}[0-9][0-9A-Za-z]?\s*[0-9][A-Za-z]{2}$"));
        var contact = new CandidateField { Name = "Contact", Type = "string" };
        contact.Proposals.Add(Pattern("Contact", "email address", @"^[^@\s]+@[^@\s]+\.[^@\s]+$"));
        contact.Proposals.Add(Unique("Contact"));
        var code = new CandidateField { Name = "Code", Type = "string" };
        code.Proposals.Add(Unique("Code"));
        var openedOn = new CandidateField { Name = "OpenedOn", Type = "DateTime" };
        openedOn.Proposals.Add(Unique("OpenedOn"));

        var model = new CandidateDomainModel();
        var ticket = new CandidateEntity { Name = "Ticket" };
        ticket.Fields.AddRange([title, status, priority, postcode, contact, code, openedOn]);
        model.Entities.Add(ticket);
        return model;
    }

    [TestMethod]
    public void A_filled_record_passes_accepted_value_lists_and_patterns()
    {
        var app = Compile(Tickets(), "Ticket");
        var item = app.Fill();

        var messages = app.Validate(item);

        Assert.IsEmpty(messages, string.Join("\n", messages));
        Assert.AreEqual("Open", app.Get(item, "Status"), "the first allowed value");
    }

    /// <summary>
    /// A column no two rows share gets a different value on every Fill. Fill's foreign-key row
    /// stays in the store, so a second record filled the same way was refused as a duplicate of it.
    /// </summary>
    [TestMethod]
    public void Two_filled_records_do_not_share_a_value_that_must_be_unique()
    {
        var app = Compile(Tickets(), "Ticket");
        var first = app.Fill();
        var second = app.Fill();

        foreach (var name in new[] { "Contact", "Code", "OpenedOn" })
            Assert.AreNotEqual(app.Get(first, name), app.Get(second, name), name);

        Assert.IsEmpty(app.Validate(second), string.Join("\n", app.Validate(second)));
    }

    /// <summary>A number with a floor above zero, and a string shorter than "Test" allows.</summary>
    [TestMethod]
    public void A_filled_record_passes_a_minimum_above_zero_and_a_short_maximum_length()
    {
        var classes = new List<ClassDefinition>
        {
            new()
            {
                Name = "Part",
                Usings = { "System.ComponentModel.DataAnnotations" },
                Properties =
                {
                    new PropertyDefinition { Name = "Id", Type = "int" },
                    new PropertyDefinition
                    {
                        Name = "Size", Type = "string",
                        Rules = { new PropertyRule { Name = "maxlength", MaxLength = 2 } },
                    },
                    new PropertyDefinition
                    {
                        Name = "Quantity", Type = "int",
                        Rules = { new PropertyRule { Name = "minvalue", MinValue = 1 } },
                    },
                    new PropertyDefinition
                    {
                        Name = "Weight", Type = "decimal",
                        Rules = { new PropertyRule { Name = "minvalue", MinValue = 0.5m } },
                    },
                },
            },
        };

        var app = Compile(classes, "Part");
        var item = app.Fill();

        var messages = app.Validate(item);
        Assert.IsEmpty(messages, string.Join("\n", messages));
    }

    /// <summary>
    /// A class of only dates and numbers is not blank once filled. Fill wrote today into every
    /// date and left numbers at 0, and today is what the blank-record check treats as unfilled --
    /// a new record's dates start there -- so such a class failed its own A_populated_..._is_valid.
    /// </summary>
    [TestMethod]
    public void A_filled_record_of_only_dates_and_numbers_is_not_blank()
    {
        var classes = new List<ClassDefinition>
        {
            new()
            {
                Name = "Reading",
                Properties =
                {
                    new PropertyDefinition { Name = "Id", Type = "int" },
                    new PropertyDefinition { Name = "TakenOn", Type = "DateTime" },
                    new PropertyDefinition { Name = "CheckedOn", Type = "DateTime" },
                    new PropertyDefinition { Name = "Value", Type = "decimal" },
                },
            },
        };

        var app = Compile(classes, "Reading");
        var item = app.Fill();

        var messages = app.Validate(item);
        Assert.IsEmpty(messages, string.Join("\n", messages));

        // One shared date, still midnight: a rule comparing two dates is satisfied by equal ones.
        var taken = (DateTime)app.Get(item, "TakenOn")!;
        Assert.AreEqual(taken, app.Get(item, "CheckedOn"));
        Assert.AreEqual(TimeSpan.Zero, taken.TimeOfDay);
        Assert.IsLessThan(DateTime.Today, taken);
    }

    /// <summary>The wiring: the scaffolder hands TestSupport the classes, rules and all.</summary>
    [TestMethod]
    public void A_scaffolded_apps_TestSupport_knows_the_accepted_value_list()
    {
        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        try
        {
            var request = new ScaffoldRequest(
                Tickets(), "Shop", workspace, target, PersistenceKind.Json,
                ImportedData: null, AppCustomization.Default with { SampleSeedData = false }, DateTime.Now.Year);

            var result = new AppScaffolder().Scaffold(request);
            Assert.IsTrue(result.Success, result.Message);
            Assert.IsNotNull(result.TestProjectPath);

            var support = File.ReadAllText(
                Path.Combine(Path.GetDirectoryName(result.TestProjectPath)!, "TestSupport.cs"));

            StringAssert.Contains(support, "[\"Ticket.Status\"] = () => \"Open\",");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>
    /// A key over several columns (an invoice line's invoice and line, a composite primary key):
    /// Fill's plain values would repeat a seeded record's combination, so the app's own
    /// round-trip test was refused as a duplicate.
    /// </summary>
    [TestMethod]
    public void Two_filled_records_do_not_share_a_combination_that_must_be_unique()
    {
        var line = new ClassDefinition
        {
            Name = "Line",
            Properties =
            {
                new PropertyDefinition { Name = "Id", Type = "int" },
                new PropertyDefinition { Name = "Batch", Type = "string" },
                new PropertyDefinition { Name = "Code", Type = "string" },
            },
            UniqueTogether = { new[] { "Batch", "Code" } },
        };

        var app = Compile([line], "Line");

        Assert.AreNotEqual(app.Get(app.Fill(), "Batch"), app.Get(app.Fill(), "Batch"));
    }

    [TestMethod]
    public void A_combination_holding_a_foreign_key_gets_a_new_row_for_it()
    {
        var pair = new ClassDefinition
        {
            Name = "Pair",
            Properties =
            {
                new PropertyDefinition { Name = "Id", Type = "int" },
                new PropertyDefinition { Name = "OwnerId", Type = "int" },
                new PropertyDefinition { Name = "ItemId", Type = "int" },
            },
            UniqueTogether = { new[] { "OwnerId", "ItemId" } },
        };

        var support = TestProjectTemplates.TestSupport("Acme", login: false, sessionAuth: false, seed: false, [pair]);

        StringAssert.Contains(support, "        \"Pair.OwnerId\",\n");
        Compile([pair], "Pair");
    }

    private static CompiledApp Compile(CandidateDomainModel model, string className)
        => Compile(DomainModelMapper.ToClassDefinitions(model), className);

    private static CompiledApp Compile(IReadOnlyList<ClassDefinition> classes, string className)
    {
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
            ("Tests/TestSupport.cs",
                TestProjectTemplates.TestSupport("Acme", login: false, sessionAuth: false, seed: false, classes)),
        });

        var compileErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        Assert.IsEmpty(compileErrors, "Compilation failed:\n" + string.Join("\n", compileErrors));

        return new CompiledApp(Assembly.Load(result.AssemblyBytes!), className);
    }

    private sealed class CompiledApp(Assembly assembly, string className)
    {
        private Type Entity => assembly.GetType("Acme.Models." + className)!;

        public object Fill()
        {
            var item = Activator.CreateInstance(Entity)!;
            assembly.GetType("Acme.Tests.TestSupport")!.GetMethod("Fill")!
                .MakeGenericMethod(Entity).Invoke(null, [item]);
            return item;
        }

        public object? Get(object item, string property) => Entity.GetProperty(property)!.GetValue(item);

        public IReadOnlyList<string> Validate(object item)
        {
            var validatorType = assembly.GetType("Acme.Validation." + className + "Validator")!;
            var contextFor = assembly.GetType("Acme.Validation.ValidationContext`1")!
                .MakeGenericType(Entity).GetMethod("For")!;
            var insert = assembly.GetType("Acme.Validation.ValidationOperation")!
                .GetField("Insert")!.GetValue(null)!;
            var lookup = Activator.CreateInstance(assembly.GetType("Acme.Validation.StoreEntityLookup")!);

            var context = contextFor.Invoke(null, [item, insert, lookup]);
            var outcome = validatorType.GetMethod("Validate")!.Invoke(Activator.CreateInstance(validatorType), [context])!;

            return (IReadOnlyList<string>)outcome.GetType().GetProperty("Messages")!.GetValue(outcome)!;
        }
    }
}
