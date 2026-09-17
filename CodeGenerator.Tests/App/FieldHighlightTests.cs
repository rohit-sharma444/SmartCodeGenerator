using System.Linq;
using System.Reflection;
using AppScaffolding;
using AppScaffolding.Screens;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The two editors that write through the store rather than through <c>Repository&lt;T&gt;</c> run
/// the pipeline themselves — and now keep WHICH property each refusal belongs to, so a form can
/// mark the field that failed instead of showing one sentence at the top.
/// </summary>
/// <remarks>
/// These two were the last callers of the removed <c>IValidator&lt;T&gt;.Validate(T item)</c>, so
/// the attribution and the migration are one change: <c>ValidationOutcome.Errors</c> is the only
/// thing that ever knew which property broke.
/// </remarks>
[TestClass]
public class FieldHighlightTests
{
    /// <summary>
    /// The Blazor editor marks the input the pipeline named.
    /// </summary>
    /// <remarks>
    /// The class is Bootstrap's own <c>is-invalid</c>, not one of this app's: the generated Blazor
    /// project comes from <c>dotnet new blazor</c>, whose stylesheet and Bootstrap are on disk and
    /// have never been written to by this generator — there is no stylesheet of ours to add a rule
    /// to, and inventing one would be a new kind of project fixup nobody asked for.
    /// </remarks>
    [TestMethod]
    public void TheBlazorEditorMarksTheFailingInput()
    {
        // The EDITOR, not MasterScreen: the master screen is a read-only list whose only input is
        // the search box. The inputs a validator can refuse are on RecordEditor.
        var source = BlazorScreenTemplates.RecordEditor("Acme", validation: true, login: false);

        StringAssert.Contains(source, "is-invalid");
        StringAssert.Contains(source, "class=\"form-control @CssFor(field)\"");
        StringAssert.Contains(source, "class=\"form-check-input d-block @CssFor(field)\"");

        // Populated from the outcome's per-property errors, and cleared at the start of every
        // save attempt so a fixed field stops being marked.
        StringAssert.Contains(source, "_invalid.Add(property)");
        StringAssert.Contains(source, "_invalid.Clear();");

        // ...and cleared again BEFORE Save, which is Load: this component is reused when the URL
        // moves from one record to another, so a mark left from the record before would sit on a
        // field of this one with nothing on screen saying why.
        Assert.IsLessThan(
            source.IndexOf("private void Save()", StringComparison.Ordinal),
            source.IndexOf("_invalid.Clear();", StringComparison.Ordinal),
            "the marks are not cleared when the editor is reused for another record");
    }

    /// <summary>
    /// The desktop editor keeps each refusal against the property it belongs to.
    /// </summary>
    /// <remarks>
    /// Asserting the substring "Property" would pass before this task and after it — the emitted
    /// source already says <c>OnPropertyChanged</c>, <c>p.PropertyType</c> and
    /// <c>GetProperty("Id")</c>. What only the new behaviour produces is the outcome being kept
    /// per property, and the one-sentence-only line being gone.
    /// </remarks>
    [TestMethod]
    public void TheDesktopEditorNamesTheFailingProperty()
    {
        var source = RecordEditorTemplate.Source("Acme", validation: true);

        // On the pipeline: a context, an operation, and an outcome that carries attribution.
        // Built with the full constructor, not ValidationContext<T>.For -- that factory's own
        // doc comment says it is for tests and callers with no real acting user, and this editor
        // has one (validation-phase-3 task 15).
        StringAssert.Contains(source, "new ValidationContext<T>(");
        StringAssert.Contains(source, "ActingUser.Role(), ActingUser.Id()");
        StringAssert.Contains(source, "IsNew ? ValidationOperation.Insert : ValidationOperation.Update");
        StringAssert.Contains(source, "foreach (var error in outcome.Errors)");
        StringAssert.Contains(source, "_errors.TryAdd(error.Property ?? string.Empty, error.Message);");

        // ...and reachable by name, which is what lets a view put the complaint beside its field.
        StringAssert.Contains(source, "public string ErrorFor(string property)");

        // The old seam, and the sentence that was all it could offer.
        Assert.DoesNotContain("_validator.Validate(Draft)", source);
        Assert.DoesNotContain("Status = $\"Cannot save: {errors[0]}\";", source);
    }

    /// <summary>
    /// A refused save names the field, over emitted code that is compiled and RUN.
    /// </summary>
    /// <remarks>
    /// Substring assertions are how this branch hid real breakage: the editor's previous validate
    /// call passed every one of them while being uncompilable against the seam Task 1 replaced.
    /// </remarks>
    [TestMethod]
    public void ARefusedSaveNamesTheFieldThatFailed()
    {
        var assembly = RecordEditorTests.EmitValidating();

        var bookType = assembly.GetType("Book")!;
        var editorType = assembly.GetType("RecordEditorViewModel`1")!.MakeGenericType(bookType);
        var editor = Activator.CreateInstance(editorType, [null, true, null])!;

        RecordEditorTests.Execute(editor, "SaveCommand");

        Assert.AreEqual(
            "Title is required.",
            ErrorFor(editor, "Title"),
            "the refusal was not kept against the property it belongs to");

        // A property nobody complained about is not marked.
        Assert.AreEqual(string.Empty, ErrorFor(editor, "Id"));
    }

    /// <summary>
    /// The migrated editor compiles, and runs, in the namespaces a generated app really declares.
    /// </summary>
    /// <remarks>
    /// <see cref="EmittedSource.Flatten"/> strips every <c>namespace</c> and every
    /// <c>using Acme.*;</c> line, so the fixtures built on it compile the whole application into
    /// the global namespace: a wrong namespace or a missing import is invisible there. This one
    /// keeps each file in its own real namespace as its own syntax tree, and the validator is the
    /// GENERATED one over the real pipeline rather than a stub — so the attribution asserted here
    /// is the attribution <c>ValidatorTemplate</c> emits.
    /// <para>
    /// Only <c>StoreFactory</c> is a fixture: the real one resolves a JsonStore, which would put
    /// this test on the file system.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheMigratedEditorCompilesAndRefusesInItsOwnNamespaces()
    {
        var definition = new ClassDefinition { Name = "Book" };
        definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

        var title = new PropertyDefinition { Name = "Title", Type = "string" };
        title.Rules.Add(new PropertyRule { Name = "required" });
        definition.Properties.Add(title);

        var files = new (string Path, string Code)[]
        {
            ("Models/Book.cs", BookModel),
            ("Data/IStore.cs", StoreSeamTemplate.Source("Acme")),
            ("Data/StoreFactory.cs", MemoryStoreFixture),
            ("Screens/LineSet.cs", LineSetTemplate.Source("Acme")),
            ("Screens/FieldGroups.cs", FieldGroupsTemplate.Source("Acme")),
            ("ViewModels/ObservableObject.cs", ViewModelTemplates.ObservableObject("Acme")),
            ("ViewModels/RelayCommand.cs", ViewModelTemplates.PortableRelayCommand("Acme")),
            ("ViewModels/RecordEditorViewModel.cs",
                RecordEditorTemplate.Source("Acme", validation: true)),
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/ValidationPipeline.cs", ValidationPipelineTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/StoreEntityLookup.cs", ValidatorTemplate.StoreEntityLookup("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", ["Book"], login: false)),
            ("Validation/ActingUser.cs", ActingUserTemplate.Source("Acme", sessionAuth: false)),
            ("Validation/ValidatorFactory.cs", ValidatorTemplate.Factory("Acme", ["Book"])),
            ("Validation/BookValidator.Generated.cs",
                ValidatorTemplate.Generated("Acme", definition, [], referencesRequired: false)),
            ("Validation/BookValidator.Custom.cs", ValidatorTemplate.CustomStub("Acme", "Book")),
        };

        var result = Compiler.CompileSources(files);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();

        Assert.IsEmpty(errors, "Compilation errors:\n" + string.Join("\n", errors));
        Assert.IsTrue(result.Success);

        var assembly = Assembly.Load(result.AssemblyBytes!);
        var bookType = assembly.GetType("Acme.Models.Book")!;
        var editorType = assembly.GetType("Acme.ViewModels.RecordEditorViewModel`1")!
            .MakeGenericType(bookType);

        var editor = Activator.CreateInstance(editorType, [null, true, null])!;

        RecordEditorTests.Execute(editor, "SaveCommand");

        Assert.AreEqual(
            "Title is required.",
            ErrorFor(editor, "Title"),
            "the generated validator's attribution did not reach the editor");

        StringAssert.Contains(
            (string)editorType.GetProperty("Status")!.GetValue(editor)!, "Title is required.");
    }

    /// <summary>What the editor kept against one property, read the way a view would.</summary>
    private static string ErrorFor(object editor, string property)
        => (string)editor.GetType().GetMethod("ErrorFor")!.Invoke(editor, [property])!;

    /// <summary>The whole reason these fragments are raw literals: one rebuilt with "\n" would
    /// leave the output one CR per line short on a CRLF checkout, and no other assertion here
    /// would see it.</summary>
    [TestMethod]
    public void TheNewFragmentsLeaveTheLineEndingsAlone()
    {
        var sources = new List<string>();

        foreach (var validation in new[] { true, false })
            sources.Add(RecordEditorTemplate.Source("Acme", validation));

        foreach (var login in new[] { true, false })
        {
            foreach (var blazorValidation in new[] { true, false })
            {
                sources.Add(BlazorScreenTemplates.MasterScreen("Acme", blazorValidation, login));
                sources.Add(BlazorScreenTemplates.TransactionScreen("Acme", blazorValidation, login));
                sources.Add(BlazorScreenTemplates.RecordEditor("Acme", blazorValidation, login));
            }
        }

        foreach (var source in sources)
        {
            var newlines = source.Count(c => c == '\n');
            var carriageReturns = source.Count(c => c == '\r');

            Assert.IsTrue(
                carriageReturns == 0 || carriageReturns == newlines,
                $"mixed line endings: {newlines} newline(s), {carriageReturns} carriage return(s)");
        }
    }

    /// <summary>One model with one required field, in the namespace the generator emits models
    /// into — so the generated validator's own <c>using Acme.Models;</c> has to resolve.</summary>
    private const string BookModel = """
namespace Acme.Models;

public sealed class Book
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
}
""";

    /// <summary>
    /// A store over a static list, under the name and namespace the real
    /// <c>StoreFactoryTemplate</c> emits.
    /// </summary>
    /// <remarks>
    /// The one fixture in that compilation: the real factory resolves a JsonStore, and this test
    /// is about the editor's validation, not about the file system. Everything the editor and the
    /// validator actually reach through it — <c>IStore&lt;T&gt;</c>, <c>StoreWrite</c> — is the
    /// emitted seam.
    /// </remarks>
    private const string MemoryStoreFixture = """
using System.Collections.Generic;

namespace Acme.Data;

public sealed class MemoryStore<T> : IStore<T> where T : class, new()
{
    public static readonly List<T> Rows = new();

    public List<T> GetAll() => new(Rows);

    public StoreWrite SaveAll(List<T> all)
    {
        Rows.Clear();
        Rows.AddRange(all);

        return StoreWrite.Ok;
    }

    public StoreWrite SaveAllUnchecked(List<T> all) => SaveAll(all);

    public int NextId(IEnumerable<T> existing) => Rows.Count + 1;

    public T NewItem(IEnumerable<T> existing) => new();
}

public static class StoreFactory
{
    public static IStore<T> Create<T>() where T : class, new() => new MemoryStore<T>();
}
""";
}
