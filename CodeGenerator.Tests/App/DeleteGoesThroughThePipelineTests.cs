using System.Linq;
using AppScaffolding;
using AppScaffolding.Auth;
using AppScaffolding.Screens;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The four emitted grid DELETE handlers move off a direct permission-matrix check and onto a
/// full pipeline run, so that a delete the pipeline would refuse (referential integrity arrives in
/// a later task) cannot slip through a screen the matrix alone would have let through.
/// </summary>
/// <remarks>
/// <c>validation: false</c> apps have no <c>ValidatorFactory</c>, no <c>ValidationPipeline</c>, no
/// <c>IValidator</c> and no <c>StoreEntityLookup</c> at all — see <c>AppScaffolder.cs:447-470</c> —
/// so every handler here is emitted in two shapes: the pipeline when validation is on, and today's
/// direct <c>Permissions.Allows(...)</c> check, unchanged, when it is off.
/// </remarks>
[TestClass]
public class DeleteGoesThroughThePipelineTests
{
    /// <summary>
    /// One handler's emitted source, using whatever arguments its own existing tests already use
    /// for the entity name and the login flag — only <paramref name="validation"/> varies here.
    /// </summary>
    private static string DeleteHandlerSource(string handler, bool validation) => handler switch
    {
        "MasterScreen" => MasterScreenTemplate.Source("Acme", validation, login: true),
        "ViewModel" => ViewModelTemplates.EntityViewModel("Acme", validation, login: true),
        "BlazorLogin" => BlazorScreenTemplates.MasterScreen("Acme", validation, login: true),
        "BlazorNoLogin" => BlazorScreenTemplates.MasterScreen("Acme", validation, login: false),
        _ => throw new ArgumentOutOfRangeException(nameof(handler), handler, "unknown handler"),
    };

    /// <summary>
    /// The Delete() method's own text, signature to matching closing brace. NOT the whole file:
    /// the plain grid's Save() already names ValidatorFactory and .Validate( for the Update it
    /// runs, so a substring check over the whole source would pass on that alone — vacuously,
    /// before this task touched Delete() at all. Found by running the red check against the
    /// pre-task baseline: three rows failed and "ViewModel" passed anyway.
    /// </summary>
    private static string ExtractDeleteMethod(string source)
    {
        var start = source.IndexOf("private void Delete()", StringComparison.Ordinal);
        if (start < 0)
            start = source.IndexOf("private async Task Delete()", StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException("no Delete() method found in source");

        var openBrace = source.IndexOf('{', start);
        var depth = 0;
        var i = openBrace;
        for (; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                break;
        }

        return source[start..(i + 1)];
    }

    /// <summary>
    /// The matrix alone is equivalent to a pipeline run only while Security is the only stage that
    /// can refuse a delete. Referential integrity ends that, and the deferred-save grid is exactly
    /// the front end that would then remove a row the pipeline refused and say nothing.
    /// </summary>
    [TestMethod]
    [DataRow("MasterScreen")]
    [DataRow("ViewModel")]
    [DataRow("BlazorLogin")]
    [DataRow("BlazorNoLogin")]
    public void Every_grid_delete_runs_the_pipeline(string handler)
    {
        // Explicit, not a default: this is the one flag that decides whether the pipeline exists
        // at all in the generated app, and this test is about the app where it does.
        var source = ExtractDeleteMethod(DeleteHandlerSource(handler, validation: true));

        StringAssert.Contains(source, "ValidationOperation.Delete");
        StringAssert.Contains(source, "ValidatorFactory.Create<");
        StringAssert.Contains(source, ".Validate(");
    }

    /// <summary>
    /// The companion the brief's test did not have. A <c>novalidation</c> app has no
    /// <c>ValidatorFactory</c> to call — generating one would be CS0246 in every such app, not a
    /// weaker version of the pipeline — so the handler must still refuse the way it always did,
    /// straight off the matrix.
    /// </summary>
    [TestMethod]
    [DataRow("MasterScreen")]
    [DataRow("ViewModel")]
    [DataRow("BlazorLogin")]
    [DataRow("BlazorNoLogin")]
    public void Every_grid_delete_still_asks_the_matrix_with_no_pipeline_to_run(string handler)
    {
        var source = ExtractDeleteMethod(DeleteHandlerSource(handler, validation: false));

        // Three handlers call Permissions.Allows directly; the Blazor login variant calls
        // MayDelete(state) instead -- the SAME matrix check, reached through the one call the
        // button's own visibility check also makes (see BlazorScreenTemplates.DeleteHandler's
        // remarks). Both are "still the matrix, not the pipeline".
        Assert.IsTrue(
            source.Contains("Permissions.Allows(", StringComparison.Ordinal)
            || source.Contains("MayDelete(state)", StringComparison.Ordinal),
            "expected a direct matrix check (Permissions.Allows or MayDelete)");
        StringAssert.Contains(source, "ValidationOperation.Delete");
        Assert.IsFalse(source.Contains("ValidatorFactory", StringComparison.Ordinal));
    }

    /// <summary>
    /// The floor the two tests above cannot reach: emitted TEXT can name the right calls and still
    /// fail to compile. This builds a small two-entity app's <c>Validation/</c> folder and screen
    /// output and compiles it for real, in its real namespaces — <see cref="EmittedSource.Flatten"/>
    /// strips <c>using</c>/<c>namespace</c> and would prove nothing about whether the pipeline call
    /// this task adds actually resolves against the seams phase 2 emits.
    /// </summary>
    /// <remarks>
    /// Scoped to the two desktop handlers: a Blazor screen is markup plus a <c>@code</c> block, not
    /// standalone C#, so <see cref="Compiler.CompileSources"/> has nothing to parse it as. The
    /// Blazor handlers are proved by the two text-only tests above, exactly as the rest of this
    /// suite proves Blazor delete behaviour by substring rather than by compiling a <c>.razor</c>
    /// file.
    /// </remarks>
    [TestMethod]
    public void The_pipeline_call_compiles_against_the_real_generated_seams()
    {
        var files = new List<(string Path, string Code)>
        {
            // Two related entities, so this is a small app rather than one bare stub -- Order
            // carries the foreign key a later task's referential-integrity check would read.
            ("Models/Entities.cs", ModelsStub),

            ("Screens/EntityRole.cs", ScreenTemplates.RoleEnum("Acme")),
            ("Screens/LineSet.cs", LineSetTemplate.Source("Acme")),
            ("Screens/FieldGroups.cs", FieldGroupsTemplate.Source("Acme")),

            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/StoreEntityLookup.cs", ValidatorTemplate.StoreEntityLookup("Acme")),
            ("Validation/ValidatorFactory.cs",
                ValidatorTemplate.Factory("Acme", Array.Empty<string>())),
            ("Validation/Permissions.cs",
                PermissionsTemplate.Source("Acme", new[] { "Customer" }, login: false)),

            ("Data/IStore.cs", StoreSeamTemplate.Source("Acme")),
            ("Data/SaveResult.cs", SaveResultTemplate.Source("Acme")),
            ("Data/StoreFactory.cs", StoreFactoryTemplate.Source("Acme", PersistenceKind.Json)),
            ("Data/JsonStore.cs", JsonStoreTemplate.Source("Acme.Data", auth: true)),

            ("Models/User.cs", AuthModelTemplates.User("Acme")),
            ("Auth/AppRole.cs", AuthModelTemplates.Role("Acme")),
            ("Auth/Session.cs", SessionTemplate.Source("Acme")),

            ("ViewModels/ObservableObject.cs", ViewModelTemplates.ObservableObject("Acme")),
            ("ViewModels/RelayCommand.cs", ViewModelTemplates.PortableRelayCommand("Acme")),
            ("ViewModels/IEntityViewModel.cs", ViewModelTemplates.EntityViewModelInterface("Acme")),
            ("ViewModels/EntityViewModel.cs",
                ViewModelTemplates.EntityViewModel("Acme", validation: true, login: true)),
            ("ViewModels/RecordEditorViewModel.cs",
                RecordEditorTemplate.Source("Acme", validation: true)),
            ("ViewModels/MasterScreenViewModel.cs",
                MasterScreenTemplate.Source("Acme", validation: true, login: true)),
        };

        var result = Compiler.CompileSources(files);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();

        Assert.IsEmpty(errors, "Compilation errors:\n" + string.Join("\n", errors));
        Assert.IsTrue(result.Success);
    }

    /// <summary>Customer is the master the plain grid and the entry screen delete from; Order
    /// carries the foreign key, so the fixture is two entities rather than one bare stub.</summary>
    private const string ModelsStub = """
        namespace Acme.Models;

        public sealed class Customer
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        public sealed class Order
        {
            public int Id { get; set; }
            public int CustomerId { get; set; }
        }
        """;
}
