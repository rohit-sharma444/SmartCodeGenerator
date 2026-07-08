using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class JsonStoreTemplateTests
{
    [TestMethod]
    public void Source_EmitsGenericStoreInGeneratedCodeNamespace()
    {
        var source = JsonStoreTemplate.Source();

        StringAssert.Contains(source, "namespace GeneratedCode");
        StringAssert.Contains(source, "class JsonStore<T>");
        StringAssert.Contains(source, "public T Add(T item)");
        StringAssert.Contains(source, "public bool Update(T item)");
        StringAssert.Contains(source, "public bool Delete(int id)");
        StringAssert.Contains(source, "AppContext.BaseDirectory");
    }
}
