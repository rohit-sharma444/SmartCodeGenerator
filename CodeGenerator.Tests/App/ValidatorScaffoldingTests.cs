using System.IO;
using System.Linq;
using AppScaffolding;
using AppScaffolding.Targets;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>The Validation/ folder is written, and a hand-edited .Custom.cs survives a rebuild.</summary>
[TestClass]
public class ValidatorScaffoldingTests
{
    /// <summary>Two entities, one pointing at the other: Book carries an AuthorId foreign key and
    /// the relationship that gives it a nav property, so the emitted validator exercises the
    /// lookup branch as well as the plain rule branches.</summary>
    private static CandidateDomainModel TwoEntities()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Author" });
        var book = new CandidateEntity
        {
            Name = "Book",
            Fields =
            {
                new CandidateField { Name = "Title", Type = "string" },
                new CandidateField { Name = "AuthorId", Type = "int" },
            },
        };
        book.Relationships.Add(new CandidateRelationship { Target = "Author", Role = "wrote" });
        model.Entities.Add(book);
        return model;
    }

    private static string NewWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "scg-validators-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>ClassLibraryTarget needs no WPF or ASP.NET tooling to create.</summary>
    private static ScaffoldResult Scaffold(CandidateDomainModel model, string workspace)
    {
        var result = new AppScaffolder().Scaffold(model, "Shop", workspace, new ClassLibraryTarget());
        Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);
        return result;
    }

    [TestMethod]
    public void Scaffolding_writes_the_validation_folder()
    {
        var workspace = NewWorkspace();
        try
        {
            var result = Scaffold(TwoEntities(), workspace);

            var validation = Path.Combine(result.ProjectPath, "Validation");
            Assert.IsTrue(File.Exists(Path.Combine(validation, "IValidator.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(validation, "IEntityLookup.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(validation, "StoreEntityLookup.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(validation, "ValidatorFactory.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(result.ProjectPath, "Data", "SaveResult.cs")));
        }
        finally
        {
            Cleanup(workspace);
        }
    }

    [TestMethod]
    public void Scaffolding_writes_both_halves_of_every_models_validator()
    {
        var workspace = NewWorkspace();
        try
        {
            var result = Scaffold(TwoEntities(), workspace);
            var validation = Path.Combine(result.ProjectPath, "Validation");

            foreach (var entity in new[] { "Author", "Book" })
            {
                Assert.IsTrue(File.Exists(Path.Combine(validation, entity + "Validator.Generated.cs")),
                    entity + "Validator.Generated.cs should exist");
                Assert.IsTrue(File.Exists(Path.Combine(validation, entity + "Validator.Custom.cs")),
                    entity + "Validator.Custom.cs should exist");
            }

            // The foreign key reached the validator, so TargetsOf really was fed the mapped classes.
            var bookValidator = File.ReadAllText(Path.Combine(validation, "BookValidator.Generated.cs"));
            StringAssert.Contains(bookValidator, "_lookup.Exists<Author>(item.AuthorId)");
        }
        finally
        {
            Cleanup(workspace);
        }
    }

    [TestMethod]
    public void A_hand_edited_custom_validator_survives_regeneration()
    {
        var workspace = NewWorkspace();
        try
        {
            var model = TwoEntities();
            var first = Scaffold(model, workspace);

            var custom = Directory
                .GetFiles(Path.Combine(first.ProjectPath, "Validation"), "*Validator.Custom.cs")
                .First();
            var generated = custom.Replace(".Custom.cs", ".Generated.cs");

            File.WriteAllText(custom, "// MY OWN RULES");
            File.WriteAllText(generated, "// STALE");

            Scaffold(model, workspace);

            Assert.AreEqual("// MY OWN RULES", File.ReadAllText(custom));
            Assert.AreNotEqual("// STALE", File.ReadAllText(generated));
        }
        finally
        {
            Cleanup(workspace);
        }
    }

    private static void Cleanup(string workspace)
    {
        try
        {
            Directory.Delete(workspace, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
