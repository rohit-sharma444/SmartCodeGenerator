using AppScaffolding;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ValidationContextTemplateTests
{
    [TestMethod]
    public void TheContextAndTheWidenedSeam_CompileTogether()
    {
        // Compiled, not asserted by substring: these four types are named by every later task,
        // and a record whose positional parameters do not line up is invisible to a Contains.
        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
        });

        var errors = string.Join("\n",
            result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => e.ToString()));

        Assert.IsTrue(result.Success, $"Compilation failed:\n{errors}");
    }

    [TestMethod]
    public void TheOperationEnum_CarriesAllFour()
    {
        var source = ValidationContextTemplate.Source("Acme");

        StringAssert.Contains(source, "enum ValidationOperation");
        StringAssert.Contains(source, "Insert");
        StringAssert.Contains(source, "Update");
        StringAssert.Contains(source, "Delete");

        // Post has no caller until phase 6's posting gate. It is here deliberately, so the
        // enum a generated app exposes does not have to widen later.
        StringAssert.Contains(source, "Post");
    }

    [TestMethod]
    public void TheContext_NamesNoAuthType()
    {
        // The console, webapi, worker and classlib targets generate the Validation folder but
        // NO auth core, so there is no User type in them. A context naming it would not
        // compile in four of the seven targets; the role and the id are what the matrix needs.
        var source = ValidationContextTemplate.Source("Acme");

        Assert.DoesNotContain("Acme.Auth", source);
        Assert.DoesNotContain("User? User", source);
        StringAssert.Contains(source, "string? UserRole");
        StringAssert.Contains(source, "int? UserId");
    }

    [TestMethod]
    public void AnOutcomeWithNoErrors_IsValid()
    {
        var source = ValidationContextTemplate.Source("Acme");

        StringAssert.Contains(source, "public bool IsValid => Errors.Count == 0;");
    }

    [TestMethod]
    public void TheSeam_TakesAContextAndReturnsAnOutcome()
    {
        // Replaced, not supplemented. A convenience overload taking a bare item would have to
        // invent an operation and a null role, so a caller reaching for it gets an inert
        // Security stage and a validator reporting success over a check that never ran.
        var source = ValidatorTemplate.ValidatorInterface("Acme");

        StringAssert.Contains(source, "ValidationOutcome Validate(ValidationContext<T> context);");
        Assert.DoesNotContain("Validate(T item)", source);
    }

    [TestMethod]
    public void TheLookup_CanCountReferences()
    {
        var source = ValidatorTemplate.EntityLookupInterface("Acme");

        StringAssert.Contains(source,
            "int CountReferencing<TEntity>(string foreignKeyProperty, int id)");
    }
}
