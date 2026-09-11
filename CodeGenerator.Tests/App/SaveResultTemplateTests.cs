using AppScaffolding;

namespace CodeGenerator.Tests.App;

/// <summary>SaveResult is the seam every generated write path returns through, so it must compile
/// on its own before anything is built on top of it.</summary>
[TestClass]
public class SaveResultTemplateTests
{
    [TestMethod]
    public void Emitted_save_result_compiles()
    {
        // SaveResult now names ValidationError from the Validation namespace, so the file that
        // declares it joins the compile — and that file in turn names IEntityLookup, so its
        // interface has to come along too, or this is compiling neither file on its own.
        EmittedSource.AssertCompiles(EmittedSource.Flatten(
            ValidationContextTemplate.Source("Acme"),
            ValidatorTemplate.EntityLookupInterface("Acme"),
            SaveResultTemplate.Source("Acme")));
    }

    [TestMethod]
    public void Emitted_save_result_declares_the_data_namespace()
    {
        Assert.Contains("namespace Acme.Data", SaveResultTemplate.Source("Acme"));
    }
}
