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
        EmittedSource.AssertCompiles(SaveResultTemplate.Source("Acme"));
    }

    [TestMethod]
    public void Emitted_save_result_declares_the_data_namespace()
    {
        Assert.Contains("namespace Acme.Data", SaveResultTemplate.Source("Acme"));
    }
}
