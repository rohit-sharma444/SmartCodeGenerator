using System.Collections;
using System.Reflection;
using AppScaffolding;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using DataIngestion;
using DataIngestion.Schema;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// A generated app must accept its own seed data. Each test here imports a table, ACCEPTS every
/// rule the scan proposed, generates the model, the validator and the seed file exactly as the
/// scaffolder does, compiles them, loads the seed rows into the compiled model, and runs every
/// row through both the compiled validator and the DataAnnotations the front-ends enforce. Any
/// error is a rule the tool offered, or applied on its own, that the source's own rows break.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SeedRowsPassTheirOwnRulesTests
{
    [TestInitialize]
    public void Setup() => ValidationRegistry.Clear();

    [TestCleanup]
    public void Cleanup() => ValidationRegistry.Clear();

    /// <summary>
    /// A lookup over the seed rows themselves, so a uniqueness check sees the other rows the way
    /// the real StoreEntityLookup sees the store: by reflection, with Equals.
    /// </summary>
    private const string LookupOverTheSeedRows = """
namespace Acme.Validation;

public sealed class StoreEntityLookup : IEntityLookup
{
    public static System.Collections.Generic.List<object> Rows { get; } = new();

    public bool Exists<TEntity>(int id) where TEntity : class, new() => true;

    public int CountReferencing<TEntity>(string foreignKeyProperty, int id)
        where TEntity : class, new() => 0;

    public bool AnyOtherWith<TEntity>(string propertyName, object? value, int exceptId)
        where TEntity : class, new()
    {
        var property = typeof(TEntity).GetProperty(propertyName)!;
        var id = typeof(TEntity).GetProperty("Id")!;

        foreach (var row in Rows)
        {
            if ((int)id.GetValue(row)! == exceptId)
                continue;

            if (Equals(property.GetValue(row), value))
                return true;
        }

        return false;
    }
}
""";

    [TestMethod]
    public void A_unique_number_column_with_blank_cells_accepts_its_own_rows()
    {
        // Rank holds no two equal values, so uniqueness is offered. The two blank cells are
        // written to the seed as nothing at all, so both rows load with Rank 0 -- and a check that
        // counts 0 as a value refuses the second of them against the first.
        var table = Table("Product", ["Name", "Rank"],
            ["Widget", "1"],
            ["Gadget", "2"],
            ["Gizmo", ""],
            ["Doohickey", ""]);

        var errors = ErrorsFromTheSeedRows(table, "Product");

        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    [TestMethod]
    public void A_date_pair_with_a_blank_end_accepts_its_own_rows()
    {
        // Every row that HAS both dates starts on or before it ends, so a cross-field rule is
        // offered. The open booking's blank EndDate loads as 0001-01-01, which its StartDate is
        // after -- so a scan that skipped the blank row offered a rule that row breaks.
        var table = Table("Booking", ["Guest", "StartDate", "EndDate"],
            ["Ann", "2026-01-01", "2026-01-10"],
            ["Bob", "2026-02-01", "2026-02-20"],
            ["Cy", "2026-03-01", ""]);

        var errors = ErrorsFromTheSeedRows(table, "Booking");

        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    [TestMethod]
    public void A_calculation_with_a_blank_operand_accepts_its_own_rows()
    {
        // Total is Net plus Tax on every row with all three, so the calculation is offered. The
        // row with no Tax loads it as 0, and 100 is not 100 plus 0 when its Total says 120.
        var table = Table("Invoice", ["Reference", "Net", "Tax", "Total"],
            ["A", "100", "20", "120"],
            ["B", "50", "10", "60"],
            ["C", "100", "", "120"]);

        var errors = ErrorsFromTheSeedRows(table, "Invoice");

        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    [TestMethod]
    public void A_percentage_rule_accepts_its_own_rows_under_a_comma_decimal_culture()
    {
        // Total always equals Net times (1 plus Rate). The emitted check reads each operand back
        // with this.Property.ToString() -- under a culture that writes decimals with a comma,
        // "80.00" becomes "80,00", which System.Globalization.CultureInfo.InvariantCulture then
        // parses as the THOUSANDS-GROUPED integer 8000, not the number the row actually holds. A
        // sum rule would still balance after every operand is scaled the same way; a product does
        // not, which is why this needs a percentage rule to show up.
        var table = Table("Order", ["Reference", "Net", "Rate", "Total"],
            ["A", "80.00", "0.25", "100.00"],
            ["B", "200.00", "0.10", "220.00"]);

        var original = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture =
            System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var errors = ErrorsFromTheSeedRows(table, "Order");
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void A_phone_column_with_a_placeholder_value_accepts_its_own_rows()
    {
        // A column named for its format gets [Phone] purely by name, whatever the data actually
        // holds. "N/A" is a placeholder every source like this has somewhere, and it is not a
        // phone number by anyone's definition -- so an unconditional [Phone] rejects a row the
        // source already contains and the user never touched.
        var table = Table("Contact", ["Name", "Phone"],
            ["Ann", "+44 20 7946 0958"],
            ["Bob", "N/A"]);

        var errors = ErrorsFromTheSeedRows(table, "Contact");

        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    [TestMethod]
    public void A_long_text_column_accepts_its_own_longest_row()
    {
        // No SQL schema means no MaxLength fact, so InferValidation falls back to a 200-character
        // default -- a bound that has nothing to do with what the source column actually holds.
        var longValue = new string('x', 350);
        var table = Table("Article", ["Title", "Body"],
            ["First", longValue],
            ["Second", "short"]);

        var errors = ErrorsFromTheSeedRows(table, "Article");

        Assert.IsEmpty(errors, string.Join("\n", errors));
    }

    /// <summary>A table whose column types are inferred from its rows, as the readers do.</summary>
    internal static TabularTable Table(string name, string[] columns, params string?[][] rows)
    {
        var table = new TabularTable { Name = name };

        foreach (var column in columns)
            table.Columns.Add(new TabularColumn { Name = column });

        foreach (var row in rows)
            table.Rows.Add(row);

        foreach (var column in table.Columns)
            column.Type = ColumnTypeInference.Infer(table.ColumnValues(column.Name), column.Name);

        return table;
    }

    /// <summary>
    /// Every error the generated app reports against its own seed rows for
    /// <paramref name="className"/>, with every proposal the scan made accepted.
    /// </summary>
    internal static IReadOnlyList<string> ErrorsFromTheSeedRows(TabularTable table, string className)
    {
        var model = DataModelBuilder.Build([table], []);

        foreach (var entity in model.Entities)
        {
            foreach (var proposal in entity.Proposals)
                proposal.Accepted = true;

            foreach (var field in entity.Fields)
            foreach (var proposal in field.Proposals)
                proposal.Accepted = true;
        }

        var classes = DomainModelMapper.ToClassDefinitions(model, out var classNames);
        var definition = classes.Single(c => c.Name == className);

        var seed = SeedFileWriter.Files(classes, classNames, TabularImport.ToImportedData([table]))
            .Single(f => f.FileName == className + ".json").Json;

        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("GlobalUsings.cs", "global using System;\nglobal using System.Collections.Generic;"),
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", [], login: false)),
            ("Validation/StoreEntityLookup.cs", LookupOverTheSeedRows),
            ("Models/" + className + ".cs", RoslynClassGenerator.Generate(definition, "Acme.Models")),
            ("Validation/" + className + "Validator.Generated.cs",
                ValidatorTemplate.Generated(
                    "Acme", definition, [], referencesRequired: false,
                    businessRules: definition.BusinessRules)),
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
        var listType = typeof(List<>).MakeGenericType(entityType);
        var rows = ((IList)System.Text.Json.JsonSerializer.Deserialize(seed, listType)!)
            .Cast<object>()
            .ToList();

        var lookupType = assembly.GetType("Acme.Validation.StoreEntityLookup")!;
        var stored = (IList)lookupType.GetProperty("Rows")!.GetValue(null)!;
        foreach (var row in rows)
            stored.Add(row);

        var validatorType = assembly.GetType("Acme.Validation." + className + "Validator")!;
        var validator = Activator.CreateInstance(validatorType)!;
        var validate = validatorType.GetMethod("Validate")!;
        var contextFor = assembly.GetType("Acme.Validation.ValidationContext`1")!
            .MakeGenericType(entityType).GetMethod("For")!;
        var update = assembly.GetType("Acme.Validation.ValidationOperation")!
            .GetField("Update")!.GetValue(null)!;

        var errors = new List<string>();

        foreach (var row in rows)
        {
            var id = entityType.GetProperty("Id")!.GetValue(row);

            var context = contextFor.Invoke(null, [row, update, Activator.CreateInstance(lookupType)]);
            var outcome = validate.Invoke(validator, [context])!;
            foreach (var message in (IReadOnlyList<string>)outcome.GetType()
                         .GetProperty("Messages")!.GetValue(outcome)!)
                errors.Add($"row {id}: {message}");

            // The DataAnnotations the console, WinForms and WPF front-ends enforce through
            // Validator.TryValidateObject: [StringLength], [Required], [EmailAddress], [Phone].
            var annotations = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
                row, new System.ComponentModel.DataAnnotations.ValidationContext(row), annotations,
                validateAllProperties: true);
            foreach (var annotation in annotations)
                errors.Add($"row {id}: {annotation.ErrorMessage}");
        }

        return errors;
    }
}
